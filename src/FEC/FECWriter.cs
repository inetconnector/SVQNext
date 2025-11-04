
// Public Domain
using SVQNext.Codec;

namespace SVQNext.Tools
{
    public static class FECWriter
    {
        // Window XOR parity: for each group of W data chunks, add 1 parity chunk = XOR of group
        public static Track BuildFecTrack(Track video, int window=8)
        {
            var meta=new Dictionary<string,string>{{"of","video"},{"type","xor-window"},{"window",window.ToString()}};
            var chunks=new List<Chunk>();
            // align to frames (skip CONFIG at index 0)
            var frames=video.Chunks.Skip(1).ToList();
            for (int i=0;i<frames.Count; i+=window)
            {
                int len = Math.Min(window, frames.Count - i);
                if (len<=0) break;
                // find max length to align XOR
                int maxLen = frames.Skip(i).Take(len).Max(c=>c.Data.Length);
                var parity=new byte[maxLen];
                for (int j=0;j<len;j++)
                {
                    var d=frames[i+j].Data;
                    for (int k=0;k<maxLen;k++)
                    {
                        byte b = k<d.Length? d[k] : (byte)0;
                        parity[k] ^= b;
                    }
                }
                chunks.Add(new Chunk(parity, CRC32.Compute(parity)));
            }
            return new Track("fec", meta, chunks);
        }
    }
}
