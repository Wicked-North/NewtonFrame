using Microsoft.Win32;
using RoggenCore.Core;
using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Threading;

namespace RoggenCore.Manager;

public partial class MainWindow : Window
{
    private readonly AppStorage _storage = new();
    private readonly DiscoveryService _discovery = new();
    private readonly DeviceManager _manager;
    private Esp32BridgeClient? _bridge;
    private DeviceInfo? _device;
    private FirmwarePackage? _package;
    private readonly DispatcherTimer _usbTimer = new() { Interval = TimeSpan.FromSeconds(2) };
    private string _lastPorts = "";
    private bool _checkingUsb;
    private string? _bridgeSerialPort;

    public MainWindow()
    {
        _manager = new DeviceManager(_storage);
        InitializeComponent();
    }

    private async void Window_Loaded(object sender, RoutedEventArgs e)
    {
        var state = await _storage.LoadStateAsync();
        if (!string.IsNullOrWhiteSpace(state.LastBridgeAddress)) AddressBox.Text = state.LastBridgeAddress;
        PortsText.Text = FormatPorts();
        _lastPorts = PortsText.Text;
        _usbTimer.Tick += UsbTimer_Tick;
        _usbTimer.Start();
        await DetectUsbBridgeAsync();
        var legacy = @"D:\NewtonFrame-Backups";
        if (Directory.Exists(legacy)) await _storage.ImportLegacyBackupsAsync(legacy);
        await RefreshBackupsAsync();
    }

    private async void UsbTimer_Tick(object? sender, EventArgs e)
    {
        if (_checkingUsb) return;
        var ports = FormatPorts();
        if (ports == _lastPorts) return;
        _lastPorts = ports;
        PortsText.Text = ports;
        _checkingUsb = true;
        try
        { await DetectUsbBridgeAsync(); }
        finally { _checkingUsb = false; }
    }

    private async Task DetectUsbBridgeAsync()
    {
        var targets = await _discovery.DiscoverSerialAsync();
        if (targets.Count == 0) return;
        AddressBox.Text = targets[0].Address;
        _bridgeSerialPort = targets[0].SerialPort;
        ConnectionBadge.Text = $"ESP32 detected on {targets[0].SerialPort}";
        StatusText.Text = targets[0].Address.EndsWith("192.168.4.1")
            ? "Bridge found by USB. Join SWD-Photo or save your Wi-Fi details below."
            : "Bridge found by USB; ready to inspect.";
    }

    private IProgress<OperationProgress> UiProgress() => new Progress<OperationProgress>(value =>
    {
        StatusText.Text = $"{value.Stage}: {value.Detail}";
        Progress.Value = value.Percent;
    });

    private async void Discover_Click(object sender, RoutedEventArgs e) => await RunAsync(async () =>
    {
        PortsText.Text = FormatPorts();
        var found = await _discovery.DiscoverAsync(AddressBox.Text, UiProgress());
        if (found.Count == 0) throw new InvalidOperationException("No RoggenCore ESP32 bridge was found. Check USB power and Wi-Fi, or enter its IP address.");
        AddressBox.Text = found[0].Address;
        _bridgeSerialPort = found[0].SerialPort;
        StatusText.Text = $"Found {found.Count} bridge{(found.Count == 1 ? "" : "s")}; selected {found[0].Address}";
    });

    private async void Connect_Click(object sender, RoutedEventArgs e) => await RunAsync(ConnectAsync);

    private async void ConfigureWifi_Click(object sender, RoutedEventArgs e) => await RunAsync(async () =>
    {
        if (string.IsNullOrWhiteSpace(_bridgeSerialPort))
        {
            var targets = await _discovery.DiscoverSerialAsync();
            _bridgeSerialPort = targets.FirstOrDefault()?.SerialPort;
        }
        if (string.IsNullOrWhiteSpace(_bridgeSerialPort))
            throw new InvalidOperationException("No RoggenCore bridge responded over USB serial.");
        await _discovery.ConfigureWifiAsync(_bridgeSerialPort, WifiSsidBox.Text, WifiPasswordBox.Password);
        WifiPasswordBox.Clear();
        StatusText.Text = "Wi-Fi saved. The bridge is restarting; press Discover when it rejoins.";
    });

    private async Task ConnectAsync()
    {
        _bridge?.Dispose();
        var address = AddressBox.Text.Trim();
        if (!address.StartsWith("http", StringComparison.OrdinalIgnoreCase)) address = "http://" + address;
        _bridge = new Esp32BridgeClient(new Uri(address.TrimEnd('/') + "/"));
        _device = await _manager.ProbeAsync(_bridge);
        TargetText.Text = $"nRF52811 · {(_device.IsUnlocked ? "unlocked" : "locked")} · {_device.FlashSize / 1024} KiB";
        DisplayText.Text = _device.DisplayWidth > 0 ? $"{_device.DisplayModel} · {_device.DisplayWidth} × {_device.DisplayHeight}" : _device.DisplayModel;
        IdentityText.Text = $"{_device.DeviceId:X16}  UICR {_device.BoardWord:X8}";
        ConnectionBadge.Text = "ESP32 bridge + tag connected";
        ConfirmationHint.Text = $"Type {RecoveryConfirmation.RequiredPhrase(_device.DeviceId)} to permit recovery erase:";
        InstallButton.IsEnabled = _package is not null && _device.IsUnlocked;
        StatusText.Text = "Target inspection complete";
    }

    private async void Diagnostics_Click(object sender, RoutedEventArgs e) => await RunAsync(async () =>
    {
        EnsureConnected();
        var results = await _manager.RunDiagnosticsAsync(_bridge!, _device!, UiProgress());
        DiagnosticsText.Text = string.Join(Environment.NewLine, results.Select(item => $"{(item.Passed ? "PASS" : "FAIL"),-4}  {item.Name,-24} {item.Detail}"));
        StatusText.Text = results.All(item => item.Passed) ? "All post-unlock tests passed" : "One or more diagnostics failed";
    });

    private async void Backup_Click(object sender, RoutedEventArgs e) => await RunAsync(async () =>
    {
        EnsureConnected();
        var record = await _manager.BackupAsync(_bridge!, _device!, "Installed firmware", "Unknown", true, UiProgress());
        await RefreshBackupsAsync();
        StatusText.Text = $"Verified backup {record.Id} saved";
    });

    private async void ChoosePackage_Click(object sender, RoutedEventArgs e) => await RunAsync(async () =>
    {
        var dialog = new OpenFileDialog { Filter = "RoggenCore firmware manifest|*.json", Title = "Choose a verified RoggenCore firmware package" };
        if (dialog.ShowDialog(this) != true) return;
        _package = await FirmwarePackageLoader.LoadAsync(dialog.FileName);
        PackageText.Text = $"{_package.Manifest.Name} {_package.Manifest.Version} · CRC {_package.Manifest.HexCrc32:X8}";
        InstallButton.IsEnabled = _device?.IsUnlocked == true;
        StatusText.Text = "Firmware package integrity verified";
    });

    private async void Install_Click(object sender, RoutedEventArgs e) => await RunAsync(async () =>
    {
        EnsureConnected();
        if (_package is null) throw new InvalidOperationException("Choose a firmware package first.");
        var baseline = await _manager.BackupAsync(_bridge!, _device!, "Previous firmware", "Pre-upgrade", true, UiProgress());
        await _manager.InstallAsync(_bridge!, _device!, _package, baseline, UiProgress());
        _device = await _manager.ProbeAsync(_bridge!);
        var installed = await _manager.BackupAsync(_bridge!, _device!, _package.Manifest.Name, _package.Manifest.Version, true, UiProgress());
        await RefreshBackupsAsync();
        MessageBox.Show(this, $"{_package.Manifest.Name} {_package.Manifest.Version} installed and verified.\n\nCRC-32: {installed.FlashCrc32:X8}", "RoggenCore Manager", MessageBoxButton.OK, MessageBoxImage.Information);
    });

    private async void Recover_Click(object sender, RoutedEventArgs e) => await RunAsync(async () =>
    {
        EnsureConnected();
        if (_package is null) throw new InvalidOperationException("Choose a recovery-capable firmware package first.");
        RecoveryConfirmation.Validate(ConfirmationBox.Text, _device!.DeviceId);
        var warning = MessageBox.Show(this, "This will permanently erase factory firmware and protection configuration. Continue only with the correct physical tag connected.", "Final recovery confirmation", MessageBoxButton.YesNo, MessageBoxImage.Warning, MessageBoxResult.No);
        if (warning != MessageBoxResult.Yes) return;
        if (_device.IsUnlocked)
            await _manager.BackupAsync(_bridge!, _device, "Pre-recovery firmware", "Factory/unknown", true, UiProgress());
        await _manager.RecoverAndInstallAsync(_bridge!, _device, _package, ConfirmationBox.Text, UiProgress());
        _device = await _manager.ProbeAsync(_bridge!);
        var results = await _manager.RunDiagnosticsAsync(_bridge!, _device, UiProgress());
        DiagnosticsText.Text = string.Join(Environment.NewLine, results.Select(item => $"{(item.Passed ? "PASS" : "FAIL"),-4}  {item.Name,-24} {item.Detail}"));
        if (results.Any(item => !item.Passed)) throw new InvalidOperationException("Recovery flashed successfully, but post-unlock diagnostics failed.");
        await _manager.BackupAsync(_bridge!, _device, _package.Manifest.Name, _package.Manifest.Version, true, UiProgress());
        await RefreshBackupsAsync();
    });

    private void OpenBackups_Click(object sender, RoutedEventArgs e) =>
        Process.Start(new ProcessStartInfo(_storage.BackupsDirectory) { UseShellExecute = true });

    private async void ImportBackups_Click(object sender, RoutedEventArgs e) => await RunAsync(async () =>
    {
        var dialog = new OpenFolderDialog { Title = "Choose a folder containing full 192 KiB flash backups" };
        if (dialog.ShowDialog(this) != true) return;
        var imported = await _storage.ImportLegacyBackupsAsync(dialog.FolderName);
        await RefreshBackupsAsync();
        StatusText.Text = $"Imported {imported} new verified backup{(imported == 1 ? "" : "s")}";
    });

    private async void RestoreBackup_Click(object sender, RoutedEventArgs e) => await RunAsync(async () =>
    {
        EnsureConnected();
        if (BackupsGrid.SelectedItem is not BackupRecord backup) throw new InvalidOperationException("Select a backup first.");
        var answer = MessageBox.Show(this, $"Restore {backup.Id}? All target flash will be replaced after CRC/SHA verification; UICR will be preserved.", "Confirm verified rollback", MessageBoxButton.YesNo, MessageBoxImage.Warning, MessageBoxResult.No);
        if (answer != MessageBoxResult.Yes) return;
        await _manager.RestoreBackupAsync(_bridge!, _device!, backup, UiProgress());
        StatusText.Text = $"Verified rollback complete: {backup.Id}";
    });

    private async Task RefreshBackupsAsync() => BackupsGrid.ItemsSource = await _storage.ListBackupsAsync();

    private string FormatPorts()
    {
        var ports = _discovery.FindSerialPorts();
        return ports.Count == 0 ? "None detected" : string.Join(", ", ports);
    }

    private void EnsureConnected()
    {
        if (_bridge is null || _device is null) throw new InvalidOperationException("Connect and inspect the target first.");
    }

    private async Task RunAsync(Func<Task> operation)
    {
        IsEnabled = false;
        try { await operation(); }
        catch (Exception ex)
        {
            StatusText.Text = ex.Message;
            MessageBox.Show(this, ex.Message, "RoggenCore Manager", MessageBoxButton.OK, MessageBoxImage.Error);
        }
        finally { IsEnabled = true; }
    }
}
