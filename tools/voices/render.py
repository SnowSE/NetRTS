"""Render the 3D view's commentary (lines.json) in 16 English voices.

    uv venv -p 3.12 .venv && uv pip install -p .venv piper-tts
    .venv/Scripts/python tools/voices/render.py

Needs ffmpeg on the path. The voices are Piper (https://github.com/OHF-Voice/piper1-gpl) neural
text-to-speech, running locally with its VCTK model: 109 speakers from across the English-speaking
world (CC BY 4.0, https://datashare.ed.ac.uk/handle/10283/3443). Sixteen are used, one per player slot.

Each line is synthesized, trimmed and re-encoded as small constant-bitrate MP3. Each voice's clips are
then joined into one file (MP3 frames can simply be concatenated) and
src/NetRts.Server/wwwroot/audio/voices.json records where every clip starts and how long it is, so
the browser fetches one file per voice and decodes clips on demand. Clips are cached in
tools/voices/.cache, so re-running after editing a few lines is quick.
"""
import hashlib
import json
import subprocess
import urllib.request
import wave
from concurrent.futures import ThreadPoolExecutor
from pathlib import Path

from piper import PiperVoice, SynthesisConfig

HERE = Path(__file__).resolve().parent
ROOT = HERE.parents[1]
OUT = ROOT / "src" / "NetRts.Server" / "wwwroot" / "audio"
CACHE = HERE / ".cache"
MODEL = "en_GB-vctk-medium"
MODEL_URL = "https://huggingface.co/rhasspy/piper-voices/resolve/main/en/en_GB/vctk/medium/"

# key, VCTK speaker, name, accent, speed (length scale: lower is faster), expressiveness (noise scale)
VOICES = [
    ("en-m", "p226", "Oliver", "English", 0.92, 0.75),
    ("en-f", "p225", "Grace", "English", 0.92, 0.75),
    ("sc-m", "p237", "Callum", "Scottish", 0.95, 0.75),
    ("sc-f", "p234", "Isla", "Scottish", 0.92, 0.75),
    ("ie-m", "p245", "Liam", "Irish", 0.92, 0.75),
    ("ie-f", "p288", "Aoife", "Irish", 0.92, 0.75),
    ("ni-m", "p292", "Ciaran", "Northern Irish", 0.95, 0.75),
    ("wa-f", "p253", "Seren", "Welsh", 0.92, 0.75),
    ("in-m", "p251", "Arjun", "Indian", 0.92, 0.75),
    ("in-f", "p248", "Priya", "Indian", 0.92, 0.75),
    ("us-m", "p311", "Jake", "American", 0.9, 0.8),
    ("us-f", "p294", "Megan", "American", 0.9, 0.8),
    ("ca-m", "p302", "Ethan", "Canadian", 0.92, 0.75),
    ("za-f", "p314", "Thandi", "South African", 0.92, 0.75),
    ("au-m", "p326", "Mitch", "Australian", 0.9, 0.8),
    ("nz-f", "p335", "Aroha", "New Zealand", 0.92, 0.75),
]
CREDIT = "Voices: Piper text-to-speech with the VCTK corpus (CC BY 4.0), University of Edinburgh CSTR."


def load_lines():
    data = json.loads((HERE / "lines.json").read_text(encoding="utf-8"))
    lines = []
    for cat, items in data.items():
        if cat.startswith("_"):
            continue
        for item in items:
            theme, text = None, item
            for t in ("snow", "classic"):
                if item.startswith(t + ":"):
                    theme, text = t, item[len(t) + 1:]
            lines.append({"cat": cat, "text": text, **({"theme": theme} if theme else {})})
    return lines


def model():
    folder = CACHE / "models"
    folder.mkdir(parents=True, exist_ok=True)
    for name in (f"{MODEL}.onnx", f"{MODEL}.onnx.json"):
        if not (folder / name).exists():
            print(f"downloading {name}")
            urllib.request.urlretrieve(MODEL_URL + name, folder / name)
    voice = PiperVoice.load(str(folder / f"{MODEL}.onnx"))
    speakers = json.loads((folder / f"{MODEL}.onnx.json").read_text(encoding="utf-8"))["speaker_id_map"]
    return voice, speakers


def clip_path(key, speaker, speed, noise, text):
    h = hashlib.sha1(f"{MODEL}|{speaker}|{speed}|{noise}|{text}".encode()).hexdigest()[:16]
    return CACHE / key / f"{h}.wav"


def encode(src):
    """Trim the silence at both ends and re-encode as 32 kbit/s mono CBR, without a Xing header."""
    out = subprocess.run(
        ["ffmpeg", "-v", "error", "-i", str(src),
         "-af", "silenceremove=start_periods=1:start_threshold=-45dB,areverse,silenceremove=start_periods=1:start_threshold=-45dB,areverse",
         "-ac", "1", "-ar", "22050", "-c:a", "libmp3lame", "-b:a", "32k", "-write_xing", "0", "-id3v2_version", "0",
         "-f", "mp3", "-"],
        check=True, capture_output=True)
    return out.stdout


def main():
    lines = load_lines()
    voice, speakers = model()
    todo = 0
    for key, speaker, _, _, speed, noise in VOICES:
        (CACHE / key).mkdir(parents=True, exist_ok=True)
        cfg = SynthesisConfig(speaker_id=speakers[speaker], length_scale=speed, noise_scale=noise, noise_w_scale=0.85)
        for line in lines:
            path = clip_path(key, speaker, speed, noise, line["text"])
            if path.exists():
                continue
            with wave.open(str(path), "wb") as w:
                voice.synthesize_wav(line["text"], w, syn_config=cfg)
            todo += 1
        print(f"{key}: synthesized")
    print(f"{todo} new clips ({len(VOICES)} voices x {len(lines)} lines)")

    (OUT / "voices").mkdir(parents=True, exist_ok=True)
    manifest = {"credit": CREDIT, "lines": lines, "voices": []}
    total = 0
    with ThreadPoolExecutor(8) as pool:
        for key, speaker, name, accent, speed, noise in VOICES:
            blobs = list(pool.map(encode, [clip_path(key, speaker, speed, noise, l["text"]) for l in lines]))
            clips, offset = [], 0
            with open(OUT / "voices" / f"{key}.mp3", "wb") as f:
                for b in blobs:
                    f.write(b)
                    clips.append([offset, len(b)])
                    offset += len(b)
            total += offset
            manifest["voices"].append({"key": key, "name": name, "accent": accent, "file": f"/audio/voices/{key}.mp3", "clips": clips})
            print(f"{key} ({name}, {accent}): {offset / 1024:.0f} KB")
    (OUT / "voices.json").write_text(json.dumps(manifest, separators=(",", ":")), encoding="utf-8")
    print(f"wrote {OUT / 'voices.json'}: {len(lines)} lines x {len(VOICES)} voices, {total / 1048576:.1f} MB of audio")


if __name__ == "__main__":
    main()
