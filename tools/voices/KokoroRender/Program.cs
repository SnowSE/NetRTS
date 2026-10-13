// Renders the Kokoro clips in tools/voices/.cache/jobs.json (written by `voices.py plan`).
//   kokoro         LocalAI's text to speech, in the voice's own language (English for these voices)
//   kokoro-accent  a Kokoro voice for another language reading English: the same steps as LocalAI's
//                  LocalTextToSpeechClient, through KokoroSharp, with English pronunciation
// Clips already on disk are skipped, so a run that stops part-way can simply be started again.
using System.Text.Json;
using System.Threading.Channels;
using KokoroSharp;
using KokoroSharp.Core;
using KokoroSharp.Processing;
using LocalAI;
using LocalAI.Audio;
using Microsoft.Extensions.AI;
using Microsoft.ML.OnnxRuntime;

var jobsFile = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", ".cache", "jobs.json"));
if (args.Length > 0)
    jobsFile = args[0];
var jobs = JsonSerializer.Deserialize<List<Job>>(File.ReadAllText(jobsFile), new JsonSerializerOptions { PropertyNameCaseInsensitive = true })!
    .Where(j => j.Engine.StartsWith("kokoro", StringComparison.Ordinal) && !File.Exists(j.Path))
    .ToList();
Console.WriteLine($"{jobs.Count} Kokoro clips to render");
if (jobs.Count == 0)
    return;

using var tts = await LocalTextToSpeechClient.LoadAsync();
using var synth = jobs.Any(j => j.Engine == "kokoro-accent")
    ? new KokoroWavSynthesizer(await ModelSource.ResolveAsync(LocalTextToSpeechClient.DefaultSource), new SessionOptions { IntraOpNumThreads = Math.Max(1, Environment.ProcessorCount / 2) })
    : null;
var english = KokoroVoiceManager.GetVoice("af_heart").GetLangCode();

var done = 0;
foreach (var job in jobs)
{
    Directory.CreateDirectory(Path.GetDirectoryName(job.Path)!);
    byte[] wav;
    if (job.Engine == "kokoro")
    {
        var response = await tts.GetAudioAsync(job.Text, new TextToSpeechOptions { VoiceId = job.Voice });
        wav = response.Contents.OfType<DataContent>().First().Data.ToArray();
    }
    else
    {
        wav = new AudioData(await SpeakEnglish(synth!, KokoroVoiceManager.GetVoice(job.Voice), english, job.Text), LocalTextToSpeechClient.SampleRate).ToWav();
    }
    // Write to a temporary name first, so a crash never leaves a half-written clip behind.
    await File.WriteAllBytesAsync(job.Path + ".tmp", wav);
    File.Move(job.Path + ".tmp", job.Path, overwrite: true);
    if (++done % 50 == 0)
        Console.WriteLine($"  {done}/{jobs.Count}");
}
Console.WriteLine($"rendered {done} clips");

static async Task<float[]> SpeakEnglish(KokoroWavSynthesizer synth, KokoroVoice voice, string lang, string text)
{
    var config = new KokoroTTSPipelineConfig(new DefaultSegmentationConfig { MaxFirstSegmentLength = 510 });
    var segments = config.SegmentationFunc(Tokenizer.Tokenize(text.Trim(), lang, config.PreprocessText));
    var channel = Channel.CreateUnbounded<float[]>();
    var job = synth.EnqueueJob(KokoroJob.Create(segments, voice, 1f, null));
    foreach (var step in job.Steps)
    {
        step.OnStepComplete = samples =>
        {
            channel.Writer.TryWrite(KokoroPlayback.PostProcessSamples(samples, out _));
            if (step == job.Steps[^1])
                channel.Writer.TryComplete();
        };
    }
    var all = new List<float>();
    await foreach (var chunk in channel.Reader.ReadAllAsync())
        all.AddRange(chunk);
    return [.. all];
}

internal sealed record Job(string Engine, string Voice, string Text, string Path);
