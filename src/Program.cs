// SPDX-License-Identifier: AGPL-3.0-or-later

using SVQNext.Codec;
using SVQNext.Tools;
using CodecPipeline = SVQNext.Codec.Pipeline;

namespace SVQNext;

/// <summary>
///     Entry point that wires the encoder, decoder and demo utilities together.
///     A single binary can generate sample content, mux it to disk or decode existing streams.
/// </summary>
public class Program
{
    // CLI defaults tuned for a responsive demo encode that still stresses the pipeline.
    private static string OUTDIR = "out";
    private static bool DO_DECODE, DO_ENCODE;
    private static string INPUT = "";
    private static int T = 120, W = 320, H = 180, BS = 8, SEARCH = 4, GOP = 24, QMotion = 2;
    private static string QUALITY = "medium", SEARCHMODE = "diamond", COLOR = "bt709";
    private static int BITDEPTH = 10; // 8/10/12
    private static bool USE_HDR = true, USE_BFRAMES = true, LOOP_FILTERS = true;
    private static bool DEMO_AUDIO, DEMO_SUBS;
    private static bool ENABLE_SCALABLE = true;
    private static int TARGET_KBPS = 1500; // simple RC target

    /// <summary>
    ///     Parses arguments, then either decodes an existing container or produces demo content.
    /// </summary>
    public static void Main(string[] args)
    {
        for (var i = 0; i < args.Length; i++)
            switch (args[i])
            {
                case "--encode": DO_ENCODE = true; break;
                case "--decode":
                    DO_DECODE = true;
                    INPUT = args[++i];
                    break;
                case "--out": OUTDIR = args[++i]; break;
                case "--frames": T = int.Parse(args[++i]); break;
                case "--width": W = int.Parse(args[++i]); break;
                case "--height": H = int.Parse(args[++i]); break;
                case "--block": BS = int.Parse(args[++i]); break;
                case "--search": SEARCH = int.Parse(args[++i]); break;
                case "--qmotion": QMotion = int.Parse(args[++i]); break;
                case "--quality": QUALITY = args[++i]; break;
                case "--searchmode": SEARCHMODE = args[++i]; break;
                case "--gop": GOP = int.Parse(args[++i]); break;
                case "--no-bframes": USE_BFRAMES = false; break;
                case "--no-filters": LOOP_FILTERS = false; break;
                case "--use-hdr": USE_HDR = int.Parse(args[++i]) != 0; break;
                case "--bitdepth": BITDEPTH = int.Parse(args[++i]); break;
                case "--colorspace": COLOR = args[++i]; break;
                case "--audio": DEMO_AUDIO = args[++i] == "demo"; break;
                case "--subs": DEMO_SUBS = args[++i] == "demo"; break;
                case "--scalable": ENABLE_SCALABLE = int.Parse(args[++i]) != 0; break;
                case "--kbps": TARGET_KBPS = int.Parse(args[++i]); break;
            }

        Directory.CreateDirectory(OUTDIR);

        if (DO_DECODE && File.Exists(INPUT))
        {
            Console.WriteLine($"[SVQ5] Decoding container {INPUT} ...");
            var mux = Container.Read(INPUT);
            var (rgb, t, h, w) = CodecPipeline.DecodeFromMux(mux);
            var recBmps = rgb.Select(f => ImageIO.ToBitmap(f)).ToList();
            // Persist a visual sanity check so that decoding can be verified quickly.
            ImageIO.SaveGif(recBmps, Path.Combine(OUTDIR, "reconstructed_from_file.gif"));
            Console.WriteLine("Done.");
            return;
        }

        if (DO_ENCODE)
        {
            Console.WriteLine("[SVQ5] Generating demo frames...");
            var frames = ImageIO.GenerateDemo(T, H, W);
            var rgb = frames.Select(f => ImageIO.ToFloatRgb(f)).ToArray();
            Console.WriteLine("[SVQ5] Encoding video...");
            // Encode using the high-performance pipeline; the settings map directly to codec knobs.
            var mux = CodecPipeline.EncodeToMux(rgb, QUALITY, SEARCHMODE, BS, SEARCH, QMotion, GOP, USE_BFRAMES,
                LOOP_FILTERS, USE_HDR, BITDEPTH, COLOR, ENABLE_SCALABLE, TARGET_KBPS);
            if (DEMO_AUDIO) mux = Audio.AttachDemoAudio(mux);
            if (DEMO_SUBS) mux = Subtitles.AttachDemoSubs(mux);

            var pack = Path.Combine(OUTDIR, "stream.svqpack");
            Container.Write(pack, mux);
            Console.WriteLine($"Container written: {pack}");

            // Segmentation & manifest
            // Segmenting after the encode keeps latency low for streaming scenarios.
            Console.WriteLine("[SVQ5] Writing streaming segments...");
            Segmenter.WriteSegments(OUTDIR, mux, 24); // ~1s chunks @24fps
            Console.WriteLine("Done. See manifest.json + seg_*.svqs");
            return;
        }

        Console.WriteLine("Usage: --encode [options] | --decode <file>");
    }
}