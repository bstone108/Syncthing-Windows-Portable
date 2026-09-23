using System.Xml.Linq;
using PortableSyncthing.Core;

var suite = new ContractSuite();
suite.Run("portable root derives from executable directory", () =>
{
    var root = PortableRoot.FromExecutablePath(@"F:\PortableSyncthing\PortableSyncthing.exe");
    Assert.Equal(@"F:\PortableSyncthing", root.RootDirectory);
    Assert.Equal(@"F:\PortableSyncthing\data\syncthing", root.SyncthingHomeDirectory);
    Assert.Equal(@"F:\PortableSyncthing\bin\current\syncthing.exe", root.SyncthingExecutablePath);
});

suite.Run("remapper rewrites portable folder paths after drive letter changes", () =>
{
    var portable = PortableRoot.FromExecutablePath(@"G:\PortableSyncthing\PortableSyncthing.exe");
    const string config = """
        <configuration version="37">
          <folder id="photos" path="F:\PortableSyncthing\folders\Photos" />
          <folder id="external" path="D:\Media" />
        </configuration>
        """;

    var remapped = SyncthingConfigRemapper.RemapPortableFolders(
        config,
        portable,
        new Dictionary<string, string>
        {
            ["photos"] = @"folders\Photos"
        });

    var document = XDocument.Parse(remapped);
    var folders = document.Root!.Elements("folder").ToDictionary(x => (string)x.Attribute("id")!, x => (string)x.Attribute("path")!);
    Assert.Equal(@"G:\PortableSyncthing\folders\Photos", folders["photos"]);
    Assert.Equal(@"D:\Media", folders["external"]);
});

suite.Run("remapper rejects a mapping that escapes the portable root", () =>
{
    var portable = PortableRoot.FromExecutablePath(@"G:\PortableSyncthing\PortableSyncthing.exe");
    Assert.Throws<PortablePathException>(() => SyncthingConfigRemapper.ResolveFolderPath(portable, @"..\outside"));
});

suite.Run("process arguments force portable home and disable Syncthing self-update", () =>
{
    var portable = PortableRoot.FromExecutablePath(@"G:\PortableSyncthing\PortableSyncthing.exe");
    var arguments = SyncthingLaunchPlan.Create(portable, 8384).Arguments;
    Assert.Contains("--home=G:\\PortableSyncthing\\data\\syncthing", arguments);
    Assert.Contains("--no-browser", arguments);
    Assert.Contains("--no-upgrade", arguments);
    Assert.Contains("--gui-address=127.0.0.1:8384", arguments);
});

suite.Finish();

internal sealed class ContractSuite
{
    private int _passed;

    public void Run(string name, Action test)
    {
        test();
        _passed++;
        Console.WriteLine($"PASS {name}");
    }

    public void Finish() => Console.WriteLine($"{_passed} contract tests passed.");
}

internal static class Assert
{
    public static void Equal<T>(T expected, T actual)
    {
        if (!EqualityComparer<T>.Default.Equals(expected, actual))
            throw new Exception($"Expected '{expected}', got '{actual}'.");
    }

    public static void Contains(string expected, IEnumerable<string> actual)
    {
        if (!actual.Contains(expected, StringComparer.Ordinal))
            throw new Exception($"Expected collection to contain '{expected}'.");
    }

    public static void Throws<T>(Action action) where T : Exception
    {
        try { action(); }
        catch (T) { return; }
        throw new Exception($"Expected {typeof(T).Name}.");
    }
}
