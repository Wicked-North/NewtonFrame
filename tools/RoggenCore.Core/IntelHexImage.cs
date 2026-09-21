namespace RoggenCore.Core;

public sealed class IntelHexImage
{
    private readonly SortedDictionary<uint, byte> _bytes = [];
    public IReadOnlyDictionary<uint, byte> Bytes => _bytes;

    public static IntelHexImage Parse(string text)
    {
        var image = new IntelHexImage();
        uint baseAddress = 0;
        var sawEnd = false;
        foreach (var raw in text.Replace("\r", "").Split('\n'))
        {
            var line = raw.Trim();
            if (line.Length == 0) continue;
            if (line[0] != ':' || line.Length < 11 || (line.Length & 1) == 0)
                throw new InvalidDataException("Invalid Intel HEX record.");
            var record = Convert.FromHexString(line[1..]);
            if (record.Length != record[0] + 5 || record.Aggregate(0, (sum, value) => (sum + value) & 0xFF) != 0)
                throw new InvalidDataException("Intel HEX checksum failed.");
            var offset = (uint)((record[1] << 8) | record[2]);
            switch (record[3])
            {
                case 0x00:
                    for (var i = 0; i < record[0]; i++)
                    {
                        var address = checked(baseAddress + offset + (uint)i);
                        if (!image._bytes.TryAdd(address, record[4 + i]))
                            throw new InvalidDataException($"Overlapping Intel HEX data at 0x{address:X8}.");
                    }
                    break;
                case 0x01:
                    sawEnd = true;
                    break;
                case 0x02:
                    if (record[0] != 2) throw new InvalidDataException("Invalid extended segment record.");
                    baseAddress = (uint)((record[4] << 8) | record[5]) << 4;
                    break;
                case 0x04:
                    if (record[0] != 2) throw new InvalidDataException("Invalid extended linear record.");
                    baseAddress = (uint)((record[4] << 8) | record[5]) << 16;
                    break;
                case 0x03:
                case 0x05:
                    break;
                default:
                    throw new InvalidDataException($"Unsupported Intel HEX record type 0x{record[3]:X2}.");
            }
        }
        if (!sawEnd || image._bytes.Count == 0) throw new InvalidDataException("Intel HEX is incomplete.");
        return image;
    }

    public void ValidateRanges(IEnumerable<FlashRange> ranges, int flashSize)
    {
        var allowed = ranges.ToArray();
        foreach (var address in _bytes.Keys)
        {
            if (address >= flashSize || !allowed.Any(range => address >= range.Start && address < range.Start + range.Length))
                throw new InvalidDataException($"Firmware writes outside its manifest at 0x{address:X8}.");
        }
    }

    public byte[] ApplyTo(ReadOnlySpan<byte> baseline)
    {
        var result = baseline.ToArray();
        foreach (var item in _bytes)
        {
            if (item.Key >= result.Length) throw new InvalidDataException("Firmware exceeds target flash.");
            result[item.Key] = item.Value;
        }
        return result;
    }
}
