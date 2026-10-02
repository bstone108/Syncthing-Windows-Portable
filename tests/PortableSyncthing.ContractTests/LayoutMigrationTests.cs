using PortableSyncthing.Core;

internal static class LayoutMigrationTests
{
    public static void Run(ContractSuite suite)
    {
        suite.Run("fresh install does not move synced folders or non-app data", FreshInstall);
        suite.Run("old layout migration preserves syncthing identity and leaves synced folders", OldLayout);
        suite.Run("partial migration keeps new files and preserves differing legacy copies", PartialMigration);
        suite.Run("migration re-run is idempotent after an old layout move", Rerun);
        suite.Run("migration failure leaves the unmoved file in place", FailureLeavesSource);
        suite.Run("non-single-file session does not relocate native build outputs", NonSingleFileLeavesNatives);
    }

    private static void FreshInstall()
    {
        WithTemp(root =>
        {
            Write(Path.Combine(root, "PortableSyncthing.exe"), "exe");
            Write(Path.Combine(root, "folders", "Photos", "keep.txt"), "synced");
            Write(Path.Combine(root, "data", "Photos", "img.txt"), "synced-data");
            Write(Path.Combine(root, "bin", "tools", "tool.txt"), "synced-bin");

            var report = LayoutMigration.Migrate(root, relocateLooseNativeLibraries: true);

            Assert.Equal(0, report.DirectoriesMoved);
            Assert.Equal(0, report.FilesMoved);
            Assert.Contains("no legacy files or directories remain", string.Join("\n", report.Lines));
            Assert.Contains("left", string.Join("\n", report.Lines));
            Assert.Equal("synced", File.ReadAllText(Path.Combine(root, "folders", "Photos", "keep.txt")));
            Assert.Equal("synced-data", File.ReadAllText(Path.Combine(root, "data", "Photos", "img.txt")));
            Assert.Equal("synced-bin", File.ReadAllText(Path.Combine(root, "bin", "tools", "tool.txt")));
            Assert.Equal("exe", File.ReadAllText(Path.Combine(root, "PortableSyncthing.exe")));
            AssertTopLevel(root, "PortableSyncthing.exe", "bin", "data", "folders", "support");
            Assert.True(File.Exists(Path.Combine(root, "support", LayoutMigration.LogFileName)), "fresh install should write a migration log under support");
            Assert.False(Directory.Exists(Path.Combine(root, "support", "migration-conflicts")), "fresh install should not create conflict copies");
        });
    }

    private static void OldLayout()
    {
        WithTemp(root =>
        {
            Write(Path.Combine(root, "PortableSyncthing.exe"), "exe");
            Write(Path.Combine(root, "data", "syncthing", "cert.pem"), "CERT");
            Write(Path.Combine(root, "data", "syncthing", "key.pem"), "KEY");
            Write(Path.Combine(root, "data", "syncthing", "config.xml"), "<configuration />");
            Write(Path.Combine(root, "data", "syncthing", "index-v0.14.0.db"), "DATABASE");
            Write(Path.Combine(root, "data", "portable-folders.json"), "{\"photos\":\"folders\\\\Photos\"}");
            Write(Path.Combine(root, "data", "logs", "syncthing.log"), "log");
            Write(Path.Combine(root, "bin", "current", "syncthing.exe"), "SYNCTHING");
            Write(Path.Combine(root, "bin", "previous", "syncthing.exe"), "OLD-SYNCTHING");
            Write(Path.Combine(root, "WebView2Runtime", "msedgewebview2.exe"), "WV2");
            Write(Path.Combine(root, "VERSION.txt"), "2026.9.23.1");
            Write(Path.Combine(root, "README.txt"), "readme");
            Write(Path.Combine(root, "WebView2Loader.dll"), "LOADER");
            Write(Path.Combine(root, "PortableSyncthing.pdb"), "PDB");
            Write(Path.Combine(root, "Microsoft.Web.WebView2.Core.xml"), "XML");
            Write(Path.Combine(root, "runtimes", "win-x64", "native", "WebView2Loader.dll"), "NESTED-LOADER");
            Write(Path.Combine(root, "folders", "Photos", "keep.txt"), "synced");
            Write(Path.Combine(root, "notes.txt"), "user");
            Write(Path.Combine(root, "data-photos", "img.txt"), "not-app-data");

            var report = LayoutMigration.Migrate(root, relocateLooseNativeLibraries: true);

            var support = Path.Combine(root, "support");
            Assert.Equal("CERT", File.ReadAllText(Path.Combine(support, "data", "syncthing", "cert.pem")));
            Assert.Equal("KEY", File.ReadAllText(Path.Combine(support, "data", "syncthing", "key.pem")));
            Assert.Equal("<configuration />", File.ReadAllText(Path.Combine(support, "data", "syncthing", "config.xml")));
            Assert.Equal("DATABASE", File.ReadAllText(Path.Combine(support, "data", "syncthing", "index-v0.14.0.db")));
            Assert.Equal("{\"photos\":\"folders\\\\Photos\"}", File.ReadAllText(Path.Combine(support, "data", "portable-folders.json")));
            Assert.Equal("log", File.ReadAllText(Path.Combine(support, "data", "logs", "syncthing.log")));
            Assert.Equal("SYNCTHING", File.ReadAllText(Path.Combine(support, "bin", "current", "syncthing.exe")));
            Assert.Equal("OLD-SYNCTHING", File.ReadAllText(Path.Combine(support, "bin", "previous", "syncthing.exe")));
            Assert.Equal("WV2", File.ReadAllText(Path.Combine(support, "WebView2Runtime", "msedgewebview2.exe")));
            Assert.Equal("2026.9.23.1", File.ReadAllText(Path.Combine(support, "VERSION.txt")));
            Assert.Equal("readme", File.ReadAllText(Path.Combine(support, "README.txt")));
            Assert.Equal("LOADER", File.ReadAllText(Path.Combine(support, "WebView2Loader.dll")));
            Assert.Equal("PDB", File.ReadAllText(Path.Combine(support, "PortableSyncthing.pdb")));
            Assert.Equal("XML", File.ReadAllText(Path.Combine(support, "Microsoft.Web.WebView2.Core.xml")));
            Assert.Equal("NESTED-LOADER", File.ReadAllText(Path.Combine(support, "runtimes", "win-x64", "native", "WebView2Loader.dll")));
            Assert.Equal("synced", File.ReadAllText(Path.Combine(root, "folders", "Photos", "keep.txt")));
            Assert.Equal("user", File.ReadAllText(Path.Combine(root, "notes.txt")));
            Assert.Equal("not-app-data", File.ReadAllText(Path.Combine(root, "data-photos", "img.txt")));
            Assert.Equal("exe", File.ReadAllText(Path.Combine(root, "PortableSyncthing.exe")));
            Assert.False(Directory.Exists(Path.Combine(root, "data")), "legacy data directory should be gone");
            Assert.False(Directory.Exists(Path.Combine(root, "bin")), "legacy bin directory should be gone");
            Assert.False(Directory.Exists(Path.Combine(root, "WebView2Runtime")), "legacy WebView2 runtime should be gone");
            Assert.False(Directory.Exists(Path.Combine(root, "runtimes")), "legacy runtimes directory should be gone");
            Assert.Contains("moved directory", string.Join("\n", report.Lines));
            Assert.True(report.DirectoriesMoved >= 4, "expected data, bin, WebView2Runtime, and runtimes to move");
            AssertTopLevel(root, "PortableSyncthing.exe", "data-photos", "folders", "notes.txt", "support");
        });
    }

    private static void PartialMigration()
    {
        WithTemp(root =>
        {
            Write(Path.Combine(root, "data", "syncthing", "config.xml"), "OLD-CONFIG");
            Write(Path.Combine(root, "data", "syncthing", "cert.pem"), "OLD-CERT");
            Write(Path.Combine(root, "data", "syncthing", "key.pem"), "OLD-KEY");
            Write(Path.Combine(root, "data", "portable-folders.json"), "OLD-MAP");
            Write(Path.Combine(root, "bin", "current", "syncthing.exe"), "SYNCTHING");
            Write(Path.Combine(root, "WebView2Runtime", "msedgewebview2.exe"), "WV2");
            Write(Path.Combine(root, "VERSION.txt"), "2026.9.23.1");
            Write(Path.Combine(root, "WebView2Loader.dll"), "OLD-LOADER");
            Write(Path.Combine(root, "support", "data", "syncthing", "config.xml"), "NEW-CONFIG");
            Write(Path.Combine(root, "support", "WebView2Runtime", "msedgewebview2.exe"), "WV2");
            Write(Path.Combine(root, "support", "VERSION.txt"), "NEW-VERSION");
            Write(Path.Combine(root, "support", "WebView2Loader.dll"), "NEW-LOADER");

            var report = LayoutMigration.Migrate(root, relocateLooseNativeLibraries: true);
            var support = Path.Combine(root, "support");

            Assert.Equal("NEW-CONFIG", File.ReadAllText(Path.Combine(support, "data", "syncthing", "config.xml")));
            Assert.Equal("OLD-CONFIG", File.ReadAllText(Path.Combine(support, "migration-conflicts", "data", "syncthing", "config.xml")));
            Assert.Equal("OLD-CERT", File.ReadAllText(Path.Combine(support, "data", "syncthing", "cert.pem")));
            Assert.Equal("OLD-KEY", File.ReadAllText(Path.Combine(support, "data", "syncthing", "key.pem")));
            Assert.Equal("OLD-MAP", File.ReadAllText(Path.Combine(support, "data", "portable-folders.json")));
            Assert.Equal("SYNCTHING", File.ReadAllText(Path.Combine(support, "bin", "current", "syncthing.exe")));
            Assert.Equal("WV2", File.ReadAllText(Path.Combine(support, "WebView2Runtime", "msedgewebview2.exe")));
            Assert.Equal("NEW-VERSION", File.ReadAllText(Path.Combine(support, "VERSION.txt")));
            Assert.Equal("2026.9.23.1", File.ReadAllText(Path.Combine(support, "migration-conflicts", "VERSION.txt")));
            Assert.Equal("NEW-LOADER", File.ReadAllText(Path.Combine(support, "WebView2Loader.dll")));
            Assert.Equal("OLD-LOADER", File.ReadAllText(Path.Combine(support, "migration-conflicts", "WebView2Loader.dll")));
            Assert.False(Directory.Exists(Path.Combine(root, "data")), "partial legacy data should be merged away");
            Assert.False(Directory.Exists(Path.Combine(root, "bin")), "unmigrated bin should move when support\\bin is absent");
            Assert.False(Directory.Exists(Path.Combine(root, "WebView2Runtime")), "identical runtime files should be removed after compare");
            Assert.False(File.Exists(Path.Combine(root, "VERSION.txt")), "conflicting VERSION.txt should not remain beside the executable");
            Assert.Contains("kept existing", string.Join("\n", report.Lines));
            Assert.True(report.FilesPreserved >= 3, "config, VERSION.txt, and WebView2Loader.dll differ and must be preserved");
            Assert.True(report.IdenticalRemoved >= 1, "identical WebView2 runtime file should be removed rather than duplicated");
        });
    }

    private static void Rerun()
    {
        WithTemp(root =>
        {
            Write(Path.Combine(root, "data", "syncthing", "cert.pem"), "CERT");
            Write(Path.Combine(root, "data", "syncthing", "key.pem"), "KEY");
            Write(Path.Combine(root, "data", "syncthing", "config.xml"), "OLD-CONFIG");
            Write(Path.Combine(root, "data", "syncthing", "index-v0.14.0.db"), "DATABASE");
            Write(Path.Combine(root, "bin", "current", "syncthing.exe"), "SYNCTHING");
            Write(Path.Combine(root, "support", "data", "syncthing", "config.xml"), "NEW-CONFIG");
            Write(Path.Combine(root, "folders", "Photos", "keep.txt"), "synced");

            var first = LayoutMigration.Migrate(root, relocateLooseNativeLibraries: true);
            var certPath = Path.Combine(root, "support", "data", "syncthing", "cert.pem");
            var keyPath = Path.Combine(root, "support", "data", "syncthing", "key.pem");
            var databasePath = Path.Combine(root, "support", "data", "syncthing", "index-v0.14.0.db");
            var conflictPath = Path.Combine(root, "support", "migration-conflicts", "data", "syncthing", "config.xml");
            var cert = File.ReadAllBytes(certPath);
            var key = File.ReadAllBytes(keyPath);
            var database = File.ReadAllBytes(databasePath);
            var conflict = File.ReadAllBytes(conflictPath);

            var second = LayoutMigration.Migrate(root, relocateLooseNativeLibraries: true);

            Assert.True(first.FilesPreserved >= 1, "first run should preserve the differing config");
            Assert.Equal(0, second.DirectoriesMoved);
            Assert.Equal(0, second.FilesMoved);
            Assert.Equal(0, second.FilesPreserved);
            Assert.Equal(0, second.IdenticalRemoved);
            Assert.Contains("no legacy files or directories remain", string.Join("\n", second.Lines));
            Assert.True(cert.AsSpan().SequenceEqual(File.ReadAllBytes(certPath)), "re-run changed cert.pem");
            Assert.True(key.AsSpan().SequenceEqual(File.ReadAllBytes(keyPath)), "re-run changed key.pem");
            Assert.True(database.AsSpan().SequenceEqual(File.ReadAllBytes(databasePath)), "re-run changed the database");
            Assert.Equal("NEW-CONFIG", File.ReadAllText(Path.Combine(root, "support", "data", "syncthing", "config.xml")));
            Assert.True(conflict.AsSpan().SequenceEqual(File.ReadAllBytes(conflictPath)), "re-run changed the preserved legacy config");
            Assert.Equal(1, Directory.GetFiles(Path.GetDirectoryName(conflictPath)!, "config.xml*", SearchOption.TopDirectoryOnly).Length);
            Assert.Equal("synced", File.ReadAllText(Path.Combine(root, "folders", "Photos", "keep.txt")));
            Assert.False(Directory.Exists(Path.Combine(root, "data")), "re-run should not recreate legacy data");
            Assert.False(Directory.Exists(Path.Combine(root, "bin")), "re-run should not recreate legacy bin");
        });
    }

    private static void FailureLeavesSource()
    {
        WithTemp(root =>
        {
            Write(Path.Combine(root, "data", "syncthing", "cert.pem"), "CERT");
            Write(Path.Combine(root, "VERSION.txt"), "OLD-VERSION");
            Directory.CreateDirectory(Path.Combine(root, "support", "VERSION.txt"));

            var exception = Assert.Throws<LayoutMigrationException>(() => LayoutMigration.Migrate(root, relocateLooseNativeLibraries: true));

            Assert.Equal("OLD-VERSION", File.ReadAllText(Path.Combine(root, "VERSION.txt")));
            Assert.True(Directory.Exists(Path.Combine(root, "support", "VERSION.txt")), "existing support directory must not be replaced");
            Assert.Equal("CERT", File.ReadAllText(Path.Combine(root, "support", "data", "syncthing", "cert.pem")));
            Assert.False(File.Exists(Path.Combine(root, "data", "syncthing", "cert.pem")), "the rename that succeeded stays in support");
            Assert.Contains("Layout migration failed", string.Join("\n", exception.Report.Lines));
            Assert.Contains("before Syncthing starts", exception.Message);
        });
    }

    private static void NonSingleFileLeavesNatives()
    {
        WithTemp(root =>
        {
            Write(Path.Combine(root, "data", "syncthing", "cert.pem"), "CERT");
            Write(Path.Combine(root, "WebView2Loader.dll"), "LOADER");
            Write(Path.Combine(root, "wpfgfx_cor3.dll"), "WPF");
            Write(Path.Combine(root, "runtimes", "win-x64", "native", "WebView2Loader.dll"), "NESTED");
            Write(Path.Combine(root, "notes.txt"), "user");

            var report = LayoutMigration.Migrate(root, relocateLooseNativeLibraries: false);

            Assert.Equal("CERT", File.ReadAllText(Path.Combine(root, "support", "data", "syncthing", "cert.pem")));
            Assert.Equal("LOADER", File.ReadAllText(Path.Combine(root, "WebView2Loader.dll")));
            Assert.Equal("WPF", File.ReadAllText(Path.Combine(root, "wpfgfx_cor3.dll")));
            Assert.Equal("NESTED", File.ReadAllText(Path.Combine(root, "runtimes", "win-x64", "native", "WebView2Loader.dll")));
            Assert.Equal("user", File.ReadAllText(Path.Combine(root, "notes.txt")));
            Assert.False(File.Exists(Path.Combine(root, "support", "WebView2Loader.dll")), "framework-dependent run must not hide the loader in support");
            Assert.True(report.DirectoriesMoved >= 1, "legacy data should still move");
        });
    }

    private static void AssertTopLevel(string root, params string[] expected)
    {
        var actual = Directory.GetFileSystemEntries(root)
            .Select(Path.GetFileName)
            .OrderBy(name => name, StringComparer.Ordinal)
            .ToArray();
        var wanted = expected.OrderBy(name => name, StringComparer.Ordinal).ToArray();
        Assert.Equal(string.Join(",", wanted), string.Join(",", actual!));
    }

    private static void Write(string path, string content)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, content);
    }

    private static void WithTemp(Action<string> test)
    {
        var root = Path.Combine(Path.GetTempPath(), "pst-layout-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            test(root);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }
}
