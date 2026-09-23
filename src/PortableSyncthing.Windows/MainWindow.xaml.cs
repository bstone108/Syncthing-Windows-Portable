using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Security.AccessControl;
using System.Security.Principal;
using System.Windows;
using Microsoft.Web.WebView2.Core;
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
            await InitializeFixedWebViewAsync(_root);
            await StartOrPromptForInstallAsync();
        }
        catch (Exception exception)
        {
            SetStatus("Startup failed: " + exception.Message);
            AddLog(exception.ToString());
        }
    }

    private async Task InitializeFixedWebViewAsync(PortableRoot root)
    {
        var runtimeDirectory = root.WebView2RuntimeDirectory;
        var browserExecutable = PortableRoot.Combine(runtimeDirectory, "msedgewebview2.exe");
        if (!File.Exists(browserExecutable))
        {
            throw new FileNotFoundException(
                "Fixed Version WebView2 Runtime is missing (" + browserExecutable + "). Evergreen WebView2 is not used. Package the app with scripts/package-windows.ps1 so WebView2Runtime sits next to PortableSyncthing.exe.",
                browserExecutable);
        }

        await TryGrantWebView2AppContainerAccessAsync(runtimeDirectory, browserExecutable);
        var environment = await CoreWebView2Environment.CreateAsync(
            browserExecutableFolder: runtimeDirectory,
            userDataFolder: PortableRoot.Combine(root.DataDirectory, "webview2"));
        await SyncthingBrowser.EnsureCoreWebView2Async(environment);
        AddLog("Using bundled Fixed Version WebView2 runtime.");
    }

    // Windows 10 runs Fixed Version 120+ renderers in an AppContainer. Zip extraction does not
    // keep the ACLs Microsoft requires, so grant them on the local runtime folder when missing.
    private async Task TryGrantWebView2AppContainerAccessAsync(string runtimeDirectory, string browserExecutable)
    {
        try
        {
            if (HasAppContainerReadExecute(browserExecutable)) return;
        }
        catch (Exception exception)
        {
            AddLog("Could not read WebView2 runtime permissions: " + exception.Message);
        }

        foreach (var sid in new[] { "S-1-15-2-2", "S-1-15-2-1" })
        {
            try
            {
                using var process = new Process();
                process.StartInfo.FileName = "icacls.exe";
                process.StartInfo.UseShellExecute = false;
                process.StartInfo.CreateNoWindow = true;
                process.StartInfo.RedirectStandardOutput = true;
                process.StartInfo.RedirectStandardError = true;
                process.StartInfo.ArgumentList.Add(runtimeDirectory);
                process.StartInfo.ArgumentList.Add("/grant");
                process.StartInfo.ArgumentList.Add($"*{sid}:(OI)(CI)(RX)");
                process.StartInfo.ArgumentList.Add("/T");
                process.StartInfo.ArgumentList.Add("/C");
                process.StartInfo.ArgumentList.Add("/Q");
                if (!process.Start())
                {
                    AddLog("Could not start icacls.exe to grant WebView2 runtime access.");
                    return;
                }

                var stdout = process.StandardOutput.ReadToEndAsync();
                var stderr = process.StandardError.ReadToEndAsync();
                using var timeout = new CancellationTokenSource(TimeSpan.FromMinutes(2));
                try
                {
                    await process.WaitForExitAsync(timeout.Token);
                }
                catch (OperationCanceledException)
                {
                    try { process.Kill(entireProcessTree: true); } catch (Exception) { }
                    AddLog("Timed out granting WebView2 runtime folder permissions.");
                    return;
                }

                var error = (await stderr).Trim();
                _ = await stdout;
                if (process.ExitCode != 0)
                    AddLog($"WebView2 runtime permission grant exited {process.ExitCode} for {sid}. {error}".Trim());
            }
            catch (Exception exception)
            {
                AddLog("WebView2 runtime permission grant was not applied: " + exception.Message);
                return;
            }
        }
    }

    private static bool HasAppContainerReadExecute(string path)
    {
        var identities = new[]
        {
            new SecurityIdentifier("S-1-15-2-1"),
            new SecurityIdentifier("S-1-15-2-2")
        };
        var rules = new FileInfo(path).GetAccessControl().GetAccessRules(true, true, typeof(SecurityIdentifier));
        foreach (var identity in identities)
        {
            var allowed = false;
            foreach (FileSystemAccessRule rule in rules)
            {
                if (rule.AccessControlType != AccessControlType.Allow) continue;
                if (!identity.Equals(rule.IdentityReference)) continue;
                if ((rule.FileSystemRights & FileSystemRights.ReadAndExecute) == FileSystemRights.ReadAndExecute)
                {
                    allowed = true;
                    break;
                }
            }
            if (!allowed) return false;
        }
        return true;
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
