// SPDX-License-Identifier: AGPL-3.0-or-later

using System.IO.Compression;

namespace SVQNext.Codec;

public static class Entropy
{
    public static byte[] Encode(byte[] data)
    {
        if (data == null) throw new ArgumentNullException(nameof(data));

        using var output = new MemoryStream();
        using (var deflate = new DeflateStream(output, CompressionLevel.SmallestSize, leaveOpen: true))
            deflate.Write(data, 0, data.Length);
        return output.ToArray();
    }

    public static byte[] Decode(byte[] cod)
    {
        if (cod == null) throw new ArgumentNullException(nameof(cod));

        using var input = new MemoryStream(cod);
        using var deflate = new DeflateStream(input, CompressionMode.Decompress);
        using var output = new MemoryStream();
        deflate.CopyTo(output);
        return output.ToArray();
    }
}
