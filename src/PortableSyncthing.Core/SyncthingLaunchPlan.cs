namespace PortableSyncthing.Core;

public sealed record SyncthingLaunchPlan(string ExecutablePath, IReadOnlyList<string> Arguments)
{
    public static SyncthingLaunchPlan Create(PortableRoot portableRoot, int guiPort) => new(
        portableRoot.SyncthingExecutablePath,
        new[]
        {
            "serve",
            $"--home={portableRoot.SyncthingHomeDirectory}",
            $"--gui-address=127.0.0.1:{guiPort}",
            "--no-browser",
            "--no-upgrade",
            $"--log-file={PortableRoot.Combine(portableRoot.LogsDirectory, "syncthing.log")}",
            "--log-max-old-files=3",
            "--log-max-size=10485760"
        });
}
