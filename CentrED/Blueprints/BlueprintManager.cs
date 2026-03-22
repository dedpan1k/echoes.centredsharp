using CentrED.UI;
using ClassicUO.Assets;

namespace CentrED.Blueprints;

/// <summary>
/// Loads built-in multis and on-disk blueprint files into a navigable tree.
/// </summary>
public class BlueprintManager(MultiLoader multiLoader)
{
    /// <summary>
    /// Gets the directory used to store blueprint files.
    /// </summary>
    public const string BLUEPRINTS_DIR = "Blueprints";
    
    /// <summary>
    /// Gets the root entry for the loaded blueprint tree.
    /// </summary>
    public BlueprintTreeEntry Root = new("Root", true, []);

    /// <summary>
    /// Reloads built-in multis and user blueprint files.
    /// </summary>
    public void Load()
    {
        Root = new("Root", true, []);
        LoadMultis();
        LoadBlueprints();
    }

    /// <summary>
    /// Loads the built-in multi definitions into the blueprint tree.
    /// </summary>
    private void LoadMultis()
    {
        Dictionary<uint, string> multiNames = MultiNamesReader.Read(BLUEPRINTS_DIR);
        var multisEntry = new BlueprintTreeEntry("multi.mul", true, []);
        for (uint i = 0; i < MultiLoader.MAX_MULTI_DATA_INDEX_COUNT; i++)
        {
            var info = multiLoader.GetMultis(i);
            if (info != null && info.Count > 0)
            {
                if (info.All(x => x.ID == 0))
                    continue;

                var path = $"{multisEntry.Path}/{i.FormatId()}:{multiNames.GetValueOrDefault(i, "Unknown")}";
                var entry = new BlueprintTreeEntry(path, true, []);
                entry.Tiles = info.Select(tile => new BlueprintTile(tile)).ToList();
                multisEntry.Children.Add(entry);
            }
        }
        Root.Children.Add(multisEntry);
    }

    /// <summary>
    /// Loads user blueprint files from disk.
    /// </summary>
    public void LoadBlueprints()
    {
        if (!Directory.Exists(BLUEPRINTS_DIR))
            Directory.CreateDirectory(BLUEPRINTS_DIR);

        var blueprints = LoadBlueprintDirectory(BLUEPRINTS_DIR);
        Root.Children.AddRange(blueprints.Children);
    }

    /// <summary>
    /// Recursively loads a blueprint directory into a tree entry.
    /// </summary>
    /// <param name="path">The directory path to scan.</param>
    /// <returns>The populated blueprint tree entry.</returns>
    private BlueprintTreeEntry LoadBlueprintDirectory(string path)
    {
        var result = new BlueprintTreeEntry(path, true, []);
        var dirs = Directory.GetDirectories(path, "*", SearchOption.TopDirectoryOnly);
        foreach (var dir in dirs)
        {
            result.Children.Add(LoadBlueprintDirectory(dir));
        }
        var files = Directory.GetFiles(path, "*", SearchOption.TopDirectoryOnly);
        foreach (var file in files)
        {
            if(file.EndsWith(MultiNamesReader.FILE_NAME))
                continue;
            
            result.Children.Add(new BlueprintTreeEntry(file, false, []));
        }
        return result;
    }
}