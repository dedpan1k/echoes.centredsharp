using CentrED.IO.Models;
using Hexa.NET.ImGui;
using static CentrED.LangEntry;

namespace CentrED.UI.Windows;

/// <summary>
/// Presents the map export controls used to configure output size, zoom, and destination
/// path before requesting a screenshot-style export from the map manager.
/// </summary>
public class ExportWindow : Window
{
    /// <summary>
    /// Stable ImGui title/ID pair for the export window.
    /// </summary>
    public override string Name => LangManager.Get(EXPORT_WINDOW) + "###Export";

    /// <summary>
    /// The export dialog stays hidden by default until the user explicitly opens it from the UI.
    /// </summary>
    public override WindowState DefaultState => new()
    {
        IsOpen = false
    };

    /// <summary>
    /// Draws the export configuration form and validates the destination file extension before
    /// allowing the export request to be queued.
    /// </summary>
    protected override void InternalDraw()
    {
        ImGui.Text(LangManager.Get(RESOLUTION_QUICK_SELECT));
        var mapManager = Application.CEDGame.MapManager;
        if (ImGui.Button("4K"))
        {
            // Quick presets cover the most common target resolutions without forcing the user
            // to type the dimensions manually every time.
            mapManager.ExportWidth = 3840;
            mapManager.ExportHeight = 2160;
        }
        ImGui.SameLine();
        if (ImGui.Button("8K"))
        {
            mapManager.ExportWidth = 7680;
            mapManager.ExportHeight = 4320;
        }
        ImGui.SameLine();
        if (ImGui.Button("16K"))
        {
            mapManager.ExportWidth = 15360;
            mapManager.ExportHeight = 8640;
        }
        ImGui.InputInt(LangManager.Get(WIDTH), ref mapManager.ExportWidth);
        ImGui.InputInt(LangManager.Get(HEIGHT), ref mapManager.ExportHeight);
        ImGui.SliderFloat(LangManager.Get(ZOOM), ref mapManager.ExportZoom, 0.2f, 1.0f);
        ImGui.Separator();
        ImGui.InputText(LangManager.Get(FILE_PATH), ref mapManager.ExportPath, 1024);
        ImGuiEx.Tooltip(LangManager.Get(EXPORT_FILE_TOOLTIP));
        var path = mapManager.ExportPath;

        // Export currently supports the image formats handled by the map export pipeline, so
        // the button is gated on a matching file extension.
        var validPath = path.EndsWith(".png") || path.EndsWith(".jpg");
        ImGui.BeginDisabled(!validPath);
        if (ImGui.Button(LangManager.Get(EXPORT)))
        {
            // The actual export work is picked up elsewhere; this window only raises the flag
            // once the requested dimensions and path have been configured.
            mapManager.Export = true;
        }
        if (!validPath)
        {
            ImGui.SameLine();
            ImGui.Text(LangManager.Get(UNKNOWN_FILE_FORMAT));
        }
        ImGui.EndDisabled();
    }
}