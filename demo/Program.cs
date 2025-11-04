
using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using SVQNext.Compat;
using SVQNext.Optimized;
using SVQNext.Pipeline;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;

static byte[] Bytes(int size, int seed=123)
{
    var rng = new Random(seed);
    var buf = new byte[size];
    rng.NextBytes(buf);
    return buf;
}

static string Sha256(byte[] data)
{
    using var sha = SHA256.Create();
    return Convert.ToHexString(sha.ComputeHash(data));
}

Console.WriteLine("SVQNext Demo: encode/decode pipeline with ImageSharp + FEC");

Directory.CreateDirectory("out");

using var img = Image.Load<Rgba32>(new MemoryStream(Bytes(640*360*3)));
// Apply a simple map to exercise ImageSharp fast path
SVQNext.Optimized.ImageFastOps.MapRows(img, px => new Rgba32(px.R, px.G, (byte)(255 - px.B), 255));
SVQNext.Compat.ImageSharpCompat.SaveBitmap(img, Path.Combine("out", "demo.png"));

var payload = File.ReadAllBytes(Path.Combine("out", "demo.png"));
Console.WriteLine($"Input size: {payload.Length} bytes, sha256={Sha256(payload)}");

// FEC encode+recover
var fec = FecPipelineHook.EncodeWithFec(payload, redundancy: 20);
Console.WriteLine($"FEC size: {fec.Length} bytes");

var rec = FecPipelineHook.RecoverWithFec(fec);
Console.WriteLine($"REC size: {rec.Length} bytes, sha256={Sha256(rec)}");

// Show optimized primitive usage
var a = Bytes(1<<16, 1);
var b = Bytes(1<<16, 2);
var sad = SVQNext.Optimized.CodecCoreOptimized.SumAbsDiff(a, b);
Console.WriteLine($"SAD(64KiB)={sad}");

Console.WriteLine("OK");
