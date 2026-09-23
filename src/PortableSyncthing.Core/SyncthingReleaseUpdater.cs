using System.IO.Compression;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text.RegularExpressions;

namespace PortableSyncthing.Core;

public sealed class SyncthingReleaseUpdater
{
    private const string LatestReleaseUri = "https://api.github.com/repos/syncthing/syncthing/releases/latest";

    public async Task<string> InstallLatestAsync(PortableRoot root, CancellationToken cancellationToken)
    {
        using var client = new HttpClient();
        client.DefaultRequestHeaders.UserAgent.ParseAdd("PortableSyncthing/1.0");
        var release = await client.GetFromJsonAsync<GitHubRelease>(LatestReleaseUri, cancellationToken)
            ?? throw new InvalidOperationException("Official Syncthing release metadata was unavailable.");
        var architecture = Environment.Is64BitOperatingSystem ? "amd64" : "386";
        var archive = release.assets.FirstOrDefault(asset => Regex.IsMatch(asset.name, $"^syncthing-windows-{architecture}-.*\\.zip$", RegexOptions.IgnoreCase))
            ?? throw new InvalidOperationException("No matching official Windows Syncthing archive was found.");
        var checksums = release.assets.FirstOrDefault(asset => asset.name.Contains("sha256", StringComparison.OrdinalIgnoreCase))
            ?? throw new InvalidOperationException("Release has no SHA-256 checksum asset; refusing an unverified update.");

        var staging = Path.Combine(root.DataDirectory, "updates", release.tag_name);
        Directory.CreateDirectory(staging);
        var archivePath = Path.Combine(staging, archive.name);
        await File.WriteAllBytesAsync(archivePath, await client.GetByteArrayAsync(archive.browser_download_url, cancellationToken), cancellationToken);
        var expectedLine = (await client.GetStringAsync(checksums.browser_download_url, cancellationToken))
            .Split('\n').FirstOrDefault(line => line.Contains(archive.name, StringComparison.Ordinal));
        var expectedHash = expectedLine?.Split(' ', StringSplitOptions.RemoveEmptyEntries).FirstOrDefault();
        var actualHash = Convert.ToHexString(SHA256.HashData(await File.ReadAllBytesAsync(archivePath, cancellationToken)));
        if (expectedHash is null || !actualHash.Equals(expectedHash, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("Syncthing download checksum verification failed.");

        var unpacked = Path.Combine(staging, "unpacked");
        if (Directory.Exists(unpacked)) Directory.Delete(unpacked, true);
        ZipFile.ExtractToDirectory(archivePath, unpacked);
        var executable = Directory.EnumerateFiles(unpacked, "syncthing.exe", SearchOption.AllDirectories).SingleOrDefault()
            ?? throw new InvalidOperationException("Verified archive did not contain syncthing.exe.");

        var current = Path.GetDirectoryName(root.SyncthingExecutablePath)!;
        var previous = Path.Combine(root.RootDirectory, "bin", "previous");
        Directory.CreateDirectory(current);
        Directory.CreateDirectory(previous);
        if (File.Exists(root.SyncthingExecutablePath)) File.Copy(root.SyncthingExecutablePath, Path.Combine(previous, "syncthing.exe"), true);
        File.Copy(executable, root.SyncthingExecutablePath, true);
        return release.tag_name;
    }

    private sealed record GitHubRelease(string tag_name, GitHubAsset[] assets);
    private sealed record GitHubAsset(string name, string browser_download_url);
}
