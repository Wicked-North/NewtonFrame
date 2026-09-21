using RoggenCore.Core;

namespace RoggenCore.Tests;

public sealed class RecoveryConfirmationTests
{
    [Fact]
    public void ConfirmationIsBoundToExactDeviceId()
    {
        const ulong id = 0x123456789ABCDEF0;
        RecoveryConfirmation.Validate("ERASE 123456789ABCDEF0", id);
        Assert.Throws<InvalidOperationException>(() => RecoveryConfirmation.Validate("ERASE 123456789ABCDE00", id));
        Assert.Throws<InvalidOperationException>(() => RecoveryConfirmation.Validate("erase 123456789ABCDEF0", id));
    }

    [Fact]
    public void LockedTargetUsesExplicitRecoveryPhrase() =>
        Assert.Equal("ERASE LOCKED NRF52811", RecoveryConfirmation.RequiredPhrase(0));
}
