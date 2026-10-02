namespace PortableSyncthing.Core;

public sealed class LayoutMigrationException : Exception
{
    public LayoutMigrationException(string message, Exception inner, MigrationReport report)
        : base(message, inner)
    {
        Report = report;
    }

    public MigrationReport Report { get; }
}

public sealed class MigrationReport
{
    private readonly List<string> _lines = new();

    public IReadOnlyList<string> Lines => _lines;
    public int DirectoriesMoved { get; internal set; }
    public int FilesMoved { get; internal set; }
    public int IdenticalRemoved { get; internal set; }
    public int FilesPreserved { get; internal set; }

    internal void Add(string line) => _lines.Add(line);
}

/// <summary>
/// Moves a v2026.9.23.1 layout (data, bin, WebView2Runtime, docs, and publish leftovers
/// beside the executable) into <see cref="SupportFolderName"/> before Syncthing starts.
/// Synced folders are not part of that layout and are left where they are.
/// </summary>
public static class LayoutMigration
{
    public const string SupportFolderName = "support";
    public const string LogFileName = "layout-migration.log";

    private static readonly string[] LegacyFileNames = ["VERSION.txt", "README.txt"];

    public static MigrationReport Migrate(string exeDirectory, bool? relocateLooseNativeLibraries = null)
    {
        if (string.IsNullOrWhiteSpace(exeDirectory))
            throw new PortablePathException("The portable executable directory is required.");

        var fullExe = Path.GetFullPath(exeDirectory);
        if (!Directory.Exists(fullExe))
            throw new PortablePathException($"The portable executable directory does not exist: {fullExe}");

        // Single-file publishes embed WPF and WebView2 native libraries. Loose copies are
        // leftovers and can move into support. A normal build still loads those DLLs from
        // the output directory, so a `dotnet run` session must leave them alone.
        // Assembly.Location is intentionally not used: the single-file analyzer rejects it.
        // A bundled Core assembly is not extracted next to the executable; a normal build copies it there.
        var assemblyName = typeof(LayoutMigration).Assembly.GetName().Name ?? "PortableSyncthing.Core";
        var relocateNatives = relocateLooseNativeLibraries
            ?? !File.Exists(Path.Combine(AppContext.BaseDirectory, assemblyName + ".dll"));

        var report = new MigrationReport();
        string? support = null;
        try
        {
            support = Path.Combine(fullExe, SupportFolderName);
            if (File.Exists(support))
                throw new IOException($"Cannot create '{support}' because a file with that name already exists.");

            Directory.CreateDirectory(support);
            MoveLegacyDirectory(fullExe, support, "data", IsLegacyDataDirectory, report);
            MoveLegacyDirectory(fullExe, support, "bin", IsLegacyBinDirectory, report);
            MoveLegacyDirectory(fullExe, support, "WebView2Runtime", Directory.Exists, report);
            if (relocateNatives)
                MoveLegacyDirectory(fullExe, support, "runtimes", IsLegacyRuntimesDirectory, report);

            foreach (var fileName in LegacyFileNames)
                MoveLegacyFile(fullExe, support, fileName, report);

            if (relocateNatives)
                MoveLoosePublishFiles(fullExe, support, report);

            report.Add(Summary(report));
            TryWriteLog(support, report);
            return report;
        }
        catch (LayoutMigrationException)
        {
            throw;
        }
        catch (Exception exception)
        {
            report.Add("Layout migration failed: " + exception.Message);
            report.Add("Layout migration stopped before Syncthing starts. Items already renamed into support stay there. Items that were not moved are still beside the executable.");
            TryWriteLog(support, report);
            throw new LayoutMigrationException(
                "Layout migration failed before Syncthing starts. Existing files were not replaced, and items that could not be moved are still in their original locations.",
                exception,
                report);
        }
    }

    private static string Summary(MigrationReport report)
    {
        if (report.DirectoriesMoved == 0 && report.FilesMoved == 0 && report.IdenticalRemoved == 0 && report.FilesPreserved == 0)
            return "Layout migration: no legacy files or directories remain beside the executable.";

        return "Layout migration finished. "
            + $"Directories moved: {report.DirectoriesMoved}. "
            + $"Files moved: {report.FilesMoved}. "
            + $"Identical legacy files removed: {report.IdenticalRemoved}. "
            + $"Existing files kept and legacy copies preserved: {report.FilesPreserved}.";
    }

    private static void MoveLegacyDirectory(
        string exeDirectory,
        string support,
        string name,
        Func<string, bool> isLegacy,
        MigrationReport report)
    {
        var source = FindChildDirectory(exeDirectory, name);
        if (source is null)
            return;

        if (!isLegacy(source))
        {
            report.Add($"Layout migration: left '{source}' in place because it is not portable app data.");
            return;
        }

        MoveDirectory(source, Path.Combine(support, name), exeDirectory, support, report);
    }

    private static void MoveLegacyFile(string exeDirectory, string support, string name, MigrationReport report)
    {
        var source = FindChildFile(exeDirectory, name);
        if (source is null)
            return;

        MoveFile(source, Path.Combine(support, name), exeDirectory, support, report);
    }

    private static void MoveLoosePublishFiles(string exeDirectory, string support, MigrationReport report)
    {
        foreach (var source in Directory.EnumerateFiles(exeDirectory).ToList())
        {
            var name = Path.GetFileName(source);
            if (!IsLoosePublishFile(name))
                continue;

            MoveFile(source, Path.Combine(support, name), exeDirectory, support, report);
        }
    }

    private static bool IsLoosePublishFile(string fileName)
    {
        if (fileName.Equals("PortableSyncthing.exe", StringComparison.OrdinalIgnoreCase))
            return false;
        if (fileName.Equals("createdump.exe", StringComparison.OrdinalIgnoreCase))
            return true;
        if (fileName.EndsWith(".deps.json", StringComparison.OrdinalIgnoreCase)
            || fileName.EndsWith(".runtimeconfig.json", StringComparison.OrdinalIgnoreCase))
            return true;

        var extension = Path.GetExtension(fileName);
        return extension.Equals(".dll", StringComparison.OrdinalIgnoreCase)
            || extension.Equals(".pdb", StringComparison.OrdinalIgnoreCase)
            || extension.Equals(".xml", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsLegacyDataDirectory(string path) =>
        Directory.Exists(Path.Combine(path, "syncthing"))
        || File.Exists(Path.Combine(path, "portable-folders.json"))
        || Directory.Exists(Path.Combine(path, "logs"))
        || Directory.Exists(Path.Combine(path, "webview2"))
        || Directory.Exists(Path.Combine(path, "updates"));

    private static bool IsLegacyBinDirectory(string path) =>
        Directory.Exists(Path.Combine(path, "current"))
        || Directory.Exists(Path.Combine(path, "previous"))
        || File.Exists(Path.Combine(path, "syncthing.exe"));

    private static bool IsLegacyRuntimesDirectory(string path) =>
        Directory.Exists(Path.Combine(path, "win-x64"));

    private static void MoveDirectory(string source, string destination, string exeDirectory, string support, MigrationReport report)
    {
        EnsureInside(exeDirectory, source);
        EnsureInside(support, destination);

        if (IsReparsePoint(source))
        {
            if (File.Exists(destination) || Directory.Exists(destination))
                throw new IOException($"Cannot merge reparse point '{source}' into existing '{destination}'.");

            Directory.CreateDirectory(Path.GetDirectoryName(destination) ?? throw new PortablePathException($"'{destination}' has no parent directory."));
            Directory.Move(source, destination);
            report.DirectoriesMoved++;
            report.Add($"Layout migration: moved directory '{source}' to '{destination}'.");
            return;
        }

        if (File.Exists(destination))
            throw new IOException($"Cannot migrate directory '{source}' because '{destination}' is a file.");

        if (!Directory.Exists(destination))
        {
            Directory.CreateDirectory(Path.GetDirectoryName(destination) ?? throw new PortablePathException($"'{destination}' has no parent directory."));
            Directory.Move(source, destination);
            report.DirectoriesMoved++;
            report.Add($"Layout migration: moved directory '{source}' to '{destination}'.");
            return;
        }

        foreach (var file in Directory.EnumerateFiles(source).ToList())
            MoveFile(file, Path.Combine(destination, Path.GetFileName(file)), exeDirectory, support, report);

        foreach (var directory in Directory.EnumerateDirectories(source).ToList())
            MoveDirectory(directory, Path.Combine(destination, Path.GetFileName(directory)), exeDirectory, support, report);

        if (!Directory.EnumerateFileSystemEntries(source).Any())
        {
            Directory.Delete(source);
            report.Add($"Layout migration: removed empty legacy directory '{source}'.");
        }
    }

    private static void MoveFile(string source, string destination, string exeDirectory, string support, MigrationReport report)
    {
        EnsureInside(exeDirectory, source);
        EnsureInside(support, destination);

        if (Directory.Exists(destination))
            throw new IOException($"Cannot migrate '{source}' onto directory '{destination}'.");

        if (!File.Exists(destination))
        {
            Directory.CreateDirectory(Path.GetDirectoryName(destination) ?? throw new PortablePathException($"'{destination}' has no parent directory."));
            File.Move(source, destination);
            report.FilesMoved++;
            report.Add($"Layout migration: moved '{source}' to '{destination}'.");
            return;
        }

        if (FilesAreEqual(source, destination))
        {
            DeleteFile(source);
            report.IdenticalRemoved++;
            report.Add($"Layout migration: removed identical legacy file '{source}'.");
            return;
        }

        var preserved = PreserveConflict(source, exeDirectory, support);
        report.FilesPreserved++;
        report.Add($"Layout migration: kept existing '{destination}' and preserved the legacy file at '{preserved}'.");
    }

    private static string PreserveConflict(string source, string exeDirectory, string support)
    {
        var relative = Path.GetRelativePath(exeDirectory, source);
        if (Path.IsPathRooted(relative) || relative.StartsWith("..", StringComparison.Ordinal) || relative.Contains($"..{Path.DirectorySeparatorChar}", StringComparison.Ordinal))
            throw new IOException($"Refusing to preserve '{source}' because it is outside '{exeDirectory}'.");

        var conflicts = Path.Combine(support, "migration-conflicts");
        var destination = UniquePath(Path.Combine(conflicts, relative));
        EnsureInside(support, destination);
        Directory.CreateDirectory(Path.GetDirectoryName(destination) ?? throw new PortablePathException($"'{destination}' has no parent directory."));
        File.Move(source, destination);
        return destination;
    }

    private static string UniquePath(string path)
    {
        if (!File.Exists(path) && !Directory.Exists(path))
            return path;

        var directory = Path.GetDirectoryName(path) ?? throw new PortablePathException($"'{path}' has no parent directory.");
        var name = Path.GetFileNameWithoutExtension(path);
        var extension = Path.GetExtension(path);
        for (var index = 1; index < 10000; index++)
        {
            var candidate = Path.Combine(directory, $"{name}.{index}{extension}");
            if (!File.Exists(candidate) && !Directory.Exists(candidate))
                return candidate;
        }

        throw new IOException($"Could not find a free path to preserve '{path}'.");
    }

    private static bool FilesAreEqual(string left, string right)
    {
        var leftInfo = new FileInfo(left);
        var rightInfo = new FileInfo(right);
        if (leftInfo.Length != rightInfo.Length)
            return false;

        const int bufferSize = 1024 * 128;
        using var leftStream = new FileStream(left, FileMode.Open, FileAccess.Read, FileShare.Read);
        using var rightStream = new FileStream(right, FileMode.Open, FileAccess.Read, FileShare.Read);
        var leftBuffer = new byte[bufferSize];
        var rightBuffer = new byte[bufferSize];
        while (true)
        {
            var leftRead = ReadFull(leftStream, leftBuffer);
            var rightRead = ReadFull(rightStream, rightBuffer);
            if (leftRead != rightRead)
                return false;
            if (leftRead == 0)
                return true;
            if (!leftBuffer.AsSpan(0, leftRead).SequenceEqual(rightBuffer.AsSpan(0, rightRead)))
                return false;
        }
    }

    private static int ReadFull(Stream stream, byte[] buffer)
    {
        var offset = 0;
        while (offset < buffer.Length)
        {
            var read = stream.Read(buffer, offset, buffer.Length - offset);
            if (read == 0)
                break;
            offset += read;
        }

        return offset;
    }

    private static void DeleteFile(string path)
    {
        var info = new FileInfo(path);
        if (info.IsReadOnly)
            info.IsReadOnly = false;
        info.Delete();
    }

    private static void TryWriteLog(string? support, MigrationReport report)
    {
        try
        {
            if (string.IsNullOrEmpty(support) || !Directory.Exists(support))
                return;

            var path = Path.Combine(support, LogFileName);
            var stamp = DateTimeOffset.Now.ToString("O");
            File.AppendAllLines(path, report.Lines.Select(line => $"{stamp}  {line}"));
        }
        catch (Exception exception)
        {
            report.Add("Layout migration could not write its log: " + exception.Message);
        }
    }

    private static string? FindChildDirectory(string parent, string name)
    {
        foreach (var directory in Directory.EnumerateDirectories(parent))
        {
            if (string.Equals(Path.GetFileName(directory), name, StringComparison.OrdinalIgnoreCase))
                return directory;
        }

        return null;
    }

    private static string? FindChildFile(string parent, string name)
    {
        foreach (var file in Directory.EnumerateFiles(parent))
        {
            if (string.Equals(Path.GetFileName(file), name, StringComparison.OrdinalIgnoreCase))
                return file;
        }

        return null;
    }

    private static bool IsReparsePoint(string path) =>
        (File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0;

    private static void EnsureInside(string root, string path)
    {
        if (!IsStrictlyInside(root, path))
            throw new IOException($"Refusing to change '{path}' because it is outside '{root}'.");
    }

    private static bool IsStrictlyInside(string root, string path)
    {
        var fullRoot = Path.TrimEndingDirectorySeparator(Path.GetFullPath(root));
        var fullPath = Path.TrimEndingDirectorySeparator(Path.GetFullPath(path));
        var prefix = fullRoot + Path.DirectorySeparatorChar;
        return fullPath.StartsWith(prefix, StringComparison.OrdinalIgnoreCase);
    }
}
