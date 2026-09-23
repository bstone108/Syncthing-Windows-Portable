namespace PortableSyncthing.Core;

public sealed class PortablePathException : Exception
{
    public PortablePathException(string message) : base(message) { }
}

public sealed record PortableRoot(string RootDirectory)
{
    public string DataDirectory => Combine(RootDirectory, "data");
    public string SyncthingHomeDirectory => Combine(DataDirectory, "syncthing");
    public string SyncthingExecutablePath => Combine(Combine(RootDirectory, "bin"), "current", "syncthing.exe");
    public string FolderMappingsPath => Combine(DataDirectory, "portable-folders.json");
    public string LogsDirectory => Combine(DataDirectory, "logs");

    public static PortableRoot FromExecutablePath(string executablePath)
    {
        if (string.IsNullOrWhiteSpace(executablePath))
            throw new PortablePathException("The application executable path is required.");

        var normalized = Normalize(executablePath);
        var separator = normalized.LastIndexOf('\\');
        if (separator < 2 || normalized[1] != ':')
            throw new PortablePathException("The application must run from an absolute Windows path.");

        return new PortableRoot(normalized[..separator]);
    }

    public string ResolvePortableRelative(string relativePath)
    {
        if (string.IsNullOrWhiteSpace(relativePath))
            throw new PortablePathException("A portable folder mapping cannot be empty.");

        var normalized = Normalize(relativePath);
        if (normalized.Length >= 2 && normalized[1] == ':' || normalized.StartsWith("\\"))
            throw new PortablePathException("Folder mappings must be relative to the portable application root.");

        var segments = normalized.Split('\\', StringSplitOptions.RemoveEmptyEntries);
        if (segments.Length == 0 || segments.Any(segment => segment is "." or ".." || segment.Contains(':')))
            throw new PortablePathException("Folder mappings must not escape the portable application root.");

        return Combine(RootDirectory, segments);
    }

    public static string Combine(string root, params string[] segments)
    {
        var all = new[] { Normalize(root).TrimEnd('\\') }
            .Concat(segments.Select(Normalize).Select(x => x.Trim('\\')));
        return string.Join("\\", all);
    }

    public static string Normalize(string path) => path.Trim().Replace('/', '\\');
}
