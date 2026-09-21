using RoggenCore.Core;

namespace RoggenCore.Tests;

public sealed class FirmwarePackageTests
{
    [Theory]
    [InlineData("upgrade-manifest.json", false)]
    [InlineData("recovery-manifest.json", true)]
    public async Task ShippedPackagePassesAllIntegrityChecks(string manifestName, bool recoveryCapable)
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "RoggenCore.sln")))
            directory = directory.Parent;
        Assert.NotNull(directory);
        var path = Path.Combine(directory!.FullName, "firmware", "RoggenCore", "1.1.0", manifestName);
        var package = await FirmwarePackageLoader.LoadAsync(path);
        Assert.Equal("RoggenCore", package.Manifest.Name);
        Assert.Equal(recoveryCapable, package.Manifest.RecoveryCapable);
        Assert.NotEmpty(package.Manifest.AllowedRanges);
    }
}