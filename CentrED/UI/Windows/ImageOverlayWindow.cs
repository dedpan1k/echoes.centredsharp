using CentrED.IO.Models;
using CentrED.Map;
using Hexa.NET.ImGui;
using static CentrED.Application;
using static CentrED.LangEntry;

namespace CentrED.UI.Windows;

/// <summary>
/// Configures the optional reference-image overlay that can be drawn over the map for tracing
/// or alignment work.
/// </summary>
public class ImageOverlayWindow : Window
{
    /// <summary>
    /// Stable ImGui title/ID pair for the image overlay window.
    /// </summary>
    public override string Name => LangManager.Get(IMAGE_OVERLAY_WINDOW) + "###ImageOverlay";

    /// <summary>
    /// The overlay editor auto-resizes to fit the current set of controls.
    /// </summary>
    public override ImGuiWindowFlags WindowFlags => ImGuiWindowFlags.AlwaysAutoResize;

    /// <summary>
    /// The overlay window starts hidden until explicitly opened by the user.
    /// </summary>
    public override WindowState DefaultState => new()
    {
        IsOpen = false
    };

    // UI-local copies of the editable values used by ImGui controls.
    private string _imagePath = "";
    private int[] _position = new int[2];
    private float _scale = 1.0f;
    private float _opacity = 1.0f;
    private float _screen = 0.0f;

    // Settings are pulled once on first draw so opening the window reflects the saved overlay state.
    private bool _settingsLoaded = false;

    /// <summary>
    /// Shortcut to the persisted overlay settings stored in the application config.
    /// </summary>
    private ImageOverlaySettings Settings => Config.Instance.ImageOverlay;

    /// <summary>
    /// Lazily loads the saved overlay state into both the window controls and the live map
    /// overlay instance.
    /// </summary>
    private void LoadSettings()
    {
        if (_settingsLoaded)
            return;

        _settingsLoaded = true;
        var overlay = CEDGame.MapManager.ImageOverlay;

        _imagePath = Settings.ImagePath;
        overlay.Enabled = Settings.Enabled;
        overlay.DrawAboveTerrain = Settings.DrawAboveTerrain;
        overlay.WorldX = Settings.WorldX;
        overlay.WorldY = Settings.WorldY;
        overlay.Scale = Settings.Scale;
        overlay.Opacity = Settings.Opacity;
        overlay.Screen = Settings.Screen;

        if (!string.IsNullOrEmpty(_imagePath) && File.Exists(_imagePath))
        {
            try
            {
                // Restore the previously used image automatically so the overlay survives restarts
                // without requiring the user to reload it manually.
                overlay.LoadImage(CEDGame.GraphicsDevice, _imagePath);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Failed to auto-load overlay image: {ex.Message}");
            }
        }
    }

    /// <summary>
    /// Copies the current live overlay state back into the persisted configuration object.
    /// </summary>
    private void SaveSettings()
    {
        var overlay = CEDGame.MapManager.ImageOverlay;

        Settings.ImagePath = _imagePath;
        Settings.Enabled = overlay.Enabled;
        Settings.DrawAboveTerrain = overlay.DrawAboveTerrain;
        Settings.WorldX = overlay.WorldX;
        Settings.WorldY = overlay.WorldY;
        Settings.Scale = overlay.Scale;
        Settings.Opacity = overlay.Opacity;
        Settings.Screen = overlay.Screen;
    }

    /// <summary>
    /// Draws the overlay file picker plus the controls that manipulate the live overlay transform
    /// and rendering behavior.
    /// </summary>
    protected override void InternalDraw()
    {
        var mapManager = CEDGame.MapManager;
        var overlay = mapManager.ImageOverlay;

        LoadSettings();

        if (ImGui.InputText(LangManager.Get(FILE_PATH), ref _imagePath, 512))
        {
            SaveSettings();
        }
        ImGui.SameLine();
        if (ImGui.Button("..."))
        {
            if (TinyFileDialogs.TryOpenFile(
                LangManager.Get(SELECT_FILE),
                Environment.CurrentDirectory,
                ["*.png", "*.jpg", "*.jpeg", "*.bmp"],
                "Image files",
                false,
                out var newPath))
            {
                // Choosing a file updates the persisted path immediately, but the texture is only
                // created when the user explicitly presses Load.
                _imagePath = newPath;
                SaveSettings();
            }
        }

        var hasTexture = overlay.Texture != null;

        // A path is required before the overlay can attempt to load an image file.
        ImGui.BeginDisabled(string.IsNullOrEmpty(_imagePath));
        if (ImGui.Button(LangManager.Get(IMAGE_OVERLAY_LOAD)))
        {
            try
            {
                overlay.LoadImage(CEDGame.GraphicsDevice, _imagePath);

                // Sync the editor fields back to the overlay in case the load path reset or
                // normalized any internal state.
                _position[0] = overlay.WorldX;
                _position[1] = overlay.WorldY;
                SaveSettings();
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Failed to load image: {ex.Message}");
            }
        }
        ImGui.EndDisabled();

        ImGui.SameLine();
        ImGui.BeginDisabled(!hasTexture);
        if (ImGui.Button(LangManager.Get(IMAGE_OVERLAY_UNLOAD)))
        {
            // Unloading releases the texture but keeps the configured path and transform settings.
            overlay.UnloadImage();
            SaveSettings();
        }
        ImGui.EndDisabled();

        ImGui.Separator();

        if (hasTexture)
        {
            // Both tile-space and raw pixel dimensions are shown so users can reason about how
            // the source image maps onto the world grid.
            ImGui.Text($"{LangManager.Get(IMAGE_OVERLAY_SIZE)}: {overlay.WidthInTiles:F1} x {overlay.HeightInTiles:F1}");
            ImGui.Text($"Image: {overlay.ImageWidth} x {overlay.ImageHeight} px");
        }
        else
        {
            ImGui.TextDisabled("No image loaded");
        }

        ImGui.Separator();

        var enabled = overlay.Enabled;
        if (ImGui.Checkbox(LangManager.Get(ENABLED), ref enabled))
        {
            overlay.Enabled = enabled;
            SaveSettings();
        }

        var drawAbove = overlay.DrawAboveTerrain;
        if (ImGui.Checkbox(LangManager.Get(IMAGE_OVERLAY_DRAW_ABOVE), ref drawAbove))
        {
            overlay.DrawAboveTerrain = drawAbove;
            SaveSettings();
        }

        // Refresh the temporary editor fields from the live overlay each frame so they remain in
        // sync with changes made elsewhere, such as centering the overlay on the current view.
        _position[0] = overlay.WorldX;
        _position[1] = overlay.WorldY;
        if (ImGui.InputInt2(LangManager.Get(IMAGE_OVERLAY_POSITION), ref _position[0]))
        {
            overlay.WorldX = _position[0];
            overlay.WorldY = _position[1];
            SaveSettings();
        }

        _scale = overlay.Scale;
        if (ImGui.SliderFloat(LangManager.Get(IMAGE_OVERLAY_SCALE), ref _scale, 0.1f, 10.0f, "%.2f"))
        {
            overlay.Scale = _scale;
            SaveSettings();
        }

        _opacity = overlay.Opacity;
        if (ImGui.SliderFloat(LangManager.Get(IMAGE_OVERLAY_OPACITY), ref _opacity, 0.0f, 1.0f, "%.2f"))
        {
            overlay.Opacity = _opacity;
            SaveSettings();
        }

        _screen = overlay.Screen;
        if (ImGui.SliderFloat(LangManager.Get(IMAGE_OVERLAY_SCREEN), ref _screen, 0.0f, 1.0f, "%.2f"))
        {
            overlay.Screen = _screen;
            SaveSettings();
        }

        if (ImGui.Button("Set to View Center"))
        {
            // Reposition the overlay to the current camera tile so reference images can be aligned
            // quickly against the portion of the map the user is already inspecting.
            var tilePos = mapManager.TilePosition;
            overlay.WorldX = tilePos.X;
            overlay.WorldY = tilePos.Y;
            SaveSettings();
        }
    }
}
