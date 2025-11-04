
// Public Domain
using SVQNext.Codec;

namespace SVQNext.Tools
{
    public static class FECRecover
    {
        // Attempt single-loss recovery per window using XOR parity (must have exactly one missing in the window)
        public static Mux Recover(Mux mux)
        {
            var video = mux.Tracks.First(t=>t.Type=="video");
            var fec = mux.Tracks.FirstOrDefault(t=>t.Type=="fec");
            if (fec==null || !fec.Meta.TryGetValue("type", out var typ) || typ!="xor-window") return mux;
            int window = int.Parse(fec.Meta["window"]);
            var frames = video.Chunks.ToList(); // include CONFIG at [0]
            int frameCount = frames.Count - 1;
            for (int w=0; w*window < frameCount; w++)
            {
                int start = 1 + w*window;
                int len = Math.Min(window, frameCount - w*window);
                // identify missing (zero-length) chunk as placeholder for demo; in real case, we would detect externally
                int missing = -1;
                for (int i=0;i<len;i++)
                {
                    if (frames[start+i].Data.Length==0) { missing=i; break; }
                }
                if (missing<0) continue;
                // reconstruct
                int maxLen=0;
                for (int i=0;i<len;i++) if (i!=missing) maxLen=Math.Max(maxLen, frames[start+i].Data.Length);
                var recon=new byte[maxLen];
                for (int i=0;i<len;i++)
                {
                    if (i==missing) continue;
                    var d=frames[start+i].Data;
                    for (int k=0;k<maxLen;k++)
                    {
                        byte b = k<d.Length? d[k] : (byte)0;
                        recon[k] ^= b;
                    }
                }
                var parity = fec.Chunks[w].Data;
                for (int k=0;k<maxLen;k++) recon[k] ^= (k<parity.Length? parity[k] : (byte)0);
                frames[start+missing] = new Chunk(recon, CRC32.Compute(recon));
            }
            var tnew = new Track("video", video.Meta, frames);
            var tracks = mux.Tracks.Where(t=>t.Type!="video").ToList();
            tracks.Insert(0, tnew);
            return new Mux(tracks);
        }
    }
}
