using System.Diagnostics;
using System.Net.Http.Headers;

namespace PortableSyncthing.Core;

public sealed class SyncthingProcessManager : IDisposable
{
    private Process? _process;

    public event EventHandler<string>? LogReceived;
    public bool IsRunning => _process is { HasExited: false };

    public async Task GenerateConfigurationAsync(PortableRoot root, CancellationToken cancellationToken)
    {
        Directory.CreateDirectory(root.SyncthingHomeDirectory);
        await RunToExitAsync(root.SyncthingExecutablePath,
            new[] { "generate", $"--home={root.SyncthingHomeDirectory}", "--no-port-probing" }, cancellationToken);
    }

    public void Start(SyncthingLaunchPlan plan)
    {
        if (IsRunning) return;
        if (!File.Exists(plan.ExecutablePath))
            throw new FileNotFoundException("Syncthing is not installed. Use Check for Updates to download it.", plan.ExecutablePath);

        var startInfo = new ProcessStartInfo(plan.ExecutablePath)
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            WorkingDirectory = Path.GetDirectoryName(plan.ExecutablePath)!
        };
        foreach (var argument in plan.Arguments) startInfo.ArgumentList.Add(argument);

        _process = new Process { StartInfo = startInfo, EnableRaisingEvents = true };
        _process.OutputDataReceived += (_, e) => Publish(e.Data);
        _process.ErrorDataReceived += (_, e) => Publish(e.Data);
        _process.Exited += (_, _) => Publish("Syncthing process exited.");
        _process.Start();
        _process.BeginOutputReadLine();
        _process.BeginErrorReadLine();
    }

    public async Task StopAsync(Uri guiBaseUri, string apiKey, CancellationToken cancellationToken)
    {
        if (!IsRunning) return;
        try
        {
            using var client = new HttpClient { BaseAddress = guiBaseUri, Timeout = TimeSpan.FromSeconds(5) };
            if (!string.IsNullOrWhiteSpace(apiKey)) client.DefaultRequestHeaders.Add("X-API-Key", apiKey);
            using var request = new HttpRequestMessage(HttpMethod.Post, "/rest/system/shutdown");
            using var response = await client.SendAsync(request, cancellationToken);
            response.EnsureSuccessStatusCode();
            await _process!.WaitForExitAsync(cancellationToken).WaitAsync(TimeSpan.FromSeconds(12), cancellationToken);
        }
        catch (Exception) when (IsRunning)
        {
            _process!.Kill(entireProcessTree: true);
            await _process.WaitForExitAsync(cancellationToken);
        }
    }

    public void Dispose() => _process?.Dispose();

    private async Task RunToExitAsync(string executable, IEnumerable<string> arguments, CancellationToken cancellationToken)
    {
        var info = new ProcessStartInfo(executable) { UseShellExecute = false, CreateNoWindow = true };
        foreach (var argument in arguments) info.ArgumentList.Add(argument);
        using var process = Process.Start(info) ?? throw new InvalidOperationException("Could not start Syncthing.");
        await process.WaitForExitAsync(cancellationToken);
        if (process.ExitCode != 0) throw new InvalidOperationException($"Syncthing configuration generation failed with exit code {process.ExitCode}.");
    }

    private void Publish(string? line)
    {
        if (!string.IsNullOrWhiteSpace(line)) LogReceived?.Invoke(this, line);
    }
}
