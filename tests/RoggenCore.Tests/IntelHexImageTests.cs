using RoggenCore.Core;

namespace RoggenCore.Tests;

public sealed class IntelHexImageTests
{
    [Fact]
    public void ParsesAndAppliesExtendedLinearAddressImage()
    {
        var image = IntelHexImage.Parse(":020000040001F9\n:0410000001020304E2\n:00000001FF\n");
        image.ValidateRanges([new FlashRange(0x11000, 4)], 0x30000);
        var result = image.ApplyTo(Enumerable.Repeat((byte)0xFF, 0x30000).ToArray());
        Assert.Equal(new byte[] { 1, 2, 3, 4 }, result[0x11000..0x11004]);
    }

    [Fact]
    public void RejectsBadChecksum() =>
        Assert.Throws<InvalidDataException>(() => IntelHexImage.Parse(":040000000102030400\n:00000001FF\n"));

    [Fact]
    public void RejectsWritesOutsideManifest()
    {
        var image = IntelHexImage.Parse(":0400000001020304F2\n:00000001FF\n");
        Assert.Throws<InvalidDataException>(() => image.ValidateRanges([new FlashRange(0x19000, 0x2000)], 0x30000));
    }
}
