using System.Net.Http.Headers;
using System.Text.RegularExpressions;

namespace RoggenCore.Core;

public sealed partial class Esp32BridgeClient : IDisposable
{
    private readonly HttpClient _http;

    public Esp32BridgeClient(Uri baseUri, TimeSpan? timeout = null)
    {
        BaseUri = baseUri;
        _http = new HttpClient { BaseAddress = baseUri, Timeout = timeout ?? TimeSpan.FromSeconds(30) };
    }

    public Uri BaseUri { get; }

    public async Task<string> GetStateAsync(CancellationToken cancellationToken = default) =>
        await GetTextAsync("get_state?cmd=0", HttpMethod.Get, cancellationToken);

    public async Task<uint> InitializeAsync(CancellationToken cancellationToken = default)
    {
        var text = await GetTextAsync("set_swd?cmd=init", HttpMethod.Post, cancellationToken);
        return ParseHexValue(text, "ID");
    }

    public async Task<bool> IsUnlockedAsync(CancellationToken cancellationToken = default)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, "set_swd?cmd=lock_state");
        using var response = await _http.SendAsync(request, cancellationToken);
        var text = await response.Content.ReadAsStringAsync(cancellationToken);
        response.EnsureSuccessStatusCode();
        if (text.Contains("nRF is locked", StringComparison.OrdinalIgnoreCase)) return false;
        EnsureNoBridgeError(text);
        return text.Contains("unlocked", StringComparison.OrdinalIgnoreCase);
    }

    public async Task<uint> ReadRegisterAsync(uint address, CancellationToken cancellationToken = default)
    {
        var text = await GetTextAsync($"set_swd?cmd=read_register&address={address:X8}", HttpMethod.Post, cancellationToken);
        return ParseHexValue(text, "value");
    }

    public Task<string> ErasePageAsync(uint address, CancellationToken cancellationToken = default) =>
        GetTextAsync($"flash_cmd?cmd=page_erase&address={address:X}", HttpMethod.Post, cancellationToken);

    public Task<string> EraseImageAsync(CancellationToken cancellationToken = default) =>
        GetTextAsync("prepare_epaper_image", HttpMethod.Post, cancellationToken);

    public Task<string> RecoveryEraseAsync(CancellationToken cancellationToken = default) =>
        GetTextAsync("set_swd?cmd=erase_all", HttpMethod.Post, cancellationToken);

    public Task<string> WriteFlashWordAsync(uint address, uint value, CancellationToken cancellationToken = default) =>
        GetTextAsync($"set_swd?cmd=write_flash&address={address:X8}&value={value:X8}", HttpMethod.Post, cancellationToken);

    public async Task UploadHexAsync(string path, CancellationToken cancellationToken = default)
    {
        await using var stream = File.OpenRead(path);
        using var form = new MultipartFormDataContent();
        var file = new StreamContent(stream);
        file.Headers.ContentType = new MediaTypeHeaderValue("text/plain");
        form.Add(file, "flash_file_direct", Path.GetFileName(path));
        using var response = await _http.PostAsync("flash_file_direct?flash_up_file_offset=0", form, cancellationToken);
        var text = await response.Content.ReadAsStringAsync(cancellationToken);
        response.EnsureSuccessStatusCode();
        EnsureNoBridgeError(text);
    }

    public async Task UploadBinaryAsync(string path, uint offset, CancellationToken cancellationToken = default)
    {
        var bytes = await File.ReadAllBytesAsync(path, cancellationToken);
        await UploadBytesAsync(bytes, offset, cancellationToken);
    }

    public async Task UploadBytesAsync(ReadOnlyMemory<byte> bytes, uint offset,
        CancellationToken cancellationToken = default)
    {
        const int chunkSize = 256;
        for (var position = 0; position < bytes.Length; position += chunkSize)
        {
            var count = Math.Min(chunkSize, bytes.Length - position);
            await UploadChunkAsync(bytes.Slice(position, count), offset + (uint)position, cancellationToken);
        }
    }

    private async Task UploadChunkAsync(ReadOnlyMemory<byte> bytes, uint offset,
        CancellationToken cancellationToken)
    {
        using var form = new MultipartFormDataContent();
        var file = new ByteArrayContent(bytes.ToArray());
        file.Headers.ContentType = new MediaTypeHeaderValue("application/octet-stream");
        form.Add(file, "flash_file_direct", "chunk.bin");
        using var response = await _http.PostAsync($"flash_file_direct?flash_up_file_offset={offset:X}", form, cancellationToken);
        var text = await response.Content.ReadAsStringAsync(cancellationToken);
        response.EnsureSuccessStatusCode();
        EnsureNoBridgeError(text);
    }

    public async Task<byte[]> DownloadFlashAsync(int flashSize, IProgress<OperationProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        progress?.Report(new("Reading flash", 0, $"Downloading {flashSize:N0} bytes"));
        var bytes = await _http.GetByteArrayAsync($"download_flash?offset=0&len={flashSize:X}", cancellationToken);
        if (bytes.Length != flashSize) throw new IOException($"Expected {flashSize} flash bytes, received {bytes.Length}.");
        progress?.Report(new("Reading flash", 100, "Flash download complete"));
        return bytes;
    }

    public async Task UploadRecoveryImageAsync(FirmwarePackage package, IProgress<OperationProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        var blank = Enumerable.Repeat((byte)0xFF, package.Manifest.FlashSize).ToArray();
        var image = IntelHexImage.Parse(await File.ReadAllTextAsync(package.HexPath, cancellationToken));
        var expected = image.ApplyTo(blank);
        var pages = package.Manifest.AllowedRanges.SelectMany(range => Enumerable.Range((int)(range.Start / 0x400),
                (int)((range.Length + 0x3FF) / 0x400)))
            .Distinct().Order().ToArray();
        for (var index = 0; index < pages.Length; index++)
        {
            var address = pages[index] * 0x400;
            progress?.Report(new("Uploading recovery image", 35 + index * 25d / pages.Length, $"Page 0x{address:X5}"));
            await UploadBytesAsync(expected.AsMemory(address, 0x400), (uint)address, cancellationToken);
        }
    }

    public async Task<byte[]> DumpUicrAsync(IProgress<OperationProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        var text = await GetTextAsync("set_glitcher?state=dump_full_uicr", HttpMethod.Post, cancellationToken);
        EnsureNoBridgeError(text);
        await WaitForIdleAsync(progress, cancellationToken);
        var bytes = await _http.GetByteArrayAsync("full_uicr.bin", cancellationToken);
        if (bytes.Length != 0x1000) throw new IOException($"Expected 4096 UICR bytes, received {bytes.Length}.");
        return bytes;
    }

    public async Task WaitForIdleAsync(IProgress<OperationProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        for (var attempt = 0; attempt < 240; attempt++)
        {
            var state = await GetStateAsync(cancellationToken);
            var match = ProgressRegex().Match(state);
            if (match.Success)
            {
                progress?.Report(new("ESP32 task", double.Parse(match.Groups[1].Value), state));
            }
            else if (state.Contains("no task running", StringComparison.OrdinalIgnoreCase))
            {
                return;
            }
            await Task.Delay(250, cancellationToken);
        }
        throw new TimeoutException("ESP32 task did not finish within 60 seconds.");
    }

    public async Task ResetAndRunAsync(CancellationToken cancellationToken = default)
    {
        await GetTextAsync("set_swd?cmd=set_reset", HttpMethod.Post, cancellationToken);
        await GetTextAsync("set_swd?cmd=write_register&address=E000EDF0&value=A05F0000", HttpMethod.Post, cancellationToken);
        var dhcsr = await ReadRegisterAsync(0xE000EDF0, cancellationToken);
        if ((dhcsr & 0x00020000u) != 0) throw new InvalidOperationException("Target CPU remained halted after reset.");
    }

    private async Task<string> GetTextAsync(string relativeUri, HttpMethod method, CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(method, relativeUri);
        using var response = await _http.SendAsync(request, cancellationToken);
        var text = await response.Content.ReadAsStringAsync(cancellationToken);
        response.EnsureSuccessStatusCode();
        EnsureNoBridgeError(text);
        return text;
    }

    private static void EnsureNoBridgeError(string text)
    {
        if (text.Contains("ERROR", StringComparison.OrdinalIgnoreCase) ||
            text.Contains("Wrong parameter", StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException(text.Trim());
    }

    private static uint ParseHexValue(string text, string label)
    {
        EnsureNoBridgeError(text);
        var matches = HexRegex().Matches(text);
        if (matches.Count == 0) throw new InvalidDataException($"Bridge returned an unexpected {label} response: {text}");
        return Convert.ToUInt32(matches[^1].Groups[1].Value, 16);
    }

    public void Dispose() => _http.Dispose();

    [GeneratedRegex(@"0x([0-9a-fA-F]{1,8})")]
    private static partial Regex HexRegex();

    [GeneratedRegex(@"Flash state\s+(\d+)%", RegexOptions.IgnoreCase)]
    private static partial Regex ProgressRegex();
}
