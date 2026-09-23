using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Windows;
using Microsoft.Web.WebView2.Wpf;
using PortableSyncthing.Core;

namespace PortableSyncthing.Windows;

public partial class MainWindow : Window
{
    private readonly SyncthingProcessManager _process = new();
    private readonly SyncthingConfigurationService _configuration = new();
    private readonly SyncthingReleaseUpdater _updater = new();
    private PortableRoot? _root;
    private string _apiKey = string.Empty;
    private readonly Uri _guiUri = new("http://127.0.0.1:8384");

    public MainWindow()
    {
        InitializeComponent();
        _process.LogReceived += (_, line) => Dispatcher.Invoke(() => AddLog(line));
    }

    private async void Window_Loaded(object sender, RoutedEventArgs e)
    {
        try
        {
            _root = PortableRoot.FromExecutablePath(Environment.ProcessPath ?? throw new InvalidOperationException("Unable to resolve application location."));
            Directory.CreateDirectory(_root.DataDirectory);
            Directory.CreateDirectory(_root.LogsDirectory);
            SyncthingBrowser.CreationProperties = new CoreWebView2CreationProperties
            {
                UserDataFolder = Path.Combine(_root.DataDirectory, "webview2")
            };
            await SyncthingBrowser.EnsureCoreWebView2Async();
            await StartOrPromptForInstallAsync();
        }
        catch (Exception exception)
        {
            SetStatus("Startup failed: " + exception.Message);
            AddLog(exception.ToString());
        }
    }

    private async Task StartOrPromptForInstallAsync()
    {
        if (_root is null) return;
        if (!File.Exists(_root.SyncthingExecutablePath))
        {
            SetStatus("Downloading and verifying the official Syncthing runtime for this portable workspace…");
            var version = await _updater.InstallLatestAsync(_root, CancellationToken.None);
            AddLog($"Installed verified official Syncthing {version}.");
        }

        var configPath = Path.Combine(_root.SyncthingHomeDirectory, "config.xml");
        if (!File.Exists(configPath))
        {
            SetStatus("Generating first portable Syncthing configuration…");
            await _process.GenerateConfigurationAsync(_root, CancellationToken.None);
        }

        _apiKey = _configuration.PrepareExistingConfiguration(_root);
        _process.Start(SyncthingLaunchPlan.Create(_root, 8384));
        SetStatus("Starting Syncthing with portable configuration and remapped folders…");
        await WaitForHealthAsync();
        SyncthingBrowser.Source = _guiUri;
        ProcessStatus.Text = "Syncthing is running. Closing this window stops the managed process.";
        SetStatus("Syncthing running — all portable folder mappings were reconciled before launch.");
    }

    private async Task WaitForHealthAsync()
    {
        using var client = new HttpClient { Timeout = TimeSpan.FromSeconds(3) };
        if (!string.IsNullOrWhiteSpace(_apiKey)) client.DefaultRequestHeaders.Add("X-API-Key", _apiKey);
        for (var attempt = 0; attempt < 25; attempt++)
        {
            try
            {
                using var response = await client.GetAsync(new Uri(_guiUri, "/rest/system/status"));
                if (response.IsSuccessStatusCode) return;
            }
            catch (HttpRequestException) { }
            await Task.Delay(TimeSpan.FromSeconds(1));
        }
        throw new InvalidOperationException("Syncthing did not become healthy within 25 seconds.");
    }

    private async void Restart_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            if (_root is null) return;
            SetStatus("Stopping Syncthing…");
            await _process.StopAsync(_guiUri, _apiKey, CancellationToken.None);
            await StartOrPromptForInstallAsync();
        }
        catch (Exception exception) { SetStatus("Restart failed: " + exception.Message); AddLog(exception.ToString()); }
    }

    private async void Update_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            if (_root is null) return;
            SetStatus("Checking the official Syncthing release and verifying checksum…");
            await _process.StopAsync(_guiUri, _apiKey, CancellationToken.None);
            var version = await _updater.InstallLatestAsync(_root, CancellationToken.None);
            AddLog($"Installed verified official Syncthing {version}.");
            await StartOrPromptForInstallAsync();
        }
        catch (Exception exception) { SetStatus("Update failed without replacing the active binary: " + exception.Message); AddLog(exception.ToString()); }
    }

    private void OpenDataFolder_Click(object sender, RoutedEventArgs e)
    {
        if (_root is not null)
            Process.Start(new ProcessStartInfo { FileName = _root.DataDirectory, UseShellExecute = true });
    }

    private void Window_Closing(object? sender, CancelEventArgs e)
    {
        try { Task.Run(() => _process.StopAsync(_guiUri, _apiKey, CancellationToken.None)).GetAwaiter().GetResult(); }
        catch (Exception exception) { AddLog("Shutdown warning: " + exception.Message); }
        finally { _process.Dispose(); }
    }

    private void SetStatus(string status) => StatusText.Text = status;

    private void AddLog(string line)
    {
        LogList.Items.Add($"{DateTimeOffset.Now:HH:mm:ss}  {line}");
        while (LogList.Items.Count > 600) LogList.Items.RemoveAt(0);
        LogList.ScrollIntoView(LogList.Items[^1]);
    }
}
