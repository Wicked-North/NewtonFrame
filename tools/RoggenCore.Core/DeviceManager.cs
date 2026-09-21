using System.Security.Cryptography;

namespace RoggenCore.Core;

public sealed class DeviceManager(AppStorage storage)
{
    public async Task<DeviceInfo> ProbeAsync(Esp32BridgeClient bridge, CancellationToken cancellationToken = default)
    {
        var swdId = await bridge.InitializeAsync(cancellationToken);
        var unlocked = await bridge.IsUnlockedAsync(cancellationToken);
        var part = unlocked ? await bridge.ReadRegisterAsync(0x10000100, cancellationToken) : 0u;
        var id0 = unlocked ? await bridge.ReadRegisterAsync(0x10000060, cancellationToken) : 0u;
        var id1 = unlocked ? await bridge.ReadRegisterAsync(0x10000064, cancellationToken) : 0u;
        var flashSize = unlocked ? checked((int)(await bridge.ReadRegisterAsync(0x10000010, cancellationToken) *
                                                   await bridge.ReadRegisterAsync(0x10000014, cancellationToken))) : 0;
        var boardWord = unlocked ? await bridge.ReadRegisterAsync(0x10001088, cancellationToken) : 0u;
        var model = part == 0x00052811 && boardWord == 0x58031700 ? "EL060H6W4A · 4-color" : "Unknown panel";
        return new DeviceInfo(swdId, part, ((ulong)id1 << 32) | id0, flashSize, unlocked, boardWord,
            model, model.StartsWith("EL060") ? 648 : 0, model.StartsWith("EL060") ? 480 : 0);
    }

    public async Task<BackupRecord> BackupAsync(Esp32BridgeClient bridge, DeviceInfo device,
        string firmwareName, string firmwareVersion, bool knownGood,
        IProgress<OperationProgress>? progress = null, CancellationToken cancellationToken = default)
    {
        if (!device.IsUnlocked || device.FlashSize <= 0) throw new InvalidOperationException("An unlocked target is required for backup.");
        var id = $"{device.DeviceId:X16}-{DateTimeOffset.UtcNow:yyyyMMdd-HHmmss}Z";
        var directory = Path.Combine(storage.BackupsDirectory, id);
        Directory.CreateDirectory(directory);
        var flashPath = Path.Combine(directory, "flash.bin");
        var uicrPath = Path.Combine(directory, "uicr.bin");
        var flash = await bridge.DownloadFlashAsync(device.FlashSize, progress, cancellationToken);
        await File.WriteAllBytesAsync(flashPath, flash, cancellationToken);
        progress?.Report(new("Backup", 70, "Reading UICR"));
        var uicr = await bridge.DumpUicrAsync(progress, cancellationToken);
        await File.WriteAllBytesAsync(uicrPath, uicr, cancellationToken);
        var record = new BackupRecord(id, DateTimeOffset.UtcNow, device.DeviceId, device.Part, device.FlashSize,
            flashPath, uicrPath, Convert.ToHexString(SHA256.HashData(flash)), Crc32.Compute(flash),
            Convert.ToHexString(SHA256.HashData(uicr)), Crc32.Compute(uicr), firmwareName, firmwareVersion, knownGood);
        await storage.SaveBackupMetadataAsync(record, cancellationToken);
        var state = await storage.LoadStateAsync(cancellationToken);
        var key = device.DeviceId.ToString("X16");
        state.Devices.TryGetValue(key, out var old);
        state.Devices[key] = new DeviceState
        {
            DeviceId = key,
            DisplayModel = device.DisplayModel,
            LastBackupId = id,
            LastKnownGoodBackupId = knownGood ? id : old?.LastKnownGoodBackupId,
            FirmwareName = firmwareName,
            FirmwareVersion = firmwareVersion,
            FirmwareSha256 = record.FlashSha256,
            FirmwareCrc32 = record.FlashCrc32,
            UpdatedUtc = DateTimeOffset.UtcNow
        };
        state.LastBridgeAddress = bridge.BaseUri.GetLeftPart(UriPartial.Authority);
        await storage.SaveStateAsync(state, cancellationToken);
        progress?.Report(new("Backup", 100, "CRC-32 and SHA-256 recorded"));
        return record;
    }

    public async Task<IReadOnlyList<DiagnosticResult>> RunDiagnosticsAsync(Esp32BridgeClient bridge,
        DeviceInfo device, IProgress<OperationProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        var results = new List<DiagnosticResult>
        {
            new("SWD identity", device.SwdId == 0x2BA01477, $"0x{device.SwdId:X8}"),
            new("Protection", device.IsUnlocked, device.IsUnlocked ? "Unlocked" : "Locked"),
            new("nRF52811 part", device.Part == 0x00052811, $"0x{device.Part:X8}"),
            new("Flash geometry", device.FlashSize == 196608, $"{device.FlashSize:N0} bytes"),
            new("Board identity", device.BoardWord == 0x58031700, $"0x{device.BoardWord:X8}"),
            new("Display profile", device.DisplayWidth == 648 && device.DisplayHeight == 480, device.DisplayModel)
        };
        if (device.IsUnlocked)
        {
            var first = await bridge.ReadRegisterAsync(0, cancellationToken);
            results.Add(new("Readable vector table", first >= 0x20000000 && first <= 0x20006000, $"SP 0x{first:X8}"));
            progress?.Report(new("Diagnostics", 40, "Reading full flash for integrity tests"));
            var flash = await bridge.DownloadFlashAsync(device.FlashSize, progress, cancellationToken);
            var appSp = BitConverter.ToUInt32(flash, 0x19000);
            var appPc = BitConverter.ToUInt32(flash, 0x19004);
            results.Add(new("RoggenCore stack vector", appSp >= 0x20001AE0 && appSp <= 0x20006000, $"0x{appSp:X8}"));
            results.Add(new("RoggenCore reset vector", appPc >= 0x00019001 && appPc < 0x0001B000 && (appPc & 1) == 1, $"0x{appPc:X8}"));
            var crc = Crc32.Compute(flash);
            var sha = Convert.ToHexString(SHA256.HashData(flash));
            results.Add(new("Full flash CRC-32", true, $"{crc:X8}"));
            var state = await storage.LoadStateAsync(cancellationToken);
            if (state.Devices.TryGetValue(device.DeviceId.ToString("X16"), out var saved) && saved.FirmwareSha256 is not null)
                results.Add(new("Last-known-good image", sha.Equals(saved.FirmwareSha256, StringComparison.OrdinalIgnoreCase), sha));
            else
                results.Add(new("Full flash SHA-256", true, sha));
        }
        return results;
    }

    public async Task InstallAsync(Esp32BridgeClient bridge, DeviceInfo device, FirmwarePackage package,
        BackupRecord baseline, IProgress<OperationProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        if (!device.IsUnlocked) throw new InvalidOperationException("Target must be unlocked before installation.");
        if (device.Part.ToString("X8") != package.Manifest.TargetPart || device.FlashSize != package.Manifest.FlashSize)
            throw new InvalidOperationException("Firmware package does not match this target.");
        var baselineBytes = await File.ReadAllBytesAsync(baseline.FlashFile, cancellationToken);
        var image = IntelHexImage.Parse(await File.ReadAllTextAsync(package.HexPath, cancellationToken));
        var ranges = package.Manifest.AllowedRanges.OrderBy(item => item.Start).ToArray();
        var expected = FirmwareImage.BuildExpectedUpgrade(baselineBytes, image, ranges);
        var pages = ranges.SelectMany(range => Enumerable.Range((int)(range.Start / 0x400),
                (int)((range.Length + 0x3FF) / 0x400)))
            .Distinct().Order().ToArray();
        for (var index = 0; index < pages.Length; index++)
        {
            var address = (uint)pages[index] * 0x400u;
            progress?.Report(new("Erasing application", index * 25d / pages.Length, $"Page 0x{address:X5}"));
            var response = await bridge.ErasePageAsync(address, cancellationToken);
            if (!response.Contains("Page erased", StringComparison.OrdinalIgnoreCase))
                throw new IOException($"Page erase failed: {response}");
        }
        progress?.Report(new("Uploading firmware", 30, package.Manifest.Name));
        await bridge.UploadHexAsync(package.HexPath, cancellationToken);
        progress?.Report(new("Verifying firmware", 60, "Reading all target flash"));
        var actual = await bridge.DownloadFlashAsync(device.FlashSize, progress, cancellationToken);
        if (!actual.AsSpan().SequenceEqual(expected))
        {
            var mismatch = Enumerable.Range(0, actual.Length).First(i => actual[i] != expected[i]);
            throw new IOException($"Readback mismatch at 0x{mismatch:X5}; target was not started.");
        }
        progress?.Report(new("Booting", 95, "Verified; releasing CPU"));
        await bridge.ResetAndRunAsync(cancellationToken);
        progress?.Report(new("Complete", 100, $"{package.Manifest.Name} {package.Manifest.Version} installed"));
    }

    public async Task RecoverAndInstallAsync(Esp32BridgeClient bridge, DeviceInfo device, FirmwarePackage package,
        string confirmation, IProgress<OperationProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        RecoveryConfirmation.Validate(confirmation, device.DeviceId);
        if (!package.Manifest.RecoveryCapable)
            throw new InvalidOperationException("This package is upgrade-only and cannot recover an erased target.");
        if (package.Manifest.AllowedRanges.All(range => range.Start != 0))
            throw new InvalidOperationException("Recovery package does not contain the MBR/SoftDevice region.");

        progress?.Report(new("Recovery erase", 5, "Erasing target flash and protection configuration"));
        await bridge.RecoveryEraseAsync(cancellationToken);
        uint swdId = 0;
        var unlocked = false;
        for (var attempt = 0; attempt < 3 && !unlocked; attempt++)
        {
            await Task.Delay(attempt == 0 ? 1000 : 500, cancellationToken);
            try
            {
                swdId = await bridge.InitializeAsync(cancellationToken);
                unlocked = swdId == 0x2BA01477 && await bridge.IsUnlockedAsync(cancellationToken);
            }
            catch (HttpRequestException) when (attempt < 2) { }
            catch (IOException) when (attempt < 2) { }
        }
        if (!unlocked)
            throw new InvalidOperationException("Target did not unlock after recovery erase.");

        foreach (var item in package.Manifest.UicrWords)
        {
            var address = Convert.ToUInt32(item.Key.Replace("0x", "", StringComparison.OrdinalIgnoreCase), 16);
            var value = Convert.ToUInt32(item.Value.Replace("0x", "", StringComparison.OrdinalIgnoreCase), 16);
            await bridge.WriteFlashWordAsync(address, value, cancellationToken);
            if (await bridge.ReadRegisterAsync(address, cancellationToken) != value)
                throw new IOException($"UICR verification failed at 0x{address:X8}.");
        }

        progress?.Report(new("Uploading recovery image", 35, package.Manifest.Name));
        await bridge.UploadHexAsync(package.HexPath, cancellationToken);
        var blank = Enumerable.Repeat((byte)0xFF, package.Manifest.FlashSize).ToArray();
        var image = IntelHexImage.Parse(await File.ReadAllTextAsync(package.HexPath, cancellationToken));
        var expected = image.ApplyTo(blank);
        progress?.Report(new("Verifying recovery", 65, "Reading all target flash"));
        var actual = await bridge.DownloadFlashAsync(package.Manifest.FlashSize, progress, cancellationToken);
        if (!actual.AsSpan().SequenceEqual(expected))
        {
            var mismatch = Enumerable.Range(0, actual.Length).First(i => actual[i] != expected[i]);
            throw new IOException($"Recovery readback mismatch at 0x{mismatch:X5}; target was not started.");
        }
        await bridge.ResetAndRunAsync(cancellationToken);
        progress?.Report(new("Recovery complete", 100, "Verified firmware started"));
    }

    public async Task RestoreBackupAsync(Esp32BridgeClient bridge, DeviceInfo device, BackupRecord backup,
        IProgress<OperationProgress>? progress = null, CancellationToken cancellationToken = default)
    {
        if (!device.IsUnlocked || backup.DeviceId != device.DeviceId || backup.FlashSize != device.FlashSize)
            throw new InvalidOperationException("Backup does not match the connected unlocked target.");
        var image = await File.ReadAllBytesAsync(backup.FlashFile, cancellationToken);
        if (image.Length != device.FlashSize || Crc32.Compute(image) != backup.FlashCrc32 ||
            !Convert.ToHexString(SHA256.HashData(image)).Equals(backup.FlashSha256, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("Backup CRC/SHA-256 verification failed.");
        var pages = device.FlashSize / 0x400;
        for (var page = 0; page < pages; page++)
        {
            progress?.Report(new("Restoring backup", page * 45d / pages, $"Erasing page {page + 1} of {pages}"));
            await bridge.ErasePageAsync((uint)(page * 0x400), cancellationToken);
        }
        progress?.Report(new("Restoring backup", 50, "Uploading full flash image"));
        await bridge.UploadBinaryAsync(backup.FlashFile, 0, cancellationToken);
        var actual = await bridge.DownloadFlashAsync(device.FlashSize, progress, cancellationToken);
        if (!actual.AsSpan().SequenceEqual(image))
        {
            var mismatch = Enumerable.Range(0, actual.Length).First(i => actual[i] != image[i]);
            throw new IOException($"Rollback readback mismatch at 0x{mismatch:X5}; target was not started.");
        }
        await bridge.ResetAndRunAsync(cancellationToken);
        progress?.Report(new("Rollback complete", 100, "Backup restored and verified"));
    }
}
