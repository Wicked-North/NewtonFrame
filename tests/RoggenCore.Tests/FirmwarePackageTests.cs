using RoggenCore.Core;

namespace RoggenCore.Tests;

public sealed class FirmwarePackageTests
{
    [Theory]
    [InlineData("1.1.0", "upgrade-manifest.json", false)]
    [InlineData("1.1.0", "recovery-manifest.json", true)]
    [InlineData("1.1.1", "upgrade-manifest.json", false)]
    [InlineData("1.1.1", "recovery-manifest.json", true)]
    public async Task ShippedPackagePassesAllIntegrityChecks(string version, string manifestName, bool recoveryCapable)
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "RoggenCore.sln")))
            directory = directory.Parent;
        Assert.NotNull(directory);
        var path = Path.Combine(directory!.FullName, "firmware", "RoggenCore", version, manifestName);
        var package = await FirmwarePackageLoader.LoadAsync(path);
        Assert.Equal("RoggenCore", package.Manifest.Name);
        Assert.Equal(version, package.Manifest.Version);
        Assert.Equal(recoveryCapable, package.Manifest.RecoveryCapable);
        Assert.NotEmpty(package.Manifest.AllowedRanges);
    }
}