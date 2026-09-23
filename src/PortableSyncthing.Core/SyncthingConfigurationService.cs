using System.Text.Json;

namespace PortableSyncthing.Core;

public sealed class PortableFolderMappingStore
{
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    public IReadOnlyDictionary<string, string> Load(string path)
    {
        if (!File.Exists(path)) return new Dictionary<string, string>(StringComparer.Ordinal);
        var mappings = JsonSerializer.Deserialize<Dictionary<string, string>>(File.ReadAllText(path), JsonOptions)
            ?? new Dictionary<string, string>();
        return new Dictionary<string, string>(mappings, StringComparer.Ordinal);
    }

    public void Save(string path, IReadOnlyDictionary<string, string> mappings)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path) ?? throw new PortablePathException("Mapping path needs a parent directory."));
        var temporary = path + ".tmp";
        File.WriteAllText(temporary, JsonSerializer.Serialize(mappings, JsonOptions));
        File.Move(temporary, path, true);
    }
}

public sealed class SyncthingConfigurationService
{
    private readonly PortableFolderMappingStore _mappingStore = new();

    public string PrepareExistingConfiguration(PortableRoot root)
    {
        var configPath = Path.Combine(root.SyncthingHomeDirectory, "config.xml");
        if (!File.Exists(configPath))
            throw new FileNotFoundException("Syncthing configuration has not been generated yet.", configPath);

        var xml = File.ReadAllText(configPath);
        var mappings = new Dictionary<string, string>(_mappingStore.Load(root.FolderMappingsPath), StringComparer.Ordinal);
        foreach (var (id, relativePath) in SyncthingConfigRemapper.DiscoverPortableMappings(xml, root))
            mappings[id] = relativePath;

        var remapped = SyncthingConfigRemapper.RemapPortableFolders(xml, root, mappings);
        WriteAtomically(configPath, remapped);
        _mappingStore.Save(root.FolderMappingsPath, mappings);
        return SyncthingConfigRemapper.ReadGuiApiKey(remapped) ?? string.Empty;
    }

    private static void WriteAtomically(string path, string content)
    {
        var temporary = path + ".portable-tmp";
        File.WriteAllText(temporary, content);
        File.Move(temporary, path, true);
    }
}
