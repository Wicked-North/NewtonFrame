using System.Security.Cryptography;
using System.Text.Json;

namespace RoggenCore.Core;

public sealed class AppStorage
{
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    public AppStorage(string? root = null)
    {
        Root = root ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "RoggenCore");
        BackupsDirectory = Path.Combine(Root, "backups");
        StatePath = Path.Combine(Root, "firmware-state.json");
        Directory.CreateDirectory(BackupsDirectory);
    }

    public string Root { get; }
    public string BackupsDirectory { get; }
    public string StatePath { get; }

    public async Task<ManagerState> LoadStateAsync(CancellationToken cancellationToken = default)
    {
        if (!File.Exists(StatePath)) return new ManagerState();
        await using var stream = File.OpenRead(StatePath);
        return await JsonSerializer.DeserializeAsync<ManagerState>(stream, JsonOptions, cancellationToken) ?? new ManagerState();
    }

    public async Task SaveStateAsync(ManagerState state, CancellationToken cancellationToken = default)
    {
        Directory.CreateDirectory(Root);
        var temporary = StatePath + ".tmp";
        await using (var stream = File.Create(temporary))
            await JsonSerializer.SerializeAsync(stream, state, JsonOptions, cancellationToken);
        File.Move(temporary, StatePath, true);
    }

    public async Task SaveBackupMetadataAsync(BackupRecord backup, CancellationToken cancellationToken = default)
    {
        var path = Path.Combine(BackupsDirectory, backup.Id, "backup.json");
        await using var stream = File.Create(path);
        await JsonSerializer.SerializeAsync(stream, backup, JsonOptions, cancellationToken);
    }

    public async Task<IReadOnlyList<BackupRecord>> ListBackupsAsync(CancellationToken cancellationToken = default)
    {
        var records = new List<BackupRecord>();
        foreach (var path in Directory.EnumerateFiles(BackupsDirectory, "backup.json", SearchOption.AllDirectories))
        {
            try
            {
                await using var stream = File.OpenRead(path);
                var item = await JsonSerializer.DeserializeAsync<BackupRecord>(stream, JsonOptions, cancellationToken);
                if (item is not null) records.Add(item);
            }
            catch (JsonException) { }
        }
        return records.OrderByDescending(item => item.CreatedUtc).ToList();
    }

    public async Task<int> ImportLegacyBackupsAsync(string sourceDirectory, CancellationToken cancellationToken = default)
    {
        if (!Directory.Exists(sourceDirectory)) return 0;
        var existing = (await ListBackupsAsync(cancellationToken)).Select(item => item.FlashSha256)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var imported = 0;
        foreach (var source in Directory.EnumerateFiles(sourceDirectory, "*.bin", SearchOption.AllDirectories))
        {
            cancellationToken.ThrowIfCancellationRequested();
            var info = new FileInfo(source);
            if (info.Length != 196608) continue;
            var hashes = await HashFileAsync(source, cancellationToken);
            if (!existing.Add(hashes.Sha256)) continue;
            var created = new DateTimeOffset(info.LastWriteTimeUtc, TimeSpan.Zero);
            var id = $"legacy-{created:yyyyMMdd-HHmmss}Z-{hashes.Sha256[..8]}";
            var directory = Path.Combine(BackupsDirectory, id);
            Directory.CreateDirectory(directory);
            var destination = Path.Combine(directory, "flash.bin");
            File.Copy(source, destination, true);
            var record = new BackupRecord(id, created, 0, 0x00052811, 196608, destination, "",
                hashes.Sha256, hashes.Crc32, "", 0, Path.GetFileNameWithoutExtension(source), "Legacy import", false);
            await SaveBackupMetadataAsync(record, cancellationToken);
            imported++;
        }
        return imported;
    }

    public static async Task<(string Sha256, uint Crc32)> HashFileAsync(string path, CancellationToken cancellationToken = default)
    {
        await using var shaStream = File.OpenRead(path);
        var sha = Convert.ToHexString(await SHA256.HashDataAsync(shaStream, cancellationToken));
        await using var crcStream = File.OpenRead(path);
        return (sha, await Crc32.ComputeAsync(crcStream, cancellationToken));
    }
}
