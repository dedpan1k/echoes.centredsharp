using System.Text.Json;
using System.Text.Json.Serialization;

namespace CentrED.IO.Models;

/// <summary>
/// Represents a persisted client profile, including connection info, favorites, tile sets, and brushes.
/// </summary>
public class Profile
{
    private const string PROFILE_FILE = "profile.json";
    private const string LOCATIONS_FILE = "locations.json";
    private const string LAND_TILE_SETS_FILE = "landtilesets.json";
    private const string STATIC_TILE_SETS_FILE = "statictilesets.json";
    private const string HUE_SETS_FILE = "huesets.json";
    private const string LAND_BRUSH_FILE = "landbrush.json";
    private const string STATIC_FILTER_FILE = "staticfilter.json";

    /// <summary>
    /// Gets or sets the profile name.
    /// </summary>
    [JsonIgnore] public string Name { get; set; } = "";

    /// <summary>
    /// Gets or sets the server hostname.
    /// </summary>
    public string Hostname { get; set; } = "127.0.0.1";

    /// <summary>
    /// Gets or sets the server port.
    /// </summary>
    public int Port { get; set; } = 2597;

    /// <summary>
    /// Gets or sets the profile username.
    /// </summary>
    public string Username { get; set; } = "";

    /// <summary>
    /// Gets or sets the profile password.
    /// </summary>
    public string Password { get; set; } = "";

    /// <summary>
    /// Gets or sets the Ultima Online client path used by the profile.
    /// </summary>
    public string ClientPath { get; set; } = "";

    /// <summary>
    /// Gets or sets named radar favorites.
    /// </summary>
    [JsonIgnore]
    public Dictionary<string, RadarFavorite> RadarFavorites { get; set; } = new();

    /// <summary>
    /// Gets or sets named land tile sets.
    /// </summary>
    [JsonIgnore] public Dictionary<string, List<ushort>> LandTileSets { get; set; } = new();

    /// <summary>
    /// Gets or sets named static tile sets.
    /// </summary>
    [JsonIgnore] public Dictionary<string, List<ushort>> StaticTileSets { get; set; } = new();

    /// <summary>
    /// Gets or sets named hue sets.
    /// </summary>
    [JsonIgnore] public Dictionary<string, SortedSet<ushort>> HueSets { get; set; } = new();

    /// <summary>
    /// Gets or sets named land brushes.
    /// </summary>
    [JsonIgnore] public Dictionary<string, LandBrush> LandBrush { get; set; } = new();

    /// <summary>
    /// Gets or sets the static filter applied by the client.
    /// </summary>
    [JsonIgnore] public List<int> StaticFilter { get; set; } = new();


    /// <summary>
    /// Serializes the profile and its companion files into the supplied directory.
    /// </summary>
    /// <param name="path">The root profiles directory.</param>
    public void Serialize(String path)
    {
        var profileDir = Path.Join(path, Name);
        if (!Directory.Exists(profileDir))
        {
            Directory.CreateDirectory(profileDir);
        }
        var options = new JsonSerializerOptions
        {
            WriteIndented = true
        };
        File.WriteAllText(Path.Join(profileDir, PROFILE_FILE), JsonSerializer.Serialize(this, options));
        File.WriteAllText(Path.Join(profileDir, LOCATIONS_FILE), JsonSerializer.Serialize(RadarFavorites, options));
        File.WriteAllText(Path.Join(profileDir, LAND_TILE_SETS_FILE), JsonSerializer.Serialize(LandTileSets, options));
        File.WriteAllText(Path.Join(profileDir, STATIC_TILE_SETS_FILE), JsonSerializer.Serialize(StaticTileSets, options));
        File.WriteAllText(Path.Join(profileDir, HUE_SETS_FILE), JsonSerializer.Serialize(HueSets, options));
        File.WriteAllText(Path.Join(profileDir, LAND_BRUSH_FILE), JsonSerializer.Serialize(LandBrush, Models.LandBrush.JsonOptions));
        File.WriteAllText(Path.Join(profileDir, STATIC_FILTER_FILE), JsonSerializer.Serialize(StaticFilter, options));
    }

    /// <summary>
    /// Loads a profile from a profile directory.
    /// </summary>
    /// <param name="profileDir">The profile directory path.</param>
    /// <returns>The deserialized profile, or <see langword="null"/> when loading fails.</returns>
    public static Profile? Deserialize(string profileDir)
    {
        DirectoryInfo dir = new DirectoryInfo(profileDir);
        if (!dir.Exists)
            return null;

        var profile = JsonSerializer.Deserialize<Profile>(File.ReadAllText(Path.Join(profileDir, PROFILE_FILE)));
        if (profile == null)
            return null;
        profile.Name = dir.Name;

        var favorites = Deserialize<Dictionary<string, RadarFavorite>>(Path.Join(profileDir, LOCATIONS_FILE));
        if (favorites != null)
            profile.RadarFavorites = favorites;

        var landTileSets = Deserialize<Dictionary<string, List<ushort>>>(Path.Join(profileDir, LAND_TILE_SETS_FILE));
        if (landTileSets != null)
            profile.LandTileSets = landTileSets;

        var staticTileSets = Deserialize<Dictionary<string, List<ushort>>>(Path.Join(profileDir, STATIC_TILE_SETS_FILE));
        if (staticTileSets != null)
            profile.StaticTileSets = staticTileSets;

        var huesets = Deserialize<Dictionary<string, SortedSet<ushort>>>(Path.Join(profileDir, HUE_SETS_FILE));
        if (huesets != null)
            profile.HueSets = huesets;

        var landBrush = Deserialize<Dictionary<string, LandBrush>>(Path.Join(profileDir, LAND_BRUSH_FILE), Models.LandBrush.JsonOptions);
        if (landBrush != null)
            profile.LandBrush = landBrush;

        var staticFilter = Deserialize<List<int>>(Path.Join(profileDir, STATIC_FILTER_FILE));
        if (staticFilter != null)
            profile.StaticFilter = staticFilter;

        return profile;
    }

    /// <summary>
    /// Deserializes a companion profile file using default JSON options.
    /// </summary>
    /// <typeparam name="T">The target type.</typeparam>
    /// <param name="filePath">The file path to read.</param>
    /// <returns>The deserialized value, or the default value when the file is missing.</returns>
    private static T? Deserialize<T>(string filePath)
    {
        return Deserialize<T>(filePath, JsonSerializerOptions.Default);
    }

    /// <summary>
    /// Deserializes a companion profile file using explicit JSON options.
    /// </summary>
    /// <typeparam name="T">The target type.</typeparam>
    /// <param name="filePath">The file path to read.</param>
    /// <param name="options">The serializer options to use.</param>
    /// <returns>The deserialized value, or the default value when the file is missing.</returns>
    private static T? Deserialize<T>(string filePath, JsonSerializerOptions options)
    {
        if (!File.Exists(filePath))
            return default;
        return JsonSerializer.Deserialize<T>(File.ReadAllText(filePath), options);
    }

    /// <summary>
    /// Persists only the static-filter companion file for the profile.
    /// </summary>
    /// <param name="path">The root profiles directory.</param>
    public void SerializeStaticFilter(string path)
    {
        var profileDir = Path.Join(path, Name);
        if (!Directory.Exists(profileDir))
        {
            Directory.CreateDirectory(profileDir);
        }
        
        var options = new JsonSerializerOptions
        {
            WriteIndented = true
        };
        
        File.WriteAllText(Path.Join(profileDir, STATIC_FILTER_FILE), JsonSerializer.Serialize(StaticFilter, options));
    }

}