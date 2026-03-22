using System.Drawing;
using CentrED.Map;
using Hexa.NET.ImGui;
using static CentrED.Application;
using static CentrED.Constants;

namespace CentrED.UI.Windows;

/// <summary>
/// Developer-facing diagnostics window exposing runtime state, performance counters, and
/// debug views for map content that normally stays hidden from the main editor UI.
/// </summary>
public class DebugWindow : Window
{
    public override string Name => "Debug";
    public override ImGuiWindowFlags WindowFlags => ImGuiWindowFlags.None;

    private int _gotoX;
    private int _gotoY;

    /// <summary>
    /// Draws the tabbed debug surface.
    /// </summary>
    protected override void InternalDraw()
    {
        if (ImGui.BeginTabBar("DebugTabs"))
        {
            DrawGeneralTab();
            DrawPerformanceTab();
            DrawGhostTilesTab();
            ImGui.EndTabBar();
        }
    }

    /// <summary>
    /// Displays general runtime information and a small set of live editing controls for the
    /// camera and debug visualization flags.
    /// </summary>
    private void DrawGeneralTab()
    {
        if (ImGui.BeginTabItem("General"))
        {
            ImGui.Text($"FPS: {ImGui.GetIO().Framerate:F1}");
            var mapManager = CEDGame.MapManager;
            ImGui.Text
            (
                $"Resolution: {CEDGame.Window.ClientBounds.Width}x{CEDGame.Window.ClientBounds.Height}"
            );
            if (CEDClient.Running)
            {
                // These counters give a quick view into how much scene content is currently
                // loaded and being maintained by the renderer.
                ImGui.Text($"Land tiles: {mapManager.LandTilesCount}");
                ImGui.Text($"Static tiles: {mapManager.StaticsManager.Count}");
                ImGui.Text($"Animated Static tiles: {mapManager.StaticsManager.AnimatedTiles.Count}");
                ImGui.Text($"Light Tiles: {mapManager.StaticsManager.LightTiles.Count}");
                ImGui.Text($"Camera focus tile {mapManager.Camera.LookAt / TILE_SIZE}");
                var mousePos = ImGui.GetMousePos();

                // Unprojecting the mouse against the current virtual layer is useful when
                // debugging selection and depth-related issues in the map view.
                ImGui.Text
                (
                    $"Virutal Layer Pos: {mapManager.Unproject((int)mousePos.X, (int)mousePos.Y, mapManager.VirtualLayerZ)}"
                );
                ImGui.Separator();
                ImGui.Text("Camera");
                var x = mapManager.TilePosition.X;
                var y = mapManager.TilePosition.Y;

                // Camera position is edited in tile coordinates so it stays aligned with the
                // world grid instead of raw pixel space.
                var cameraMoved = ImGuiEx.DragInt("Position x", ref x, 1, 0, CEDClient.WidthInTiles - 1);
                cameraMoved |= ImGuiEx.DragInt("Position y", ref y, 1, 0, CEDClient.HeightInTiles - 1);
                if (cameraMoved)
                {
                    mapManager.TilePosition = new Point(x, y);
                }
                if (ImGui.SliderFloat("Zoom", ref mapManager.Camera.Zoom, 0.2f, 4.0f))
                {
                    // Clamp defensively so a slider edge case cannot leave the camera with an
                    // invalid near-zero zoom.
                    mapManager.Camera.Zoom = Math.Max(0.01f, mapManager.Camera.Zoom);
                }
                ImGui.NewLine();
                ImGui.SliderFloat("Yaw", ref mapManager.Camera.Yaw, -180.0f, 180.0f);
                ImGui.SliderFloat("Pitch", ref mapManager.Camera.Pitch, -180.0f, 180.0f);
                ImGui.SliderFloat("Roll", ref mapManager.Camera.Roll, -180.0f, 180.0f);
                ImGui.Separator();
                ImGui.Text("Misc");
                ImGui.Checkbox("Draw SelectionBuffer", ref CEDGame.MapManager.DebugDrawSelectionBuffer);
                ImGui.Checkbox("Draw LightMap", ref CEDGame.MapManager.DebugDrawLightMap);
            }
            ImGui.Checkbox("Debug Logging", ref CEDGame.MapManager.DebugLogging);
            ImGui.Checkbox("Debug Invalid Tiles", ref CEDGame.MapManager.DebugInvalidTiles);

            ImGui.Separator();

            // Shader reload is exposed here so rendering tweaks can be iterated without a full restart.
            if (ImGui.Button("Reload Shader"))
                mapManager.ReloadShader();
            ImGui.Separator();
            if (ImGui.Button("Test Window"))
                CEDGame.UIManager.ShowTestWindow = !CEDGame.UIManager.ShowTestWindow;
            ImGui.EndTabItem();
        }
    }

    /// <summary>
    /// Displays the collected metric timers used to profile frame stages.
    /// </summary>
    private void DrawPerformanceTab()
    {
        if (ImGui.BeginTabItem("Performance"))
        {
            ImGui.Text($"FPS: {ImGui.GetIO().Framerate:F1}");

            // Sorting keeps the timer list stable frame-to-frame, which makes it easier to scan.
            foreach (var nameValue in Metrics.Timers.OrderBy(t => t.Key))
            {
                ImGui.Text($"{nameValue.Key}: {nameValue.Value.TotalMilliseconds}ms");
            }
            ImGui.EndTabItem();
        }
    }

    /// <summary>
    /// Lists ghost tiles that exist in the transient editor state but are not committed as
    /// normal world tiles.
    /// </summary>
    private void DrawGhostTilesTab()
    {
        var count = CEDGame.MapManager.GhostLandTiles.Values.Count +
                    CEDGame.MapManager.StaticsManager.GhostTiles.Count();
        if (ImGui.BeginTabItem("GhostTiles"))
        {
            ImGui.Text($"Ghost Tiles: {count}");
            if (ImGui.BeginTable("GhostTilesTable", 2))
            {
                foreach (var landTile in CEDGame.MapManager.GhostLandTiles.Values)
                {
                    DrawLand(landTile);
                }
                foreach (var staticTile in CEDGame.MapManager.StaticsManager.GhostTiles)
                {
                    DrawStatic(staticTile);
                }
                ImGui.EndTable();
            }
            ImGui.EndTabItem();
        }
    }
    
    /// <summary>
    /// Draws a land ghost tile preview and its metadata row.
    /// </summary>
    private void DrawLand(LandObject lo)
    {
        var landTile = lo.LandTile;
        ImGui.TableNextRow();
        if (ImGui.TableNextColumn())
        {
            var spriteInfo = CEDGame.MapManager.Arts.GetLand(landTile.Id);
            CEDGame.UIManager.DrawImage(spriteInfo.Texture, spriteInfo.UV);
        }
        if (ImGui.TableNextColumn())
        {
            // TileData names are surfaced here so ghost tiles can be identified without
            // manually decoding the numeric id.
            ImGui.Text("Land " + CEDGame.MapManager.UoFileManager.TileData.LandData[landTile.Id].Name ?? "");
            ImGui.Text($"x:{landTile.X} y:{landTile.Y} z:{landTile.Z}");
            ImGui.Text($"id: {landTile.Id.FormatId()}");
        }
    }

    /// <summary>
    /// Draws a static ghost tile preview and its metadata row.
    /// </summary>
    private void DrawStatic(StaticObject so)
    {
        var staticTile = so.StaticTile;
        ImGui.TableNextRow();
        if (ImGui.TableNextColumn())
        {
            var spriteInfo = CEDGame.MapManager.Arts.GetArt(staticTile.Id);
            var realBounds = CEDGame.MapManager.Arts.GetRealArtBounds(staticTile.Id);

            // Static art often includes transparent padding, so the preview crops to the real
            // sprite bounds instead of drawing the full atlas rectangle.
            CEDGame.UIManager.DrawImage
            (
                spriteInfo.Texture,
                new Rectangle(spriteInfo.UV.X + realBounds.X, spriteInfo.UV.Y + realBounds.Y, realBounds.Width, realBounds.Height)
            );
        }
        if (ImGui.TableNextColumn())
        {
            ImGui.Text("Static " + CEDGame.MapManager.UoFileManager.TileData.StaticData[staticTile.Id].Name);
            ImGui.Text($"x:{staticTile.X} y:{staticTile.Y} z:{staticTile.Z}");
            ImGui.Text($"id: {staticTile.Id.FormatId()} hue: {staticTile.Hue.FormatId()}");
        }
    }
}