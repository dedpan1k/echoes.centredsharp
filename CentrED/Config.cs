using System.Text.Json;
using CentrED.IO.Models;
using Microsoft.Xna.Framework.Input;

namespace CentrED;

/// <summary>
/// Stores settings for the optional reference image overlay drawn over the map.
/// </summary>
public class ImageOverlaySettings
{
    /// <summary>
    /// Gets or sets the path to the image file used as the overlay source.
    /// </summary>
    // Path to the user-supplied reference image that can be composited over the
    // map while editing.
    public string ImagePath = "";

    /// <summary>
    /// Gets or sets a value indicating whether the overlay is enabled.
    /// </summary>
    public bool Enabled;

    /// <summary>
    /// Gets or sets a value indicating whether the overlay is drawn above terrain.
    /// </summary>
    public bool DrawAboveTerrain;

    /// <summary>
    /// Gets or sets the overlay anchor X coordinate in world space.
    /// </summary>
    // World-space anchor for the overlay so it can be aligned with map tiles.
    public int WorldX;

    /// <summary>
    /// Gets or sets the overlay anchor Y coordinate in world space.
    /// </summary>
    public int WorldY;

    /// <summary>
    /// Gets or sets the scale factor applied when rendering the overlay.
    /// </summary>
    // Visual tuning controls applied when the overlay is rendered.
    public float Scale = 1.0f;

    /// <summary>
    /// Gets or sets the overlay opacity.
    /// </summary>
    public float Opacity = 1.0f;

    /// <summary>
    /// Gets or sets the screen blend amount used when drawing the overlay.
    /// </summary>
    public float Screen = 0.0f;
}

/// <summary>
/// Represents the persisted root settings object for the editor.
/// </summary>
public class ConfigRoot
{
    /// <summary>
    /// Gets or sets the name of the profile that should be active on startup.
    /// </summary>
    // Persist the last selected connection/profile so the editor can restore it
    // on the next launch.
    public string ActiveProfile = "";

    /// <summary>
    /// Gets or sets the path to the server configuration file.
    /// </summary>
    public string ServerConfigPath = "cedserver.xml";

    /// <summary>
    /// Gets or sets a value indicating whether texture maps are preferred when available.
    /// </summary>
    // Editor-wide feature toggles and rendering preferences.
    public bool PreferTexMaps;

    /// <summary>
    /// Gets or sets a value indicating whether objects should use bright highlighting.
    /// </summary>
    public bool ObjectBrightHighlight;

    /// <summary>
    /// Gets or sets a value indicating whether legacy mouse scroll behavior is enabled.
    /// </summary>
    public bool LegacyMouseScroll;

    /// <summary>
    /// Gets or sets a value indicating whether editor viewports are enabled.
    /// </summary>
    public bool Viewports;

    /// <summary>
    /// Gets or sets the graphics backend preference.
    /// </summary>
    public string GraphicsDriver = "Auto"; //Auto,SDL_GPU,D3D11,OpenGL

    /// <summary>
    /// Gets or sets persisted window layout state keyed by window identifier.
    /// </summary>
    // UI state that should survive between sessions.
    public Dictionary<string, WindowState> Layout = new();

    /// <summary>
    /// Gets or sets persisted key bindings keyed by action name.
    /// </summary>
    public Dictionary<string, (Keys[], Keys[])> Keymap = new();

    /// <summary>
    /// Gets or sets the editor font size.
    /// </summary>
    public int FontSize = 13;

    /// <summary>
    /// Gets or sets the font file name used by the editor UI.
    /// </summary>
    public string FontName = "ProggyClean.ttf";

    /// <summary>
    /// Gets or sets the active UI language.
    /// </summary>
    public string Language = "English";

    /// <summary>
    /// Gets or sets the numeric display format used by the UI.
    /// </summary>
    public UI.NumberDisplayFormat NumberFormat = UI.NumberDisplayFormat.HEX;

    /// <summary>
    /// Gets or sets the image overlay settings.
    /// </summary>
    public ImageOverlaySettings ImageOverlay = new();
}

/// <summary>
/// Loads, exposes, and persists the editor configuration.
/// </summary>
public static class Config
{
    // Throttle disk writes while the editor is running; many UI interactions can
    // touch config state in quick succession.
    private static readonly TimeSpan ConfigSaveRate = TimeSpan.FromSeconds(30);
    private static DateTime LastConfigSave = DateTime.Now;

    // The config objects use public fields rather than properties, so JSON
    // serialization must explicitly opt in to field handling.
    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        IncludeFields = true,
        WriteIndented = true,
    };
    
    /// <summary>
    /// Gets the in-memory configuration instance used by the running editor.
    /// </summary>
    // This singleton-style root object is mutated throughout the app and then
    // periodically written back to settings.json.
    public static ConfigRoot Instance;
    private static string _configFilePath = "settings.json";

    /// <summary>
    /// Loads the configuration file and applies startup settings that must be available early.
    /// </summary>
    public static void Initialize()
    {
        // First launch creates a default config file so later reads and edits can
        // assume the file is present.
        if (!File.Exists(_configFilePath))
        {
            var newConfig = new ConfigRoot();
            File.WriteAllText(_configFilePath, JsonSerializer.Serialize(newConfig));
        }

        var jsonText = File.ReadAllText(_configFilePath);
        Instance = JsonSerializer.Deserialize<ConfigRoot>(jsonText, SerializerOptions);

        // FNA chooses its backend during startup, so export the selected driver
        // before graphics device creation begins.
        if (Instance.GraphicsDriver != "Auto")
            Environment.SetEnvironmentVariable("FNA3D_FORCE_DRIVER", Instance.GraphicsDriver);
    }

    /// <summary>
    /// Saves the configuration when the autosave interval has elapsed.
    /// </summary>
    public static void AutoSave()
    {
        // Callers can invoke this frequently; the rate limiter ensures we only
        // hit disk when the save interval has elapsed.
        if (DateTime.Now > LastConfigSave + ConfigSaveRate)
        {
            Save();
        }
    }

    /// <summary>
    /// Writes the current configuration snapshot to disk immediately.
    /// </summary>
    public static void Save()
    {
        // Serialize the in-memory config snapshot exactly as the editor sees it,
        // then move the save watermark forward.
        File.WriteAllText(_configFilePath, JsonSerializer.Serialize(Instance, SerializerOptions));
        LastConfigSave = DateTime.Now;
    }
}