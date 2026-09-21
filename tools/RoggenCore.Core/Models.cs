using System.Text.Json.Serialization;

namespace RoggenCore.Core;

public sealed record BridgeTarget(string Address, string Source, string? SerialPort = null)
{
    [JsonIgnore] public Uri BaseUri => new(Address.EndsWith('/') ? Address : Address + '/');
}

public sealed record DeviceInfo(
    uint SwdId,
    uint Part,
    ulong DeviceId,
    int FlashSize,
    bool IsUnlocked,
    uint BoardWord,
    string DisplayModel,
    int DisplayWidth,
    int DisplayHeight);

public sealed record BackupRecord(
    string Id,
    DateTimeOffset CreatedUtc,
    ulong DeviceId,
    uint Part,
    int FlashSize,
    string FlashFile,
    string UicrFile,
    string FlashSha256,
    uint FlashCrc32,
    string UicrSha256,
    uint UicrCrc32,
    string FirmwareName,
    string FirmwareVersion,
    bool KnownGood);

public sealed class ManagerState
{
    public int SchemaVersion { get; set; } = 1;
    public string? LastBridgeAddress { get; set; }
    public Dictionary<string, DeviceState> Devices { get; set; } = new(StringComparer.OrdinalIgnoreCase);
}

public sealed class DeviceState
{
    public string DeviceId { get; set; } = "";
    public string DisplayModel { get; set; } = "Unknown";
    public string? LastBackupId { get; set; }
    public string? LastKnownGoodBackupId { get; set; }
    public string FirmwareName { get; set; } = "Unknown";
    public string FirmwareVersion { get; set; } = "Unknown";
    public string? FirmwareSha256 { get; set; }
    public uint? FirmwareCrc32 { get; set; }
    public DateTimeOffset UpdatedUtc { get; set; }
}

public sealed class FirmwareManifest
{
    public int SchemaVersion { get; set; } = 1;
    public string Name { get; set; } = "";
    public string Version { get; set; } = "";
    public string TargetPart { get; set; } = "00052811";
    public int FlashSize { get; set; } = 196608;
    public string HexFile { get; set; } = "";
    public string HexSha256 { get; set; } = "";
    public uint HexCrc32 { get; set; }
    public List<FlashRange> AllowedRanges { get; set; } = [];
    public bool RecoveryCapable { get; set; }
    public Dictionary<string, string> UicrWords { get; set; } = new(StringComparer.OrdinalIgnoreCase);
}

public sealed record FlashRange(uint Start, uint Length);
public sealed record FirmwarePackage(string Directory, string ManifestPath, string HexPath, FirmwareManifest Manifest);
public sealed record OperationProgress(string Stage, double Percent, string Detail);
public sealed record DiagnosticResult(string Name, bool Passed, string Detail);
