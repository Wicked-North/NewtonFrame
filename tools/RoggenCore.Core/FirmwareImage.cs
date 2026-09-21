namespace RoggenCore.Core;

public static class FirmwareImage
{
    public static byte[] BuildExpectedUpgrade(ReadOnlySpan<byte> baseline, IntelHexImage image,
        IEnumerable<FlashRange> ranges, int pageSize = 0x400)
    {
        var erased = baseline.ToArray();
        var pages = ranges.SelectMany(range => Enumerable.Range((int)(range.Start / (uint)pageSize),
                (int)((range.Length + (uint)pageSize - 1) / (uint)pageSize)))
            .Distinct().Order().ToArray();
        foreach (var page in pages)
            Array.Fill(erased, (byte)0xFF, page * pageSize, Math.Min(pageSize, erased.Length - page * pageSize));
        return image.ApplyTo(erased);
    }
}