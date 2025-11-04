// Public Domain
using System;
using System.Collections.Generic;
using SVQNext.Codec;

namespace SVQNext.Tools
{
    public static class Subtitles
    {
        public static Mux AttachDemoSubs(Mux mux)
        {
            string srt = "1\n00:00:00,000 --> 00:00:02,000\nSVQ-Next v5 Demo\n\n2\n00:00:02,000 --> 00:00:05,000\nPatent-free and public domain.\n";
            var data = System.Text.Encoding.UTF8.GetBytes(srt);
            var tr = new Track("subs", new Dictionary<string,string>{{"format","srt"}}, new List<Chunk>{ new Chunk(data, Codec.CRC32.Compute(data)) });
            var list=new List<Track>(mux.Tracks); list.Add(tr);
            return new Mux(list);
        }
    }
}
