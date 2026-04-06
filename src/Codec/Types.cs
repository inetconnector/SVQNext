// SPDX-License-Identifier: AGPL-3.0-or-later

namespace SVQNext.Codec;

public struct EncodedFrame
{
    public short[,,] MV;
    public byte[] Modes;
    public byte[] PredictionModes;
    public byte[] PartitionModes;
    public byte[] IntraModes;
    public byte[] ResidualModes;
    public short[] TxQ;
    public ushort[] Idx;
    public short[] GainQ;
    public short[] DCq;
    public (int Hc, int Wc) Shape;
    public bool IsB;
    public bool IsRefresh;
    public bool UseChromaPred;
    public int RefPrev;
    public int RefNext;
}

public struct EncodedSequence
{
    public EncodedFrame[] Frames;
    public int T, H, W, BS, QMotion, Search, GOP;
    public string SearchMode;
    public bool Loop, UseB;
}

public static class BlockCodingMode
{
    public const byte Skip = 0;
    public const byte InterFull = 1;
    public const byte InterSplit = 2;
    public const byte IntraDcFull = 3;
    public const byte IntraVerticalFull = 4;
    public const byte IntraHorizontalFull = 5;
    public const byte IntraDcSplit = 6;
    public const byte IntraVerticalSplit = 7;
    public const byte IntraHorizontalSplit = 8;
    public const byte IntraPlanarFull = 9;
    public const byte IntraPlanarSplit = 10;
    public const byte IntraDiagonalFull = 11;
    public const byte IntraDiagonalSplit = 12;

    public static bool IsSplit(byte mode)
    {
        return mode is InterSplit or IntraDcSplit or IntraVerticalSplit or IntraHorizontalSplit or IntraPlanarSplit or IntraDiagonalSplit;
    }

    public static bool IsIntra(byte mode)
    {
        return mode >= IntraDcFull;
    }
}

public static class PredictionMode
{
    public const byte Skip = 0;
    public const byte Inter = 1;
    public const byte Intra = 2;
}

public static class PartitionMode
{
    public const byte Full = 0;
    public const byte Split = 1;
}

public static class IntraPredictor
{
    public const byte Dc = 0;
    public const byte Vertical = 1;
    public const byte Horizontal = 2;
    public const byte Planar = 3;
    public const byte Diagonal = 4;
}

public static class BlockSyntax
{
    public static void DecodeLegacyMode(byte mode, out byte predictionMode, out byte partitionMode, out byte intraMode)
    {
        predictionMode = PredictionMode.Inter;
        partitionMode = PartitionMode.Full;
        intraMode = IntraPredictor.Dc;

        switch (mode)
        {
            case BlockCodingMode.Skip:
                predictionMode = PredictionMode.Skip;
                return;
            case BlockCodingMode.InterFull:
                predictionMode = PredictionMode.Inter;
                partitionMode = PartitionMode.Full;
                return;
            case BlockCodingMode.InterSplit:
                predictionMode = PredictionMode.Inter;
                partitionMode = PartitionMode.Split;
                return;
            case BlockCodingMode.IntraDcFull:
                predictionMode = PredictionMode.Intra;
                intraMode = IntraPredictor.Dc;
                return;
            case BlockCodingMode.IntraVerticalFull:
                predictionMode = PredictionMode.Intra;
                intraMode = IntraPredictor.Vertical;
                return;
            case BlockCodingMode.IntraHorizontalFull:
                predictionMode = PredictionMode.Intra;
                intraMode = IntraPredictor.Horizontal;
                return;
            case BlockCodingMode.IntraDcSplit:
                predictionMode = PredictionMode.Intra;
                partitionMode = PartitionMode.Split;
                intraMode = IntraPredictor.Dc;
                return;
            case BlockCodingMode.IntraVerticalSplit:
                predictionMode = PredictionMode.Intra;
                partitionMode = PartitionMode.Split;
                intraMode = IntraPredictor.Vertical;
                return;
            case BlockCodingMode.IntraHorizontalSplit:
                predictionMode = PredictionMode.Intra;
                partitionMode = PartitionMode.Split;
                intraMode = IntraPredictor.Horizontal;
                return;
            case BlockCodingMode.IntraPlanarFull:
                predictionMode = PredictionMode.Intra;
                intraMode = IntraPredictor.Planar;
                return;
            case BlockCodingMode.IntraPlanarSplit:
                predictionMode = PredictionMode.Intra;
                partitionMode = PartitionMode.Split;
                intraMode = IntraPredictor.Planar;
                return;
            case BlockCodingMode.IntraDiagonalFull:
                predictionMode = PredictionMode.Intra;
                intraMode = IntraPredictor.Diagonal;
                return;
            case BlockCodingMode.IntraDiagonalSplit:
                predictionMode = PredictionMode.Intra;
                partitionMode = PartitionMode.Split;
                intraMode = IntraPredictor.Diagonal;
                return;
            default:
                return;
        }
    }

    public static byte EncodeLegacyMode(byte predictionMode, byte partitionMode, byte intraMode)
    {
        if (predictionMode == PredictionMode.Skip)
            return BlockCodingMode.Skip;

        if (predictionMode == PredictionMode.Inter)
            return partitionMode == PartitionMode.Split ? BlockCodingMode.InterSplit : BlockCodingMode.InterFull;

        return (partitionMode, intraMode) switch
        {
            (PartitionMode.Full, IntraPredictor.Dc) => BlockCodingMode.IntraDcFull,
            (PartitionMode.Full, IntraPredictor.Vertical) => BlockCodingMode.IntraVerticalFull,
            (PartitionMode.Full, IntraPredictor.Horizontal) => BlockCodingMode.IntraHorizontalFull,
            (PartitionMode.Full, IntraPredictor.Planar) => BlockCodingMode.IntraPlanarFull,
            (PartitionMode.Full, IntraPredictor.Diagonal) => BlockCodingMode.IntraDiagonalFull,
            (PartitionMode.Split, IntraPredictor.Dc) => BlockCodingMode.IntraDcSplit,
            (PartitionMode.Split, IntraPredictor.Vertical) => BlockCodingMode.IntraVerticalSplit,
            (PartitionMode.Split, IntraPredictor.Horizontal) => BlockCodingMode.IntraHorizontalSplit,
            (PartitionMode.Split, IntraPredictor.Planar) => BlockCodingMode.IntraPlanarSplit,
            (PartitionMode.Split, IntraPredictor.Diagonal) => BlockCodingMode.IntraDiagonalSplit,
            _ => BlockCodingMode.IntraDcFull
        };
    }
}
