using CentrED.IO;

namespace CentrED.Assets;

/// <summary>
/// Represents the coarse-grained asset families that will be surfaced in the native asset workspace.
/// </summary>
public enum AssetWorkspaceFamilyKind
{
    ArtAndLandTiles,
    GumpsAndTextures,
    AnimationsAndAnimData,
    HuesAndTileData,
    MapsStaticsAndReplacement,
    Multis,
}

/// <summary>
/// Describes the readiness of one asset family for the currently selected Ultima data directory.
/// </summary>
/// <param name="Kind">The asset family represented by this status.</param>
/// <param name="DisplayName">The user-facing name of the asset family.</param>
/// <param name="Description">A short description of the workflows planned for the family.</param>
/// <param name="IsReady">Whether the currently selected directory satisfies the required file groups.</param>
/// <param name="ResolvedFiles">The file names found that satisfy the family requirements.</param>
/// <param name="MissingRequirements">The missing requirement groups, flattened for display.</param>
public readonly record struct AssetWorkspaceFamilyStatus(
    AssetWorkspaceFamilyKind Kind,
    string DisplayName,
    string Description,
    bool IsReady,
    IReadOnlyList<string> ResolvedFiles,
    IReadOnlyList<string> MissingRequirements);

/// <summary>
/// Discovers, normalizes, and validates the local Ultima data directory used by the asset workspace.
/// </summary>
public sealed class AssetWorkspaceService
{
    private sealed record AssetWorkspaceFamilyDefinition(
        AssetWorkspaceFamilyKind Kind,
        string DisplayName,
        string Description,
        string[][] RequiredFileGroups);

    private static readonly AssetWorkspaceFamilyDefinition[] Definitions =
    [
        new(
            AssetWorkspaceFamilyKind.ArtAndLandTiles,
            "Art and Land Tiles",
            "Browse, preview, replace, import, export, and save terrain and static art assets.",
            [
                ["tiledata.mul"],
                ["art.mul", "artLegacyMUL.uop"],
            ]),
        new(
            AssetWorkspaceFamilyKind.GumpsAndTextures,
            "Gumps and Textures",
            "Inspect UI gumps and texture maps, then replace, import, export, and save them.",
            [
                ["gumpart.mul", "gumpartLegacyMUL.uop"],
                ["texmaps.mul", "texmapsLegacyMUL.uop"],
            ]),
        new(
            AssetWorkspaceFamilyKind.AnimationsAndAnimData,
            "Animations and AnimData",
            "Preview animation frames, edit animdata metadata, and support playback plus import/export flows.",
            [
                ["animdata.mul"],
                ["anim.mul", "anim2.mul", "anim3.mul", "anim4.mul", "anim5.mul"],
            ]),
        new(
            AssetWorkspaceFamilyKind.HuesAndTileData,
            "Hues and TileData",
            "Edit hue palettes, tile flags, names, and metadata-driven filters with round-trip saving.",
            [
                ["hues.mul"],
                ["tiledata.mul"],
            ]),
        new(
            AssetWorkspaceFamilyKind.MapsStaticsAndReplacement,
            "Maps, Statics, and Replacement",
            "Run map and statics replacement tools and stage world-apply operations from asset-derived input.",
            [
                ["map0.mul", "map0LegacyMUL.uop"],
                ["staidx0.mul"],
                ["statics0.mul"],
            ]),
        new(
            AssetWorkspaceFamilyKind.Multis,
            "Multis",
            "View, edit, and import/export multis and their component layouts using centeredsharp-native tools.",
            [
                ["multi.idx"],
                ["multi.mul"],
            ]),
    ];

    private readonly List<AssetWorkspaceFamilyStatus> _families = [];

    /// <summary>
    /// Gets the current family readiness snapshot.
    /// </summary>
    public IReadOnlyList<AssetWorkspaceFamilyStatus> Families => _families;

    /// <summary>
    /// Gets the resolved root path currently being inspected.
    /// </summary>
    public string EffectiveRootPath { get; private set; } = "";

    /// <summary>
    /// Gets the explicit asset-directory override stored in configuration.
    /// </summary>
    public string ConfiguredRootPath => NormalizePath(Config.Instance.AssetDirectory);

    /// <summary>
    /// Gets the current active profile's client directory when available.
    /// </summary>
    public string ProfileRootPath => NormalizePath(ProfileManager.ActiveProfile.ClientPath);

    /// <summary>
    /// Gets whether the effective path currently comes from the active profile instead of the config override.
    /// </summary>
    public bool UsingProfileFallback => ConfiguredRootPath.Length == 0 && ProfileRootPath.Length > 0;

    /// <summary>
    /// Gets whether the effective root path exists on disk.
    /// </summary>
    public bool HasValidRootPath { get; private set; }

    /// <summary>
    /// Gets the number of top-level files discovered while scanning the effective root path.
    /// </summary>
    public int DiscoveredFileCount { get; private set; }

    /// <summary>
    /// Gets the number of asset families that are currently ready.
    /// </summary>
    public int ReadyFamilyCount => _families.Count(x => x.IsReady);

    /// <summary>
    /// Gets the last status message produced during refresh.
    /// </summary>
    public string StatusMessage { get; private set; } = "Choose a Ultima data directory to enable the asset workspace.";

    /// <summary>
    /// Gets the time of the most recent refresh operation in UTC.
    /// </summary>
    public DateTime LastRefreshUtc { get; private set; }

    /// <summary>
    /// Persists a new asset root override and refreshes the readiness snapshot.
    /// </summary>
    /// <param name="path">The new override path. An empty value clears the override.</param>
    public void SetConfiguredRootPath(string path)
    {
        Config.Instance.AssetDirectory = NormalizePath(path);
        Config.Save();
        Refresh();
    }

    /// <summary>
    /// Rebuilds the readiness snapshot for the effective asset directory.
    /// </summary>
    public void Refresh()
    {
        LastRefreshUtc = DateTime.UtcNow;
        EffectiveRootPath = ConfiguredRootPath.Length > 0 ? ConfiguredRootPath : ProfileRootPath;
        _families.Clear();

        if (EffectiveRootPath.Length == 0)
        {
            HasValidRootPath = false;
            DiscoveredFileCount = 0;
            StatusMessage = "Choose a Ultima data directory or connect with a profile that already has one configured.";
            PopulateUnavailableFamilies();
            return;
        }

        if (!Directory.Exists(EffectiveRootPath))
        {
            HasValidRootPath = false;
            DiscoveredFileCount = 0;
            StatusMessage = $"Asset directory not found: {EffectiveRootPath}";
            PopulateUnavailableFamilies();
            return;
        }

        try
        {
            var files = Directory
                .EnumerateFiles(EffectiveRootPath)
                .Select(Path.GetFileName)
                .OfType<string>()
                .ToHashSet(StringComparer.OrdinalIgnoreCase);

            DiscoveredFileCount = files.Count;
            HasValidRootPath = true;

            foreach (var definition in Definitions)
            {
                _families.Add(BuildStatus(definition, files));
            }

            StatusMessage = ReadyFamilyCount == Definitions.Length
                ? "Asset workspace foundation is ready for all currently tracked families."
                : $"Asset workspace foundation is ready for {ReadyFamilyCount} of {Definitions.Length} tracked families.";
        }
        catch (Exception ex)
        {
            HasValidRootPath = false;
            DiscoveredFileCount = 0;
            StatusMessage = $"Failed to scan asset directory: {ex.Message}";
            PopulateUnavailableFamilies();
        }
    }

    /// <summary>
    /// Normalizes a user-supplied path so the config and UI can compare values consistently.
    /// </summary>
    /// <param name="path">The raw path value.</param>
    /// <returns>The normalized path, or an empty string if none was provided.</returns>
    public static string NormalizePath(string? path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return string.Empty;
        }

        return Path.TrimEndingDirectorySeparator(path.Trim());
    }

    private void PopulateUnavailableFamilies()
    {
        foreach (var definition in Definitions)
        {
            _families.Add(new AssetWorkspaceFamilyStatus(
                definition.Kind,
                definition.DisplayName,
                definition.Description,
                false,
                [],
                definition.RequiredFileGroups.Select(FormatRequirementGroup).ToArray()));
        }
    }

    private static AssetWorkspaceFamilyStatus BuildStatus(AssetWorkspaceFamilyDefinition definition, HashSet<string> files)
    {
        var resolvedFiles = new List<string>();
        var missingRequirements = new List<string>();

        foreach (var group in definition.RequiredFileGroups)
        {
            var match = group.FirstOrDefault(files.Contains);
            if (match != null)
            {
                resolvedFiles.Add(match);
            }
            else
            {
                missingRequirements.Add(FormatRequirementGroup(group));
            }
        }

        return new AssetWorkspaceFamilyStatus(
            definition.Kind,
            definition.DisplayName,
            definition.Description,
            missingRequirements.Count == 0,
            resolvedFiles,
            missingRequirements);
    }

    private static string FormatRequirementGroup(IEnumerable<string> group)
    {
        return string.Join(" or ", group);
    }
}