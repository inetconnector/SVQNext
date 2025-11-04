
using System;
using System.IO;
using System.Threading.Tasks;

namespace SVQNext.Pipeline
{
    /// <summary>
    /// Simple wrapper to integrate FEC writer/recover into the existing pipeline without touching legacy code.
    /// </summary>
    public static class FecPipelineHook
    {
        public static byte[] EncodeWithFec(byte[] payload, int redundancy = 20)
        {
            // If FECWriter available in integrated code (from v9), prefer it; else return original payload.
            var writerType = Type.GetType("FECWriter"); // best-effort dynamic locate
            if (writerType == null) return payload;
            // Pseudo dynamic usage – replace with your actual FEC API if different:
            try
            {
                dynamic inst = Activator.CreateInstance(writerType);
                return (byte[])writerType.GetMethod("Encode")?.Invoke(inst, new object[]{ payload, redundancy });
            }
            catch
            {
                return payload;
            }
        }

        public static byte[] RecoverWithFec(byte[] payload)
        {
            var recType = Type.GetType("FECRecover");
            if (recType == null) return payload;
            try
            {
                dynamic inst = Activator.CreateInstance(recType);
                return (byte[])recType.GetMethod("Recover")?.Invoke(inst, new object[]{ payload });
            }
            catch
            {
                return payload;
            }
        }
    }
}
