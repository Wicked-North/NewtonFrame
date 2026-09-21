using RoggenCore.Core;
using System.Text;

namespace RoggenCore.Tests;

public sealed class Crc32Tests
{
    [Fact]
    public void StandardCheckVectorMatchesIeeeCrc32() =>
        Assert.Equal(0xCBF43926u, Crc32.Compute(Encoding.ASCII.GetBytes("123456789")));

    [Fact]
    public async Task StreamingAndMemoryResultsMatch()
    {
        var bytes = Enumerable.Range(0, 77760).Select(index => (byte)(index * 37)).ToArray();
        await using var stream = new MemoryStream(bytes);
        Assert.Equal(Crc32.Compute(bytes), await Crc32.ComputeAsync(stream));
    }
}
