// Public Domain
namespace SVQNext.Codec
{
    public static class Transform
    {
        public static void HadamardBlock(float[,] block, int bs) => MathUtil.Hadamard2D(block, bs);
        public static float[,] Sub(float[,] A,float[,] B)
        {
            int h=A.GetLength(0), w=A.GetLength(1);
            var R=new float[h,w];
            for (int y=0;y<h;y++) for (int x=0;x<w;x++) R[y,x]=A[y,x]-B[y,x];
            return R;
        }
    }
}
