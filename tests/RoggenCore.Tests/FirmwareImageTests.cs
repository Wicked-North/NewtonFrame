using RoggenCore.Core;

namespace RoggenCore.Tests;

public sealed class FirmwareImageTests
{
    [Fact]
    public void ExpectedUpgradeErasesWholePageBeforeApplyingHex()
    {
        var baseline = Enumerable.Repeat((byte)0x55, 0x1000).ToArray();
        var image = IntelHexImage.Parse(":020000040000FA\n:0404000001020304EE\n:00000001FF\n");
        var expected = FirmwareImage.BuildExpectedUpgrade(baseline, image, [new FlashRange(0x400, 0x400)]);
        Assert.All(expected[0x400..0x404], value => Assert.NotEqual(0xFF, value));
        Assert.All(expected[0x404..0x800], value => Assert.Equal(0xFF, value));
        Assert.Equal(0x55, expected[0x3FF]);
        Assert.Equal(0x55, expected[0x800]);
    }
}