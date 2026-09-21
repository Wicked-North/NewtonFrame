using Microsoft.Win32;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.IO.Ports;
using System.Text.RegularExpressions;
using System.Text;

namespace RoggenCore.Core;

public sealed class DiscoveryService
{
    public IReadOnlyList<string> FindSerialPorts()
    {
        var ports = new SortedSet<string>(StringComparer.OrdinalIgnoreCase);
        if (!OperatingSystem.IsWindows()) return [];
        using var key = Registry.LocalMachine.OpenSubKey(@"HARDWARE\DEVICEMAP\SERIALCOMM");
        if (key is null) return [];
        foreach (var name in key.GetValueNames())
            if (key.GetValue(name) is string port) ports.Add(port);
        return ports.ToList();
    }

    public async Task<IReadOnlyList<BridgeTarget>> DiscoverAsync(string? savedAddress,
        IProgress<OperationProgress>? progress = null, CancellationToken cancellationToken = default)
    {
        var candidates = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var serialTargets = await DiscoverSerialAsync(cancellationToken);
        foreach (var target in serialTargets) AddCandidate(candidates, target.Address);
        AddCandidate(candidates, savedAddress);
        AddCandidate(candidates, "http://swd.local");
        AddCandidate(candidates, "http://192.168.4.1");

        foreach (var network in NetworkInterface.GetAllNetworkInterfaces().Where(n => n.OperationalStatus == OperationalStatus.Up))
        foreach (var item in network.GetIPProperties().UnicastAddresses)
        {
            if (item.Address.AddressFamily != AddressFamily.InterNetwork || IPAddress.IsLoopback(item.Address)) continue;
            var bytes = item.Address.GetAddressBytes();
            if (bytes[0] == 169 && bytes[1] == 254) continue;
            for (var host = 1; host < 255; host++)
                candidates.Add($"http://{bytes[0]}.{bytes[1]}.{bytes[2]}.{host}");
        }

        progress?.Report(new("Discovery", 0, $"Checking {candidates.Count} possible bridge addresses"));
        using var gate = new SemaphoreSlim(32);
        var found = new List<BridgeTarget>(serialTargets);
        var sync = new object();
        var completed = 0;
        await Task.WhenAll(candidates.Select(async address =>
        {
            await gate.WaitAsync(cancellationToken);
            try
            {
                using var client = new HttpClient { BaseAddress = new Uri(address), Timeout = TimeSpan.FromMilliseconds(700) };
                var response = await client.GetStringAsync("get_state?cmd=0", cancellationToken);
                if (response.Contains(";info;") || response.Contains("no task running", StringComparison.OrdinalIgnoreCase))
                {
                    var source = address.Contains("swd.local") ? "mDNS" : address.EndsWith("192.168.4.1") ? "ESP32 access point" : "LAN";
                    lock (sync) found.Add(new BridgeTarget(address, source));
                }
            }
            catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException) { }
            finally
            {
                gate.Release();
                var done = Interlocked.Increment(ref completed);
                progress?.Report(new("Discovery", done * 100d / candidates.Count, $"Checked {done} of {candidates.Count}"));
            }
        }));
        foreach (var target in serialTargets)
        {
            var match = found.FindIndex(item => item.Address.Equals(target.Address, StringComparison.OrdinalIgnoreCase));
            if (match >= 0) found[match] = target;
        }
        return found.DistinctBy(item => item.Address).OrderBy(item => item.Address).ToList();
    }

    public async Task<IReadOnlyList<BridgeTarget>> DiscoverSerialAsync(CancellationToken cancellationToken = default)
    {
        if (!OperatingSystem.IsWindows()) return [];
        var found = new List<BridgeTarget>();
        foreach (var portName in FindSerialPorts())
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                using var port = new SerialPort(portName, 115200)
                {
                    DtrEnable = false,
                    RtsEnable = false,
                    ReadTimeout = 600,
                    WriteTimeout = 600,
                    NewLine = "\n"
                };
                port.Open();
                port.DiscardInBuffer();
                port.WriteLine("ROGGENCORE?");
                var deadline = DateTime.UtcNow.AddMilliseconds(900);
                while (DateTime.UtcNow < deadline)
                {
                    var line = await Task.Run(port.ReadLine, cancellationToken);
                    var match = Regex.Match(line, @"ROGGENCORE_BRIDGE;1;(https?://[^\s;]+)", RegexOptions.IgnoreCase);
                    if (!match.Success) continue;
                    found.Add(new BridgeTarget(match.Groups[1].Value.TrimEnd('/'), "USB serial", portName));
                    break;
                }
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException or TimeoutException) { }
        }
        return found;
    }

    public async Task ConfigureWifiAsync(string portName, string ssid, string password,
        CancellationToken cancellationToken = default)
    {
        if (!OperatingSystem.IsWindows()) throw new PlatformNotSupportedException();
        if (string.IsNullOrWhiteSpace(ssid)) throw new ArgumentException("Wi-Fi network name is required.", nameof(ssid));
        using var port = new SerialPort(portName, 115200)
        {
            DtrEnable = false,
            RtsEnable = false,
            ReadTimeout = 1500,
            WriteTimeout = 1000,
            NewLine = "\n"
        };
        port.Open();
        port.DiscardInBuffer();
        var encodedSsid = Convert.ToBase64String(Encoding.UTF8.GetBytes(ssid));
        var encodedPassword = Convert.ToBase64String(Encoding.UTF8.GetBytes(password));
        port.WriteLine($"ROGGENCORE_WIFI;{encodedSsid};{encodedPassword}");
        var response = await Task.Run(port.ReadLine, cancellationToken);
        if (!response.Contains("ROGGENCORE_WIFI_SAVED", StringComparison.Ordinal))
            throw new IOException($"Bridge rejected Wi-Fi configuration: {response.Trim()}");
    }

    private static void AddCandidate(HashSet<string> candidates, string? address)
    {
        if (string.IsNullOrWhiteSpace(address)) return;
        if (!address.StartsWith("http://", StringComparison.OrdinalIgnoreCase) &&
            !address.StartsWith("https://", StringComparison.OrdinalIgnoreCase)) address = "http://" + address;
        candidates.Add(address.TrimEnd('/'));
    }
}
