// Public Domain
namespace SVQNext.Codec
{
    public struct EncodedFrame
    {
        public short[,,] MV;
        public ushort[] Idx;
        public short[] DCq;
        public (int Hc,int Wc) Shape;
        public bool IsB;
        public int RefPrev;
        public int RefNext;
    }
    public struct EncodedSequence
    {
        public EncodedFrame[] Frames;
        public int T,H,W,BS,QMotion,Search,GOP;
        public string SearchMode;
        public bool Loop, UseB;
    }
}
