namespace RoggenCore.Core;

public static class Crc32
{
    public static uint Compute(ReadOnlySpan<byte> data)
    {
        var crc = 0xFFFFFFFFu;
        foreach (var value in data)
        {
            crc ^= value;
            for (var bit = 0; bit < 8; bit++)
                crc = (crc >> 1) ^ (0xEDB88320u & (uint)-(int)(crc & 1));
        }
        return crc ^ 0xFFFFFFFFu;
    }

    public static async Task<uint> ComputeAsync(Stream stream, CancellationToken cancellationToken = default)
    {
        var crc = 0xFFFFFFFFu;
        var buffer = new byte[64 * 1024];
        int count;
        while ((count = await stream.ReadAsync(buffer, cancellationToken)) > 0)
        {
            for (var index = 0; index < count; index++)
            {
                var value = buffer[index];
                crc ^= value;
                for (var bit = 0; bit < 8; bit++)
                    crc = (crc >> 1) ^ (0xEDB88320u & (uint)-(int)(crc & 1));
            }
        }
        return crc ^ 0xFFFFFFFFu;
    }
}
