using System.Security.Cryptography;
using System.Text.Json;

namespace RoggenCore.Core;

public static class FirmwarePackageLoader
{
    public static async Task<FirmwarePackage> LoadAsync(string manifestPath, CancellationToken cancellationToken = default)
    {
        var fullManifestPath = Path.GetFullPath(manifestPath);
        await using var stream = File.OpenRead(fullManifestPath);
        var manifest = await JsonSerializer.DeserializeAsync<FirmwareManifest>(stream, new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true
        }, cancellationToken) ?? throw new InvalidDataException("Firmware manifest is empty.");
        if (manifest.SchemaVersion != 1 || string.IsNullOrWhiteSpace(manifest.Name) ||
            string.IsNullOrWhiteSpace(manifest.Version) || manifest.AllowedRanges.Count == 0)
            throw new InvalidDataException("Firmware manifest is incomplete or unsupported.");
        var directory = Path.GetDirectoryName(fullManifestPath)!;
        var hexPath = Path.GetFullPath(Path.Combine(directory, manifest.HexFile));
        if (!hexPath.StartsWith(directory, StringComparison.OrdinalIgnoreCase) || !File.Exists(hexPath))
            throw new InvalidDataException("Firmware file is missing or outside its package.");
        var bytes = await File.ReadAllBytesAsync(hexPath, cancellationToken);
        var sha = Convert.ToHexString(SHA256.HashData(bytes));
        var crc = Crc32.Compute(bytes);
        if (!sha.Equals(manifest.HexSha256, StringComparison.OrdinalIgnoreCase) || crc != manifest.HexCrc32)
            throw new InvalidDataException("Firmware package CRC/SHA-256 verification failed.");
        var image = IntelHexImage.Parse(System.Text.Encoding.ASCII.GetString(bytes));
        image.ValidateRanges(manifest.AllowedRanges, manifest.FlashSize);
        return new FirmwarePackage(directory, fullManifestPath, hexPath, manifest);
    }
}
