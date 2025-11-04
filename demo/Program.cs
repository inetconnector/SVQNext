using SVQNext.Codec;
using SVQNext.Tools;
using CodecPipeline = SVQNext.Codec.Pipeline;
using Bitmap = SixLabors.ImageSharp.Image<SixLabors.ImageSharp.PixelFormats.Rgba32>;

const int frameCount = 96;
const int width = 320;
const int height = 180;
const string quality = "medium";
const string searchMode = "diamond";
const int blockSize = 8;
const int searchRange = 4;
const int qMotion = 2;
const int gop = 24;
const bool useBFrames = true;
const bool applyFilters = true;
const bool useHdr = false;
const int bitDepth = 8;
const string colorSpace = "bt709";
const bool enableScalable = true;
const int targetKbps = 1200;

var outputRoot = Path.Combine("out", "codec_demo");
Directory.CreateDirectory(outputRoot);

Console.WriteLine("SVQNext Codec Demo — full encode/decode pipeline\n");
Console.WriteLine($"Output directory: {outputRoot}");

Console.WriteLine("[1/6] Generating synthetic demo frames...");
var demoFrames = ImageIO.GenerateDemo(frameCount, height, width);
var sourceGifPath = Path.Combine(outputRoot, "source.gif");
ImageIO.SaveGif(demoFrames, sourceGifPath);
Console.WriteLine($"Saved animated source preview: {sourceGifPath}");

var rgbFrames = new List<float[,,]>(demoFrames.Count);
foreach (var frame in demoFrames)
{
    rgbFrames.Add(ImageIO.ToFloatRgb(frame));
    frame.Dispose();
}
demoFrames.Clear();

Console.WriteLine("[2/6] Encoding video using SVQNext pipeline...");
var mux = CodecPipeline.EncodeToMux(
    rgbFrames,
    quality,
    searchMode,
    blockSize,
    searchRange,
    qMotion,
    gop,
    useBFrames,
    applyFilters,
    useHdr,
    bitDepth,
    colorSpace,
    enableScalable,
    targetKbps);

var audioSeconds = Math.Max(1, frameCount / 24);
mux = Audio.AttachDemoAudio(mux, seconds: audioSeconds);

DescribeMux(mux);

var containerPath = Path.Combine(outputRoot, "demo_stream.svqpack");
Container.Write(containerPath, mux);
Console.WriteLine($"Container written: {containerPath} ({FormatSize(containerPath)})");

Console.WriteLine("[3/6] Writing streaming segments...");
var segmentDir = Path.Combine(outputRoot, "segments");
Segmenter.WriteSegments(segmentDir, mux, framesPerSegment: 24);
Console.WriteLine($"Segments + manifest ready under {segmentDir}");

Console.WriteLine("[4/6] Decoding container back to RGB frames...");
var decodedMux = Container.Read(containerPath);
var (decodedRgb, decodedCount, decodedHeight, decodedWidth) = CodecPipeline.DecodeFromMux(decodedMux);
Console.WriteLine($"Decoded {decodedCount} frame(s) at {decodedWidth}x{decodedHeight}");

Console.WriteLine("[5/6] Exporting reconstructed preview GIF...");
var reconstructedFrames = new List<Bitmap>(decodedCount);
for (var i = 0; i < decodedCount; i++) reconstructedFrames.Add(ImageIO.ToBitmap(decodedRgb[i]));
var reconstructedGifPath = Path.Combine(outputRoot, "reconstructed.gif");
ImageIO.SaveGif(reconstructedFrames, reconstructedGifPath);
Console.WriteLine($"Saved reconstructed preview: {reconstructedGifPath}");
foreach (var frame in reconstructedFrames) frame.Dispose();

Console.WriteLine("[6/6] Recovering embedded audio track...");
var wavePath = Path.Combine(outputRoot, "demo_audio.wav");
var extracted = Audio.ExtractDemoAudio(decodedMux, wavePath);
Console.WriteLine(extracted != null
    ? $"Decoded audio written to {wavePath} ({FormatSize(wavePath)})"
    : "No audio track present in mux.");

var psnrValues = new List<double>();
var frameLimit = Math.Min(rgbFrames.Count, decodedRgb.Length);
for (var i = 0; i < frameLimit; i++) psnrValues.Add(ComputePsnr(rgbFrames[i], decodedRgb[i]));

var avgPsnr = psnrValues.Count > 0 ? psnrValues.Average() : double.NaN;
Console.WriteLine();
Console.WriteLine("Quality summary:");
Console.WriteLine($"  Average PSNR: {avgPsnr:F2} dB");
Console.WriteLine($"  Source GIF size: {FormatSize(sourceGifPath)}");
Console.WriteLine($"  Reconstructed GIF size: {FormatSize(reconstructedGifPath)}");
Console.WriteLine($"  Container size: {FormatSize(containerPath)}");

Console.WriteLine("Demo complete. Inspect the 'out/codec_demo' directory for all artefacts.");

static void DescribeMux(Mux mux)
{
    Console.WriteLine();
    Console.WriteLine("Mux summary:");
    Console.WriteLine($"  Tracks: {mux.Tracks.Count}");
    foreach (var track in mux.Tracks)
    {
        Console.WriteLine($"  - {track.Type} ({track.Chunks.Count} chunk(s))");
        foreach (var kv in track.Meta.OrderBy(k => k.Key))
            Console.WriteLine($"      {kv.Key} = {kv.Value}");
    }
    Console.WriteLine();
}

static double ComputePsnr(float[,,] original, float[,,] reconstructed)
{
    var h = Math.Min(original.GetLength(0), reconstructed.GetLength(0));
    var w = Math.Min(original.GetLength(1), reconstructed.GetLength(1));
    var c = Math.Min(original.GetLength(2), reconstructed.GetLength(2));

    double mse = 0;
    var samples = h * w * c;

    for (var y = 0; y < h; y++)
    for (var x = 0; x < w; x++)
    for (var ch = 0; ch < c; ch++)
    {
        var diff = original[y, x, ch] - reconstructed[y, x, ch];
        mse += diff * diff;
    }

    if (samples == 0) return double.NaN;

    mse /= samples;
    if (mse <= double.Epsilon) return double.PositiveInfinity;
    return 10.0 * Math.Log10(1.0 / mse);
}

static string FormatSize(string path)
{
    var bytes = new FileInfo(path).Length;
    return bytes switch
    {
        >= 1_000_000 => $"{bytes / 1_000_000.0:F1} MB",
        >= 1_000 => $"{bytes / 1_000.0:F1} KB",
        _ => $"{bytes} B"
    };
}
