using System.Xml.Linq;

namespace PortableSyncthing.Core;

public static class SyncthingConfigRemapper
{
    public static string RemapPortableFolders(
        string xml,
        PortableRoot portableRoot,
        IReadOnlyDictionary<string, string> folderMappings)
    {
        var document = XDocument.Parse(xml, LoadOptions.PreserveWhitespace);
        foreach (var folder in document.Root?.Elements("folder") ?? Enumerable.Empty<XElement>())
        {
            var id = (string?)folder.Attribute("id");
            if (id is null || !folderMappings.TryGetValue(id, out var relativePath))
                continue;

            folder.SetAttributeValue("path", ResolveFolderPath(portableRoot, relativePath));
        }

        return document.Declaration is null
            ? document.ToString(SaveOptions.DisableFormatting)
            : document.Declaration + Environment.NewLine + document.ToString(SaveOptions.DisableFormatting);
    }

    public static string ResolveFolderPath(PortableRoot portableRoot, string relativePath) =>
        portableRoot.ResolvePortableRelative(relativePath);

    public static IReadOnlyDictionary<string, string> DiscoverPortableMappings(string xml, PortableRoot portableRoot)
    {
        var rootPrefix = portableRoot.RootDirectory.TrimEnd('\\') + "\\";
        var document = XDocument.Parse(xml);
        return (document.Root?.Elements("folder") ?? Enumerable.Empty<XElement>())
            .Select(folder => new { Id = (string?)folder.Attribute("id"), Path = (string?)folder.Attribute("path") })
            .Where(item => item.Id is not null && item.Path is not null && item.Path.StartsWith(rootPrefix, StringComparison.OrdinalIgnoreCase))
            .ToDictionary(item => item.Id!, item => item.Path![rootPrefix.Length..]);
    }

    public static string? ReadGuiApiKey(string xml) =>
        XDocument.Parse(xml).Root?.Element("gui")?.Element("apikey")?.Value;
}
