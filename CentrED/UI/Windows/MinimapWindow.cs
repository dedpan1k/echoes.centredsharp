using System.Drawing;
using CentrED.Client;
using CentrED.IO;
using Hexa.NET.ImGui;
using static CentrED.Application;
using static CentrED.LangEntry;
using RadarMap = CentrED.Map.RadarMap;
using Vector2 = System.Numerics.Vector2;
using Vector4 = System.Numerics.Vector4;

namespace CentrED.UI.Windows;

/// <summary>
/// Displays the radar-map minimap, exposes favorite locations, and lets the user pan the main
/// map view by interacting directly with the scaled radar texture.
/// </summary>
public class MinimapWindow : Window
{
    /// <summary>
    /// Stable ImGui title/ID pair for the minimap window.
    /// </summary>
    public override string Name => LangManager.Get(MINIMAP_WINDOW) + "###Minimap";

    private string _inputFavoriteName = "";
    private string _favoriteToDelete = "";

    // Temporary x/y buffer reused by the coordinate input and the hover readout over the radar map.
    private int[] mapPos = new int[2];
    private bool _showError = true;
    private bool _showConfirmation = true;

    /// <summary>
    /// Draws the favorites strip, coordinate input, and interactive minimap surface.
    /// </summary>
    protected override void InternalDraw()
    {
        if (!CEDClient.Running)
        {
            ImGui.Text(LangManager.Get(NOT_CONNECTED));
            return;
        }
        ImGui.Text(LangManager.Get(FAVORITES));
        if (ImGui.BeginChild("Favorites", new Vector2(RadarMap.Instance.Texture.Width, 100)))
        {
            ImGui.InputText(LangManager.Get(NAME), ref _inputFavoriteName, 64);
            ImGui.SameLine();
            ImGui.BeginDisabled(string.IsNullOrEmpty(_inputFavoriteName) || 
                                ProfileManager.ActiveProfile.RadarFavorites.ContainsKey(_inputFavoriteName));
            if (ImGui.Button(LangManager.Get(ADD)))
            {
                // Favorites capture the current camera tile so common work areas can be revisited quickly.
                ProfileManager.ActiveProfile.RadarFavorites.Add
                (
                    _inputFavoriteName,
                    new()
                    {
                        X = (ushort)CEDGame.MapManager.TilePosition.X,
                        Y = (ushort)CEDGame.MapManager.TilePosition.Y
                    }
                );
                ProfileManager.Save();
                _inputFavoriteName = "";
            }
            ImGui.EndDisabled();
            
            foreach (var (name, coords) in ProfileManager.ActiveProfile.RadarFavorites)
            {
                if (name != ProfileManager.ActiveProfile.RadarFavorites.First().Key)
                {
                    ImGui.SameLine();
                }
                if (ImGui.GetCursorPos().X + 75 >= RadarMap.Instance.Texture.Width)
                {
                    ImGui.NewLine();
                }

                var cursorPosition = ImGui.GetCursorPos();

                // Each favorite acts like a teleport target for the main map camera.
                if (ImGui.Button($"{name}", new Vector2(75, 19)))
                {
                    CEDGame.MapManager.TilePosition = new Point(coords.X, coords.Y);
                }
                ImGuiEx.Tooltip($"X:{coords.X} Y:{coords.Y}");

                ImGui.SetCursorPos(cursorPosition + new Vector2(ImGui.GetItemRectSize().X, 0));

                ImGui.PushStyleColor(ImGuiCol.Button, new Vector4(1, 0, 0, .2f));
                ImGui.PushStyleColor(ImGuiCol.ButtonHovered, new Vector4(1, 0, 0, 1));
                if (ImGui.Button($"x##{name}"))
                {
                    _favoriteToDelete = name;
                    ImGui.OpenPopup("DeleteFavorite");
                    _showConfirmation = true;
                }
                ImGui.PopStyleColor(2);
            }

                // Deletion is confirmed in a modal so accidental favorite removal is reversible.
            if (ImGui.BeginPopupModal
                (
                    "DeleteFavorite",
                    ref _showConfirmation,
                    ImGuiWindowFlags.AlwaysAutoResize | ImGuiWindowFlags.NoTitleBar
                ))
            {
                ImGui.Text(string.Format(LangManager.Get(DELETE_WARNING_1TYPE_2NAME), LangManager.Get(FAVORITE).ToLower(), _favoriteToDelete));
                if (ImGui.Button(LangManager.Get(YES)))
                {
                    if (!string.IsNullOrEmpty(_favoriteToDelete))
                    {
                        ProfileManager.ActiveProfile.RadarFavorites.Remove(_favoriteToDelete);
                        ProfileManager.Save();
                    }
                    _favoriteToDelete = "";
                    ImGui.CloseCurrentPopup();
                }
                ImGui.SameLine();
                if (ImGui.Button(LangManager.Get(NO)))
                {
                    _favoriteToDelete = "";
                    ImGui.CloseCurrentPopup();
                }
                ImGui.EndPopup();
            }
        }
        ImGui.EndChild();
        ImGui.Separator();
        ImGui.PushItemWidth(100);
        if (ImGui.InputInt2("X/Y", ref mapPos[0]))
        {
            // The coordinate boxes are a second navigation path alongside clicking the minimap.
            CEDGame.MapManager.TilePosition = new Point(mapPos[0], mapPos[1]);
        };
        ImGui.PopItemWidth();
        if (ImGui.BeginChild("Minimap", Vector2.Zero, ImGuiChildFlags.None, ImGuiWindowFlags.HorizontalScrollbar))
        {
            var currentPos = ImGui.GetCursorScreenPos();
            var tex = RadarMap.Instance.Texture;

            // Draw the radar image first, then layer interaction and overlays on top of the same bounds.
            CEDGame.UIManager.DrawImage(tex, tex.Bounds);

            ImGui.SetCursorScreenPos(currentPos);

            // A full-size invisible button turns the image region into a normal ImGui item so hover,
            // active-state, and popup handling all work without custom hit testing.
            ImGui.InvisibleButton("MinimapInvButton", new Vector2(tex.Width, tex.Height));
            var hovered = ImGui.IsItemHovered();
            var held = ImGui.IsItemActive();

            if (ImGui.BeginPopupContextItem())
            {
                // The radar map can be refreshed on demand when the cached image is stale.
                if (ImGui.Button(LangManager.Get(REFRESH)))
                {
                    CEDClient.Send(new RequestRadarMapPacket());
                    ImGui.CloseCurrentPopup();
                }
                ImGui.EndPopup();
            }

            if (hovered)
            {
                // Radar pixels map to world tiles at an 8:1 scale in both directions.
                var newPos = (ImGui.GetMousePos() - currentPos) * 8;
                if (held)
                {
                    // Click-dragging across the minimap repositions the main camera continuously.
                    CEDGame.MapManager.TilePosition = new Point((int)newPos.X, (int)newPos.Y);
                }
                mapPos[0] = (int)newPos.X;
                mapPos[1] = (int)newPos.Y;
            }
            else
            {
                mapPos[0] = CEDGame.MapManager.TilePosition.X;
                mapPos[1] = CEDGame.MapManager.TilePosition.Y;
            }

            var rect = CEDGame.MapManager.ViewRange;
            var center = new Point(rect.X1 + rect.Width / 2, rect.Y1 + rect.Height / 2);
            var p1 = currentPos + new Vector2(rect.X1 / 8, center.Y / 8);
            var p2 = currentPos + new Vector2(center.X / 8, rect.Y1 / 8);
            var p3 = currentPos + new Vector2(rect.X2 / 8, center.Y / 8);
            var p4 = currentPos + new Vector2(center.X / 8, rect.Y2 / 8);

            // The current camera view is shown as a diamond because the world itself is rendered isometrically.
            ImGui.GetWindowDrawList().AddQuad(p1, p2, p3, p4, ImGui.GetColorU32(ImGuiColor.Red));

            // Other tools reuse the minimap to visualize their active areas, so their overlays are drawn here too.
            CEDGame.UIManager.GetWindow<LSOWindow>().DrawArea(currentPos);
            CEDGame.UIManager.GetWindow<ServerAdminWindow>().DrawArea(currentPos);
        }
        ImGui.EndChild();
    }
}