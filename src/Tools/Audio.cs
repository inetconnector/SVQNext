// Public Domain

using System.Linq;
using System.Text;
using SVQNext.Codec;

namespace SVQNext.Tools;

public static class Audio
{
    // Simple PD ADPCM (4-bit) demo encoder/decoder to showcase audio track
    public static Mux AttachDemoAudio(Mux mux, int sampleRate = 16000, int seconds = 5)
    {
        var n = sampleRate * seconds;
        var pcm = new short[n];
        for (var i = 0; i < n; i++)
        {
            var t = i / (double)sampleRate;
            var s = Math.Sin(2 * Math.PI * 440 * t) * 0.5 + Math.Sin(2 * Math.PI * 660 * t) * 0.25;
            pcm[i] = (short)Math.Round(s * 32767);
        }

        var cod = AdpcmEncode(pcm);
        var tr = new Track("audio",
            new Dictionary<string, string>
            {
                { "sr", sampleRate.ToString() },
                { "codec", "adpcm4" },
                { "samples", pcm.Length.ToString() }
            },
            new List<Chunk> { new(cod, CRC32.Compute(cod)) });
        var list = new List<Track>(mux.Tracks);
        list.Add(tr);
        return new Mux(list);
    }

    public static byte[] AdpcmEncode(short[] pcm)
    {
        var idx = 0;
        var step = 7; // simple
        var outb = new List<byte>();
        for (var i = 0; i < pcm.Length; i += 2)
        {
            byte b = 0;
            for (var k = 0; k < 2; k++)
            {
                if (i + k >= pcm.Length)
                {
                    b <<= 4;
                    continue;
                }

                var s = pcm[i + k];
                var code = (s >> 12) & 0xF;
                b = (byte)((b << 4) | code);
            }

            outb.Add(b);
        }

        return outb.ToArray();
    }

    public static short[] AdpcmDecode(ReadOnlySpan<byte> data, int sampleCount)
    {
        if (sampleCount <= 0) return Array.Empty<short>();

        var pcm = new short[sampleCount];
        var pos = 0;

        foreach (var b in data)
        {
            if (pos >= sampleCount) break;
            pcm[pos++] = ExpandNibble(b >> 4);
            if (pos >= sampleCount) break;
            pcm[pos++] = ExpandNibble(b & 0xF);
        }

        return pcm;
    }

    public static string? ExtractDemoAudio(Mux mux, string outputWavePath)
    {
        if (mux == null) throw new ArgumentNullException(nameof(mux));

        var track = mux.Tracks.FirstOrDefault(t =>
            t.Type == "audio" &&
            t.Meta.TryGetValue("codec", out var codec) &&
            codec == "adpcm4");

        if (track == null) return null;

        var sampleRate = track.Meta.TryGetValue("sr", out var srStr) && int.TryParse(srStr, out var srVal)
            ? srVal
            : 16000;

        var sampleCount = track.Meta.TryGetValue("samples", out var countStr) && int.TryParse(countStr, out var countVal)
            ? countVal
            : track.Chunks.Sum(c => c.Data.Length * 2);

        var totalBytes = track.Chunks.Sum(c => c.Data.Length);
        var buffer = new byte[totalBytes];
        var offset = 0;
        foreach (var chunk in track.Chunks)
        {
            Buffer.BlockCopy(chunk.Data, 0, buffer, offset, chunk.Data.Length);
            offset += chunk.Data.Length;
        }

        var pcm = AdpcmDecode(buffer, sampleCount);
        WriteWave(outputWavePath, pcm, sampleRate);
        return outputWavePath;
    }

    private static short ExpandNibble(int code)
    {
        if (code >= 8) code -= 16;
        return (short)(code << 12);
    }

    private static void WriteWave(string path, IReadOnlyList<short> pcm, int sampleRate)
    {
        using var fs = File.Create(path);
        using var bw = new BinaryWriter(fs, Encoding.ASCII, leaveOpen: false);

        var subchunk2Size = pcm.Count * sizeof(short);
        var byteRate = sampleRate * sizeof(short);
        const short blockAlign = sizeof(short);

        bw.Write(Encoding.ASCII.GetBytes("RIFF"));
        bw.Write(36 + subchunk2Size);
        bw.Write(Encoding.ASCII.GetBytes("WAVE"));
        bw.Write(Encoding.ASCII.GetBytes("fmt "));
        bw.Write(16); // PCM header size
        bw.Write((short)1); // format (PCM)
        bw.Write((short)1); // channels (mono)
        bw.Write(sampleRate);
        bw.Write(byteRate);
        bw.Write(blockAlign);
        bw.Write((short)16); // bits per sample
        bw.Write(Encoding.ASCII.GetBytes("data"));
        bw.Write(subchunk2Size);
        foreach (var sample in pcm) bw.Write(sample);
    }
}
