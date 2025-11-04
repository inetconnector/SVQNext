// Public Domain

using System.Text.Json;
using SVQNext.Codec;

namespace SVQNext.Tools;

public static class Segmenter
{
    public static void WriteSegments(string outDir, Mux mux, int framesPerSegment)
    {
        Directory.CreateDirectory(outDir);
        var video = mux.Tracks.Find(t => t.Type == "video");
        if (video == null) throw new InvalidDataException("no video");
        // chunk 0 is CONFIG, keep in every segment for independence
        var T = video.Chunks.Count - 1;
        var segCount = (int)Math.Ceiling(T / (double)framesPerSegment);
        var manifest = new List<Dictionary<string, object>>();
        for (var s = 0; s < segCount; s++)
        {
            var start = s * framesPerSegment;
            var end = Math.Min(T, start + framesPerSegment);
            var chunks = new List<Chunk>();
            chunks.Add(video.Chunks[0]); // CONFIG
            for (var t = start; t < end; t++) chunks.Add(video.Chunks[t + 1]);
            var vtrack = new Track("video", video.Meta, chunks);
            var segMux = new Mux(new List<Track> { vtrack });
            var name = $"seg_{s + 1:00000}.svqs";
            Container.Write(Path.Combine(outDir, name), segMux);
            manifest.Add(new Dictionary<string, object> { { "url", name }, { "frames", end - start } });
        }

        var manObj = new { version = 1, segments = manifest };
        File.WriteAllText(Path.Combine(outDir, "manifest.json"),
            JsonSerializer.Serialize(manObj, new JsonSerializerOptions { WriteIndented = true }));
    }
}