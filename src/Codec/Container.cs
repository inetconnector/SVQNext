// Public Domain
using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace SVQNext.Codec
{
    // Simple chunked container: .svqpack
    // [MAGIC SVQ5][ver][trackCount]
    // Each track: header + list of chunks with CRC32
    public record Chunk(byte[] Data, uint Crc);
    public record Track(string Type, Dictionary<string,string> Meta, List<Chunk> Chunks);
    public record Mux(List<Track> Tracks);

    public static class Container
    {
        public static void Write(string path, Mux mux)
        {
            using var fs=File.Create(path);
            using var bw=new BinaryWriter(fs, Encoding.UTF8, false);
            bw.Write(new byte[]{ (byte)'S',(byte)'V',(byte)'Q',(byte)'5' });
            bw.Write(1); // version
            bw.Write(mux.Tracks.Count);
            foreach (var t in mux.Tracks)
            {
                bw.Write(t.Type);
                bw.Write(t.Meta.Count);
                foreach (var kv in t.Meta){ bw.Write(kv.Key); bw.Write(kv.Value); }
                bw.Write(t.Chunks.Count);
                foreach (var c in t.Chunks)
                {
                    bw.Write(c.Data.Length);
                    bw.Write(c.Data);
                    bw.Write(c.Crc);
                }
            }
        }

        public static Mux Read(string path)
        {
            using var fs=File.OpenRead(path);
            using var br=new BinaryReader(fs, Encoding.UTF8, false);
            var m=br.ReadBytes(4);
            if (m[0]!='S'||m[1]!='V'||m[2]!='Q'||m[3]!='5') throw new InvalidDataException("not svq5");
            int ver=br.ReadInt32(); if (ver!=1) throw new InvalidDataException("ver");
            int tracks=br.ReadInt32();
            var list=new List<Track>();
            for (int i=0;i<tracks;i++)
            {
                string type=br.ReadString();
                int metaN=br.ReadInt32(); var meta=new Dictionary<string,string>();
                for (int j=0;j<metaN;j++){ meta[br.ReadString()]=br.ReadString(); }
                int cnt=br.ReadInt32(); var chunks=new List<Chunk>(cnt);
                for (int j=0;j<cnt;j++)
                {
                    int len=br.ReadInt32();
                    var data=br.ReadBytes(len);
                    uint crc=br.ReadUInt32();
                    if (CRC32.Compute(data)!=crc) throw new InvalidDataException("CRC mismatch");
                    chunks.Add(new Chunk(data, crc));
                }
                list.Add(new Track(type, meta, chunks));
            }
            return new Mux(list);
        }
    }

    public static class CRC32
    {
        static readonly uint[] T=Gen();
        static uint[] Gen()
        {
            var t=new uint[256];
            for (uint i=0;i<256;i++)
            {
                uint c=i;
                for (int j=0;j<8;j++) c = (c&1)!=0 ? 0xEDB88320u ^ (c>>1) : (c>>1);
                t[i]=c;
            }
            return t;
        }
        public static uint Compute(byte[] data)
        {
            uint c=0xFFFFFFFFu;
            for (int i=0;i<data.Length;i++) c=T[(c^data[i])&0xFF] ^ (c>>8);
            return c^0xFFFFFFFFu;
        }
    }
}
