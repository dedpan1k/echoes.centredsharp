using System.Drawing;
using CentrED.Client;
using CentrED.IO;
using Hexa.NET.ImGui;
using Microsoft.Xna.Framework.Input;
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
    private const float MinZoom = 0.5f;
    private const float MaxZoom = 6f;
    private const float ZoomStep = 0.25f;

    /// <summary>
    /// Stable ImGui title/ID pair for the minimap window.
    /// </summary>
    public override string Name => LangManager.Get(MINIMAP_WINDOW) + "###Minimap";
    public override string Shortcut => Keys.M.ToString();

    private string _inputFavoriteName = "";
    private string _favoriteToDelete = "";
    private float _zoom = 1f;

    // Temporary x/y buffer reused by the coordinate input and the hover readout over the radar map.
    private int[] mapPos = new int[2];
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
            var tex = RadarMap.Instance.Texture;
            var drawSize = new Vector2(tex.Width * _zoom, tex.Height * _zoom);
            var scrollX = ImGui.GetScrollX();
            var scrollY = ImGui.GetScrollY();

            // Draw the radar image first, then layer interaction and overlays on top of the same bounds.
            CEDGame.UIManager.DrawImage(tex, tex.Bounds, drawSize, true);

            var currentPos = ImGui.GetItemRectMin();
            var currentSize = ImGui.GetItemRectSize();
            var viewportOrigin = currentPos + new Vector2(scrollX, scrollY);
            var viewportSize = new Vector2
            (
                MathF.Max(0f, ImGui.GetWindowWidth() - ImGui.GetStyle().WindowPadding.X * 2f),
                MathF.Max(0f, ImGui.GetWindowHeight() - ImGui.GetStyle().WindowPadding.Y * 2f - (ImGui.GetScrollMaxX() > 0 ? ImGui.GetStyle().ScrollbarSize : 0f))
            );

            ImGui.SetCursorScreenPos(currentPos);

            // A full-size invisible button turns the image region into a normal ImGui item so hover,
            // active-state, and popup handling all work without custom hit testing.
            ImGui.InvisibleButton("MinimapInvButton", currentSize);
            var hovered = ImGui.IsItemHovered();
            var held = ImGui.IsItemActive();
            var io = ImGui.GetIO();

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

            if (hovered && io.MouseWheel != 0)
            {
                var keyboard = Keyboard.GetState();
                var shiftDown = keyboard.IsKeyDown(Keys.LeftShift) || keyboard.IsKeyDown(Keys.RightShift);
                var altDown = keyboard.IsKeyDown(Keys.LeftAlt) || keyboard.IsKeyDown(Keys.RightAlt);

                if (altDown)
                {
                    var previousZoom = _zoom;
                    _zoom = Math.Clamp(_zoom + io.MouseWheel * ZoomStep, MinZoom, MaxZoom);

                    if (_zoom != previousZoom)
                    {
                        var mouseViewportPos = ImGui.GetMousePos() - viewportOrigin;
                        var imagePosUnderMouse = ImGui.GetMousePos() - currentPos;
                        var textureX = imagePosUnderMouse.X / previousZoom;
                        var textureY = imagePosUnderMouse.Y / previousZoom;
                        var maxScrollX = Math.Max(0f, tex.Width * _zoom - viewportSize.X);
                        var maxScrollY = Math.Max(0f, tex.Height * _zoom - viewportSize.Y);

                        ImGui.SetScrollX(Math.Clamp(textureX * _zoom - mouseViewportPos.X, 0f, maxScrollX));
                        ImGui.SetScrollY(Math.Clamp(textureY * _zoom - mouseViewportPos.Y, 0f, maxScrollY));
                    }

                    io.MouseWheel = 0;
                }
                else if (shiftDown)
                {
                    var horizontalStep = ImGui.GetFontSize() * 8f;
                    var targetScrollX = ImGui.GetScrollX() - io.MouseWheel * horizontalStep;
                    ImGui.SetScrollX(Math.Clamp(targetScrollX, 0f, ImGui.GetScrollMaxX()));
                    io.MouseWheel = 0;
                }
            }

            if (hovered)
            {
                // Radar pixels map to world tiles at an 8:1 scale in both directions.
                var newPos = (ImGui.GetMousePos() - currentPos) / _zoom * 8;
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
            var p1 = currentPos + new Vector2(rect.X1 / 8f * _zoom, center.Y / 8f * _zoom);
            var p2 = currentPos + new Vector2(center.X / 8f * _zoom, rect.Y1 / 8f * _zoom);
            var p3 = currentPos + new Vector2(rect.X2 / 8f * _zoom, center.Y / 8f * _zoom);
            var p4 = currentPos + new Vector2(center.X / 8f * _zoom, rect.Y2 / 8f * _zoom);

            // The current camera view is shown as a diamond because the world itself is rendered isometrically.
            ImGui.GetWindowDrawList().AddQuad(p1, p2, p3, p4, ImGui.GetColorU32(ImGuiColor.Red));

            // Other tools reuse the minimap to visualize their active areas, so their overlays are drawn here too.
            CEDGame.UIManager.GetWindow<LSOWindow>().DrawArea(currentPos);
            CEDGame.UIManager.GetWindow<ServerAdminWindow>().DrawArea(currentPos);
        }
        ImGui.EndChild();
    }
}