"""Build the 3D view's commentary audio: every line in lines.json, in every voice in cast.json.

    python tools/voices/voices.py plan       # list the clips to render (.cache/jobs.json)
    dotnet run --project tools/voices/KokoroRender -c Release   # Kokoro voices, through LocalAI
    <piper venv>/python tools/voices/voices.py piper            # Piper voices (needs piper-tts)
    python tools/voices/voices.py pack       # trim, encode, join; write wwwroot/audio

Chatterbox (more excited delivery, rendered on a GPU machine from a reference clip of each voice):

    python tools/voices/voices.py chatterbox-jobs 0.7 0.3   # .cache/chatterbox-jobs.json: every clip to render
    ... render each job's text as <file> with Chatterbox, exaggeration and cfg_weight as given ...
    python tools/voices/voices.py import <folder> 0.7 0.3   # take the clips; cast.json switches to them

Rendered clips are cached in tools/voices/.cache/clips (named by a hash of engine, voice and text),
so after editing a few lines only those are rendered again. Packing needs ffmpeg on the path: each
clip is trimmed and re-encoded as small constant-bitrate MP3, each voice's clips are joined into one
file (MP3 frames can simply be concatenated), and src/NetRts.Server/wwwroot/audio/voices.json records
where every clip starts and how long it is, so the browser fetches one file per voice and decodes
clips on demand.

The Kokoro side needs the LocalAI repository next to this one (D:/LocalAI beside D:/NetRTS; see
KokoroRender.csproj). For Piper: `uv venv -p 3.12 .venv && uv pip install -p .venv piper-tts`.
"""
import hashlib
import json
import subprocess
import sys
import urllib.request
import wave
from concurrent.futures import ThreadPoolExecutor
from pathlib import Path

HERE = Path(__file__).resolve().parent
ROOT = HERE.parents[1]
OUT = ROOT / "src" / "NetRts.Server" / "wwwroot" / "audio"
CACHE = HERE / ".cache"
JOBS = CACHE / "jobs.json"
PIPER_MODEL = "en_GB-vctk-medium"
PIPER_URL = "https://huggingface.co/rhasspy/piper-voices/resolve/main/en/en_GB/vctk/medium/"
CREDITS = {
    "kokoro": "Kokoro (Apache 2.0) through LocalAI",
    "piper": "Piper with the VCTK corpus (CC BY 4.0, University of Edinburgh CSTR)",
    "chatterbox": "Chatterbox (MIT, Resemble AI)",
}


def credit(cast):
    engines = {("kokoro" if v["engine"].startswith("kokoro") else v["engine"]) for v in cast}
    engines |= {("kokoro" if v["source"]["engine"].startswith("kokoro") else v["source"]["engine"]) for v in cast if "source" in v}
    return "Voices: " + "; ".join(CREDITS[e] for e in CREDITS if e in engines) + "."


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


def load_cast():
    return json.loads((HERE / "cast.json").read_text(encoding="utf-8"))["voices"]


def clip_path(voice, text):
    h = hashlib.sha1(f"{voice['engine']}|{voice['voice']}|{text}".encode()).hexdigest()[:16]
    return CACHE / "clips" / voice["key"] / f"{h}.wav"


def plan():
    jobs = []
    for v in load_cast():
        for line in load_lines():
            path = clip_path(v, line["text"])
            if not path.exists():
                jobs.append({"engine": v["engine"], "voice": v["voice"], "text": line["text"], "path": str(path)})
    CACHE.mkdir(parents=True, exist_ok=True)
    JOBS.write_text(json.dumps(jobs, indent=0), encoding="utf-8")
    by = {}
    for j in jobs:
        by[j["engine"]] = by.get(j["engine"], 0) + 1
    print(f"{len(jobs)} clips to render: {by or 'none'}")


def piper():
    from piper import PiperVoice, SynthesisConfig

    folder = CACHE / "models"
    folder.mkdir(parents=True, exist_ok=True)
    for name in (f"{PIPER_MODEL}.onnx", f"{PIPER_MODEL}.onnx.json"):
        if not (folder / name).exists():
            print(f"downloading {name}")
            urllib.request.urlretrieve(PIPER_URL + name, folder / name)
    model = PiperVoice.load(str(folder / f"{PIPER_MODEL}.onnx"))
    speakers = json.loads((folder / f"{PIPER_MODEL}.onnx.json").read_text(encoding="utf-8"))["speaker_id_map"]
    jobs = [j for j in json.loads(JOBS.read_text(encoding="utf-8")) if j["engine"] == "piper"]
    for j in jobs:
        path = Path(j["path"])
        if path.exists():
            continue
        path.parent.mkdir(parents=True, exist_ok=True)
        cfg = SynthesisConfig(speaker_id=speakers[j["voice"]], length_scale=0.92, noise_scale=0.75, noise_w_scale=0.85)
        with wave.open(str(path), "wb") as w:
            model.synthesize_wav(j["text"], w, syn_config=cfg)
    print(f"piper: {len(jobs)} clips")


def encode(src):
    """Trim the silence at both ends and re-encode as 32 kbit/s mono CBR, without a Xing header."""
    out = subprocess.run(
        ["ffmpeg", "-v", "error", "-i", str(src),
         "-af", "silenceremove=start_periods=1:start_threshold=-45dB,areverse,silenceremove=start_periods=1:start_threshold=-45dB,areverse,loudnorm=I=-18:TP=-1.5:LRA=11",
         "-ac", "1", "-ar", "24000", "-c:a", "libmp3lame", "-b:a", "32k", "-write_xing", "0", "-id3v2_version", "0",
         "-f", "mp3", "-"],
        check=True, capture_output=True)
    return out.stdout


def pack():
    lines = load_lines()
    cast = load_cast()
    missing = [str(clip_path(v, l["text"])) for v in cast for l in lines if not clip_path(v, l["text"]).exists()]
    if missing:
        sys.exit(f"{len(missing)} clips not rendered yet, e.g. {missing[0]}; run plan, then the renderers")
    voices_dir = OUT / "voices"
    voices_dir.mkdir(parents=True, exist_ok=True)
    for old in voices_dir.glob("*.mp3"):
        old.unlink()
    manifest = {"credit": credit(cast), "lines": lines, "voices": []}
    total = 0
    with ThreadPoolExecutor(8) as pool:
        for v in cast:
            blobs = list(pool.map(encode, [clip_path(v, l["text"]) for l in lines]))
            clips, offset = [], 0
            with open(voices_dir / f"{v['key']}.mp3", "wb") as f:
                for b in blobs:
                    f.write(b)
                    clips.append([offset, len(b)])
                    offset += len(b)
            total += offset
            manifest["voices"].append({"key": v["key"], "name": v["name"], "accent": v["accent"], "gender": v["gender"],
                                       "file": f"/audio/voices/{v['key']}.mp3", "clips": clips})
            print(f"{v['key']} ({v['name']}, {v['accent']}): {offset / 1024:.0f} KB")
    (OUT / "voices.json").write_text(json.dumps(manifest, separators=(",", ":")), encoding="utf-8")
    print(f"wrote {OUT / 'voices.json'}: {len(lines)} lines x {len(cast)} voices, {total / 1048576:.1f} MB")


def chatterbox_name(key, exaggeration, cfg):
    return f"{key}@{exaggeration}/{cfg}"


def chatterbox_jobs(exaggeration, cfg):
    """Every clip for a Chatterbox render: each voice's key (its reference clip is <key>.wav), the line's
    number and text, and the file to write (<key>-<line>.wav, line numbered in lines.json order)."""
    lines = load_lines()
    jobs = [{"key": v["key"], "line": i, "text": l["text"], "file": f"{v['key']}-{i:03}.wav"}
            for v in load_cast() for i, l in enumerate(lines)]
    out = CACHE / "chatterbox-jobs.json"
    CACHE.mkdir(parents=True, exist_ok=True)
    out.write_text(json.dumps({"exaggeration": float(exaggeration), "cfg_weight": float(cfg), "jobs": jobs}, indent=1), encoding="utf-8")
    print(f"{len(jobs)} clips ({len(load_cast())} voices x {len(lines)} lines) -> {out}")


def import_clips(folder, exaggeration, cfg):
    """Take rendered Chatterbox clips (<key>-<line>.wav or .mp3) into the cache, and switch every voice
    that has all its lines to them in cast.json, remembering what it was made with before."""
    folder = Path(folder)
    lines = load_lines()
    data = json.loads((HERE / "cast.json").read_text(encoding="utf-8"))
    switched = []
    for v in data["voices"]:
        files = []
        for i in range(len(lines)):
            found = [p for p in (folder / f"{v['key']}-{i:03}.wav", folder / f"{v['key']}-{i:03}.mp3") if p.exists()]
            files.append(found[0] if found else None)
        if not all(files):
            print(f"{v['key']}: {sum(1 for f in files if not f)} of {len(lines)} clips missing; left as it is")
            continue
        target = {**v, "engine": "chatterbox", "voice": chatterbox_name(v["key"], exaggeration, cfg)}
        for f, line in zip(files, lines):
            dst = clip_path(target, line["text"])
            dst.parent.mkdir(parents=True, exist_ok=True)
            subprocess.run(["ffmpeg", "-v", "error", "-y", "-i", str(f), "-ac", "1", "-ar", "24000", str(dst)], check=True)
        v.setdefault("source", {"engine": v["engine"], "voice": v["voice"]})
        v["engine"], v["voice"] = target["engine"], target["voice"]
        switched.append(v["key"])
    (HERE / "cast.json").write_text(json.dumps(data, indent=2, ensure_ascii=False) + "\n", encoding="utf-8")
    print(f"switched to Chatterbox: {', '.join(switched) or 'none'}; now run pack")


if __name__ == "__main__":
    cmd = sys.argv[1] if len(sys.argv) > 1 else "plan"
    if cmd == "chatterbox-jobs":
        chatterbox_jobs(*sys.argv[2:4])
    elif cmd == "import":
        import_clips(*sys.argv[2:5])
    else:
        {"plan": plan, "piper": piper, "pack": pack}[cmd]()
