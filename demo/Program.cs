using System.Diagnostics;
using System.Globalization;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using SVQNext.Codec;
using CodecPipeline = SVQNext.Codec.Pipeline;
using Bitmap = SixLabors.ImageSharp.Image<SixLabors.ImageSharp.PixelFormats.Rgba32>;

var settings = DemoSettings.Parse(args);
Directory.CreateDirectory(settings.OutputRoot);

Console.WriteLine("SVQNext Video Roundtrip Demo\n");
Console.WriteLine($"Output directory: {settings.OutputRoot}");
Console.WriteLine(settings.InputDirectory is null
    ? $"Source: synthetic {settings.FrameCount}-frame sequence at {settings.Width}x{settings.Height}"
    : $"Source: image sequence from {settings.InputDirectory}");
Console.WriteLine($"SVQNext settings: quality={settings.Quality}, block={settings.BlockSize}, search={settings.SearchRange}, gop={settings.Gop}, target={settings.TargetKbps} kbps");
Console.WriteLine();

Console.WriteLine("[1/7] Preparing source frames...");
using var sourceFrames = FrameSet.Load(settings);
var sourceRgb = sourceFrames.ToFloatRgbFrames();
var sourceGifPath = Path.Combine(settings.OutputRoot, "source.gif");
var sourceFrameDir = Path.Combine(settings.OutputRoot, "source_frames_bmp");
sourceFrames.SaveGif(sourceGifPath);
sourceFrames.SaveBmpSequence(sourceFrameDir);
Console.WriteLine($"Saved source preview: {sourceGifPath}");
Console.WriteLine($"Saved source BMP sequence: {sourceFrameDir}");

Console.WriteLine("[2/7] Encoding with SVQNext...");
var mux = CodecPipeline.EncodeToMux(
    sourceRgb,
    settings.Quality,
    settings.SearchMode,
    settings.BlockSize,
    settings.SearchRange,
    settings.QMotion,
    settings.Gop,
    settings.UseBFrames,
    settings.ApplyFilters,
    settings.UseHdr,
    settings.BitDepth,
    settings.ColorSpace,
    settings.EnableScalable,
    settings.TargetKbps);

var containerPath = Path.Combine(settings.OutputRoot, "demo_stream.svqpack");
Container.Write(containerPath, mux);
Console.WriteLine($"Container written: {containerPath} ({FormatFileSize(containerPath)})");

Console.WriteLine("[3/7] Decoding SVQNext stream...");
var decodedMux = Container.Read(containerPath);
var (decodedRgb, decodedCount, decodedHeight, decodedWidth) = CodecPipeline.DecodeFromMux(decodedMux);
Console.WriteLine($"Decoded {decodedCount} frame(s) at {decodedWidth}x{decodedHeight}");

Console.WriteLine("[4/7] Exporting reconstructed SVQNext artefacts...");
using var svqFrames = FrameSet.FromRgb(decodedRgb);
var svqGifPath = Path.Combine(settings.OutputRoot, "svqnext_reconstructed.gif");
var svqFrameDir = Path.Combine(settings.OutputRoot, "svqnext_frames");
svqFrames.SaveGif(svqGifPath);
svqFrames.SavePngSequence(svqFrameDir);
Console.WriteLine($"Saved reconstructed preview: {svqGifPath}");

var svqMetrics = QualitySummary.Measure(sourceRgb, decodedRgb);
var svqResult = VideoResult.Create(
    label: "SVQNext",
    encodedPath: containerPath,
    previewPath: svqGifPath,
    metrics: svqMetrics,
    notes: $"Container track count: {decodedMux.Tracks.Count}. Decoded size: {decodedWidth}x{decodedHeight}.");

Console.WriteLine("[5/7] Evaluating SVQNext quality...");
PrintResult(svqResult);

Console.WriteLine("[6/7] Running optional H.264 comparison...");
var h264Result = TryRunH264Comparison(settings, sourceFrames, sourceRgb, sourceFrameDir, svqResult);
if (h264Result is null)
{
    Console.WriteLine("H.264 comparison skipped.");
}
else
{
    PrintResult(h264Result);
}

Console.WriteLine("[7/7] Writing comparison report...");
var reportPath = Path.Combine(settings.OutputRoot, "comparison_report.txt");
File.WriteAllText(reportPath, BuildReport(settings, sourceFrames, svqResult, h264Result));
Console.WriteLine($"Report written: {reportPath}");
Console.WriteLine();
Console.WriteLine("Demo complete. Inspect the generated artefacts under the output directory.");

static VideoResult? TryRunH264Comparison(
    DemoSettings settings,
    FrameSet sourceFrames,
    IReadOnlyList<float[,,]> sourceRgb,
    string sourceFrameDir,
    VideoResult svqResult)
{
    var ffmpegPath = ResolveFfmpegPath(settings.FfmpegPath);
    if (ffmpegPath is null)
    {
        Console.WriteLine("No ffmpeg executable found. Install ffmpeg or pass --ffmpeg <path> to enable H.264 benchmarking.");
        return null;
    }

    var encoder = ResolveH264Encoder(ffmpegPath, settings.H264Encoder);
    if (encoder is null)
    {
        Console.WriteLine("FFmpeg is available, but no supported H.264 encoder was found for the comparison.");
        return null;
    }

    var durationSeconds = Math.Max(sourceFrames.Count / (double)settings.FramesPerSecond, 1d / settings.FramesPerSecond);
    var targetKbps = Math.Max(100, (int)Math.Round(svqResult.SizeBytes * 8.0 / durationSeconds / 1000.0));

    var encodedPath = Path.Combine(settings.OutputRoot, "h264_reference.mp4");
    var decodedDir = Path.Combine(settings.OutputRoot, "h264_frames");
    Directory.CreateDirectory(decodedDir);

    try
    {
        RunFfmpegEncode(ffmpegPath, sourceFrameDir, encodedPath, settings, sourceFrames.Count, targetKbps, encoder);
        RunFfmpegDecode(ffmpegPath, encodedPath, decodedDir, sourceFrames.Count);
    }
    catch (Exception ex)
    {
        Console.WriteLine($"H.264 comparison failed: {ex.Message}");
        return null;
    }

    using var decodedFrames = FrameSet.LoadFromDirectory(decodedDir, limit: sourceFrames.Count);
    var decodedRgb = decodedFrames.ToFloatRgbFrames();
    var decodedGifPath = Path.Combine(settings.OutputRoot, "h264_reconstructed.gif");
    decodedFrames.SaveGif(decodedGifPath);

    var metrics = QualitySummary.Measure(sourceRgb, decodedRgb);
    return VideoResult.Create(
        label: "H.264",
        encodedPath: encodedPath,
        previewPath: decodedGifPath,
        metrics: metrics,
        notes: $"Encoded via ffmpeg/{encoder} with requested target {targetKbps} kbps. Actual size may differ based on the encoder's rate-control behavior.");
}

static void RunFfmpegEncode(
    string ffmpegPath,
    string sourceFrameDir,
    string encodedPath,
    DemoSettings settings,
    int frameCount,
    int targetKbps,
    string encoder)
{
    var args = new List<string>
    {
        "-y",
        "-hide_banner",
        "-loglevel", "error",
        "-framerate", settings.FramesPerSecond.ToString(CultureInfo.InvariantCulture),
        "-start_number", "0",
        "-i", Path.Combine(sourceFrameDir, "frame_%04d.bmp"),
        "-frames:v", frameCount.ToString(CultureInfo.InvariantCulture),
        "-an",
        "-c:v", encoder,
        "-pix_fmt", "yuv420p",
        "-b:v", $"{targetKbps}k",
        "-maxrate", $"{targetKbps}k",
        "-bufsize", $"{targetKbps * 2}k",
        "-g", settings.Gop.ToString(CultureInfo.InvariantCulture),
        encodedPath
    };

    if (string.Equals(encoder, "libx264", StringComparison.OrdinalIgnoreCase))
    {
        args.InsertRange(args.Count - 5, new[] { "-preset", settings.H264Preset });
    }

    RunProcess(ffmpegPath, args, "H.264 encode");
}

static void RunFfmpegDecode(string ffmpegPath, string encodedPath, string decodedDir, int frameCount)
{
    var args = new[]
    {
        "-y",
        "-hide_banner",
        "-loglevel", "error",
        "-i", encodedPath,
        "-frames:v", frameCount.ToString(CultureInfo.InvariantCulture),
        "-start_number", "0",
        Path.Combine(decodedDir, "frame_%04d.bmp")
    };

    RunProcess(ffmpegPath, args, "H.264 decode");
}

static string? ResolveFfmpegPath(string? configuredPath)
{
    if (!string.IsNullOrWhiteSpace(configuredPath))
        return File.Exists(configuredPath) ? Path.GetFullPath(configuredPath) : configuredPath;

    var pathEnv = Environment.GetEnvironmentVariable("PATH");
    if (string.IsNullOrWhiteSpace(pathEnv))
        return null;

    foreach (var dir in pathEnv.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
    {
        var candidate = Path.Combine(dir, "ffmpeg.exe");
        if (File.Exists(candidate))
            return candidate;
    }

    return null;
}

static string? ResolveH264Encoder(string ffmpegPath, string? requestedEncoder)
{
    if (!string.IsNullOrWhiteSpace(requestedEncoder))
        return requestedEncoder;

    var output = CaptureProcess(ffmpegPath, new[] { "-hide_banner", "-encoders" }, "FFmpeg encoder probe");
    var candidates = new[] { "libx264", "h264_mf", "h264_d3d12va", "h264_amf", "h264_qsv", "h264_nvenc" };
    foreach (var candidate in candidates)
    {
        if (output.Contains(candidate, StringComparison.OrdinalIgnoreCase))
            return candidate;
    }

    return null;
}

static void RunProcess(string fileName, IReadOnlyList<string> arguments, string stepName)
{
    _ = CaptureProcess(fileName, arguments, stepName);
}

static string CaptureProcess(string fileName, IReadOnlyList<string> arguments, string stepName)
{
    using var process = new Process();
    process.StartInfo = new ProcessStartInfo
    {
        FileName = fileName,
        RedirectStandardOutput = true,
        RedirectStandardError = true,
        UseShellExecute = false,
        CreateNoWindow = true
    };

    foreach (var argument in arguments)
        process.StartInfo.ArgumentList.Add(argument);

    process.Start();
    var stdout = process.StandardOutput.ReadToEnd();
    var stderr = process.StandardError.ReadToEnd();
    process.WaitForExit();

    if (process.ExitCode != 0)
    {
        var detail = string.IsNullOrWhiteSpace(stderr) ? stdout : stderr;
        throw new InvalidOperationException($"{stepName} failed with exit code {process.ExitCode}: {detail.Trim()}");
    }

    return string.IsNullOrWhiteSpace(stdout) ? stderr : stdout;
}

static void PrintResult(VideoResult result)
{
    Console.WriteLine();
    Console.WriteLine(result.Label);
    Console.WriteLine($"  Encoded size: {FormatSize(result.SizeBytes)}");
    Console.WriteLine($"  Avg PSNR:     {result.Metrics.AveragePsnr:F2} dB");
    Console.WriteLine($"  Min PSNR:     {result.Metrics.MinimumPsnr:F2} dB");
    Console.WriteLine($"  Avg SSIM:     {result.Metrics.AverageSsim:F4}");
    Console.WriteLine($"  Min SSIM:     {result.Metrics.MinimumSsim:F4}");
    Console.WriteLine($"  Preview:      {result.PreviewPath}");
    Console.WriteLine($"  Notes:        {result.Notes}");
    Console.WriteLine();
}

static string BuildReport(DemoSettings settings, FrameSet sourceFrames, VideoResult svqResult, VideoResult? h264Result)
{
    var lines = new List<string>
    {
        "SVQNext comparison report",
        "=========================",
        $"Created: {DateTimeOffset.Now:yyyy-MM-dd HH:mm:ss zzz}",
        $"Output directory: {settings.OutputRoot}",
        $"Source frames: {sourceFrames.Count}",
        $"Source dimensions: {sourceFrames.Width}x{sourceFrames.Height}",
        $"Source type: {(settings.InputDirectory is null ? "synthetic" : settings.InputDirectory)}",
        $"Frames per second: {settings.FramesPerSecond}",
        $"SVQNext settings: quality={settings.Quality}, block={settings.BlockSize}, search={settings.SearchRange}, gop={settings.Gop}, target={settings.TargetKbps} kbps",
        string.Empty,
        FormatReportBlock(svqResult)
    };

    if (h264Result is null)
    {
        lines.Add("H.264");
        lines.Add("  Status: skipped");
        lines.Add("  Reason: ffmpeg or a supported H.264 encoder was not available, or encoding failed.");
    }
    else
    {
        lines.Add(string.Empty);
        lines.Add(FormatReportBlock(h264Result));
        lines.Add(string.Empty);
        lines.Add("Comparison");
        lines.Add($"  Size ratio (SVQ/H.264): {(double)svqResult.SizeBytes / h264Result.SizeBytes:F3}");
        lines.Add($"  Avg PSNR delta: {(svqResult.Metrics.AveragePsnr - h264Result.Metrics.AveragePsnr):F2} dB");
        lines.Add($"  Avg SSIM delta: {(svqResult.Metrics.AverageSsim - h264Result.Metrics.AverageSsim):F4}");
        lines.Add("  Interpretation: values close to zero mean the codecs are behaving similarly on this clip.");
    }

    lines.Add(string.Empty);
    lines.Add("Notes");
    lines.Add("  This demo uses objective metrics (PSNR/SSIM) on the exact same frame set.");
    lines.Add("  A claim of parity with H.264 should only be made after repeated runs on real video clips, not just the synthetic sample.");
    return string.Join(Environment.NewLine, lines);
}

static string FormatReportBlock(VideoResult result)
{
    return string.Join(Environment.NewLine, new[]
    {
        result.Label,
        $"  Encoded size: {FormatSize(result.SizeBytes)}",
        $"  Avg PSNR: {result.Metrics.AveragePsnr:F2} dB",
        $"  Min PSNR: {result.Metrics.MinimumPsnr:F2} dB",
        $"  Avg SSIM: {result.Metrics.AverageSsim:F4}",
        $"  Min SSIM: {result.Metrics.MinimumSsim:F4}",
        $"  Preview: {result.PreviewPath}",
        $"  Notes: {result.Notes}"
    });
}

static string FormatFileSize(string path)
{
    return FormatSize(new FileInfo(path).Length);
}

static string FormatSize(long bytes)
{
    return bytes switch
    {
        >= 1_000_000 => $"{bytes / 1_000_000.0:F2} MB",
        >= 1_000 => $"{bytes / 1_000.0:F2} KB",
        _ => $"{bytes} B"
    };
}

internal sealed record DemoSettings(
    string OutputRoot,
    string? InputDirectory,
    int FrameCount,
    int Width,
    int Height,
    int FramesPerSecond,
    string Quality,
    string SearchMode,
    int BlockSize,
    int SearchRange,
    int QMotion,
    int Gop,
    bool UseBFrames,
    bool ApplyFilters,
    bool UseHdr,
    int BitDepth,
    string ColorSpace,
    bool EnableScalable,
    int TargetKbps,
    string? FfmpegPath,
    string H264Preset,
    string? H264Encoder)
{
    public static DemoSettings Parse(string[] args)
    {
        var outputRoot = Path.Combine("out", "codec_demo");
        string? inputDirectory = null;
        var frameCount = 96;
        var width = 320;
        var height = 180;
        var fps = 24;
        var quality = "medium";
        var searchMode = "diamond";
        var blockSize = 8;
        var searchRange = 4;
        var qMotion = 2;
        var gop = 24;
        var useBFrames = true;
        var applyFilters = true;
        var useHdr = false;
        var bitDepth = 8;
        var colorSpace = "bt709";
        var enableScalable = true;
        var targetKbps = 1200;
        string? ffmpegPath = null;
        var h264Preset = "medium";
        string? h264Encoder = null;

        for (var i = 0; i < args.Length; i++)
        {
            switch (args[i])
            {
                case "--out":
                    outputRoot = args[++i];
                    break;
                case "--input-dir":
                    inputDirectory = args[++i];
                    break;
                case "--frames":
                    frameCount = int.Parse(args[++i], CultureInfo.InvariantCulture);
                    break;
                case "--width":
                    width = int.Parse(args[++i], CultureInfo.InvariantCulture);
                    break;
                case "--height":
                    height = int.Parse(args[++i], CultureInfo.InvariantCulture);
                    break;
                case "--fps":
                    fps = int.Parse(args[++i], CultureInfo.InvariantCulture);
                    break;
                case "--quality":
                    quality = args[++i];
                    break;
                case "--searchmode":
                    searchMode = args[++i];
                    break;
                case "--block":
                    blockSize = int.Parse(args[++i], CultureInfo.InvariantCulture);
                    break;
                case "--search":
                    searchRange = int.Parse(args[++i], CultureInfo.InvariantCulture);
                    break;
                case "--qmotion":
                    qMotion = int.Parse(args[++i], CultureInfo.InvariantCulture);
                    break;
                case "--gop":
                    gop = int.Parse(args[++i], CultureInfo.InvariantCulture);
                    break;
                case "--no-bframes":
                    useBFrames = false;
                    break;
                case "--no-filters":
                    applyFilters = false;
                    break;
                case "--use-hdr":
                    useHdr = int.Parse(args[++i], CultureInfo.InvariantCulture) != 0;
                    break;
                case "--bitdepth":
                    bitDepth = int.Parse(args[++i], CultureInfo.InvariantCulture);
                    break;
                case "--colorspace":
                    colorSpace = args[++i];
                    break;
                case "--scalable":
                    enableScalable = int.Parse(args[++i], CultureInfo.InvariantCulture) != 0;
                    break;
                case "--kbps":
                    targetKbps = int.Parse(args[++i], CultureInfo.InvariantCulture);
                    break;
                case "--ffmpeg":
                    ffmpegPath = args[++i];
                    break;
                case "--h264-preset":
                    h264Preset = args[++i];
                    break;
                case "--h264-encoder":
                    h264Encoder = args[++i];
                    break;
            }
        }

        return new DemoSettings(
            OutputRoot: outputRoot,
            InputDirectory: inputDirectory,
            FrameCount: frameCount,
            Width: width,
            Height: height,
            FramesPerSecond: fps,
            Quality: quality,
            SearchMode: searchMode,
            BlockSize: blockSize,
            SearchRange: searchRange,
            QMotion: qMotion,
            Gop: gop,
            UseBFrames: useBFrames,
            ApplyFilters: applyFilters,
            UseHdr: useHdr,
            BitDepth: bitDepth,
            ColorSpace: colorSpace,
            EnableScalable: enableScalable,
            TargetKbps: targetKbps,
            FfmpegPath: ffmpegPath,
            H264Preset: h264Preset,
            H264Encoder: h264Encoder);
    }
}

internal sealed class FrameSet : IDisposable
{
    private readonly List<Bitmap> _frames;

    private FrameSet(List<Bitmap> frames)
    {
        if (frames.Count == 0)
            throw new ArgumentException("At least one frame is required.", nameof(frames));

        _frames = frames;
        Width = frames[0].Width;
        Height = frames[0].Height;
    }

    public int Count => _frames.Count;
    public int Width { get; }
    public int Height { get; }

    public static FrameSet Load(DemoSettings settings)
    {
        if (settings.InputDirectory is null)
            return new FrameSet(ImageIO.GenerateDemo(settings.FrameCount, settings.Height, settings.Width));

        return LoadFromDirectory(settings.InputDirectory, settings.FrameCount);
    }

    public static FrameSet LoadFromDirectory(string directory, int? limit = null)
    {
        if (!Directory.Exists(directory))
            throw new DirectoryNotFoundException($"Input directory not found: {directory}");

        var files = Directory.EnumerateFiles(directory)
            .Where(path =>
            {
                var ext = Path.GetExtension(path);
                return ext.Equals(".png", StringComparison.OrdinalIgnoreCase) ||
                       ext.Equals(".jpg", StringComparison.OrdinalIgnoreCase) ||
                       ext.Equals(".jpeg", StringComparison.OrdinalIgnoreCase) ||
                       ext.Equals(".bmp", StringComparison.OrdinalIgnoreCase);
            })
            .OrderBy(path => path, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        if (limit.HasValue)
            files = files.Take(limit.Value).ToArray();

        if (files.Length == 0)
            throw new InvalidOperationException($"No supported image files found in {directory}");

        var frames = new List<Bitmap>(files.Length);
        foreach (var file in files)
            frames.Add(Image.Load<Rgba32>(file));

        return new FrameSet(frames);
    }

    public static FrameSet FromRgb(IEnumerable<float[,,]> rgbFrames)
    {
        var frames = rgbFrames.Select(ImageIO.ToBitmap).ToList();
        return new FrameSet(frames);
    }

    public IReadOnlyList<float[,,]> ToFloatRgbFrames()
    {
        return _frames.Select(ImageIO.ToFloatRgb).ToList();
    }

    public void SaveGif(string path)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
        ImageIO.SaveGif(_frames, path);
    }

    public void SavePngSequence(string directory)
    {
        Directory.CreateDirectory(directory);
        for (var i = 0; i < _frames.Count; i++)
        {
            var path = Path.Combine(directory, $"frame_{i:D4}.png");
            _frames[i].SaveAsPng(path);
        }
    }

    public void SaveBmpSequence(string directory)
    {
        Directory.CreateDirectory(directory);
        for (var i = 0; i < _frames.Count; i++)
        {
            var path = Path.Combine(directory, $"frame_{i:D4}.bmp");
            _frames[i].SaveAsBmp(path);
        }
    }

    public void Dispose()
    {
        foreach (var frame in _frames)
            frame.Dispose();
    }
}

internal sealed record QualitySummary(double AveragePsnr, double MinimumPsnr, double AverageSsim, double MinimumSsim)
{
    public static QualitySummary Measure(IReadOnlyList<float[,,]> original, IReadOnlyList<float[,,]> reconstructed)
    {
        var count = Math.Min(original.Count, reconstructed.Count);
        if (count == 0)
            return new QualitySummary(double.NaN, double.NaN, double.NaN, double.NaN);

        var psnr = new List<double>(count);
        var ssim = new List<double>(count);
        for (var i = 0; i < count; i++)
        {
            psnr.Add(MathUtil.PSNR(original[i], reconstructed[i]));
            ssim.Add(MathUtil.SSIM(original[i], reconstructed[i]));
        }

        return new QualitySummary(
            AveragePsnr: psnr.Average(),
            MinimumPsnr: psnr.Min(),
            AverageSsim: ssim.Average(),
            MinimumSsim: ssim.Min());
    }
}

internal sealed record VideoResult(
    string Label,
    string EncodedPath,
    string PreviewPath,
    long SizeBytes,
    QualitySummary Metrics,
    string Notes)
{
    public static VideoResult Create(string label, string encodedPath, string previewPath, QualitySummary metrics, string notes)
    {
        return new VideoResult(
            Label: label,
            EncodedPath: encodedPath,
            PreviewPath: previewPath,
            SizeBytes: new FileInfo(encodedPath).Length,
            Metrics: metrics,
            Notes: notes);
    }
}
