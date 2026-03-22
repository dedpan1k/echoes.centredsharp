using CentrED.IO.Models;
using Hexa.NET.ImGui;
using static CentrED.Application;
using static CentrED.LangEntry;
using Vector2 = System.Numerics.Vector2;
using CentrED.IO;


namespace CentrED.UI.Windows;

/// <summary>
/// Provides the runtime filtering controls for map rendering, including global visibility
/// toggles, Z-range clipping, and inclusion/exclusion lists for object ids and hues.
/// </summary>
public class FilterWindow : Window
{
    /// <summary>
    /// Hooks connection lifecycle events so the per-profile static filter can be restored on
    /// connect and persisted again on disconnect.
    /// </summary>
    public FilterWindow()
    {
        CEDClient.Connected += OnConnected;
        CEDClient.Disconnected += OnDisconnected;
    }
    
    /// <summary>
    /// Stable ImGui title/ID pair for the filter window.
    /// </summary>
    public override string Name => LangManager.Get(FILTER_WINDOW) + "###Filter";

    /// <summary>
    /// Filtering controls are part of the default workspace layout and start visible.
    /// </summary>
    public override WindowState DefaultState => new()
    {
        IsOpen = true
    };

    /// <summary>
    /// Tile preview width/height reused by the object-id filter table.
    /// </summary>
    private Vector2 StaticDimensions => TilesWindow.TilesDimensions;

    // These sets live on the map manager because they directly affect world rendering.
    private SortedSet<int> ObjectIdFilter => CEDGame.MapManager.ObjectIdFilter;
    private SortedSet<int> ObjectHueFilter => CEDGame.MapManager.ObjectHueFilter;

    // Related windows provide the row rendering helpers used inside the filter tables.
    private TilesWindow _tilesWindow => CEDGame.UIManager.GetWindow<TilesWindow>(); 
    private HuesWindow _huesWindow => CEDGame.UIManager.GetWindow<HuesWindow>(); 

    /// <summary>
    /// Persists the current static-id filter back into the active profile when the session ends.
    /// </summary>
    private static void OnDisconnected()
    {
        ProfileManager.ActiveProfile.StaticFilter = CEDGame.MapManager.ObjectIdFilter.ToList();
        ProfileManager.SaveStaticFilter();
    }

    /// <summary>
    /// Restores the active profile's saved static-id filter into the live map manager.
    /// </summary>
    private static void OnConnected()
    {
        CEDGame.MapManager.ObjectIdFilter = new SortedSet<int>(ProfileManager.ActiveProfile.StaticFilter);
    }

    /// <summary>
    /// Draws the filter UI, including Z clipping, global render toggles, and list-based object
    /// and hue filters.
    /// </summary>
    protected override void InternalDraw()
    {
        if (!CEDClient.Running)
        {
            ImGui.Text(LangManager.Get(NOT_CONNECTED));
            return;
        }
        ImGui.SetCursorPosX(ImGui.GetCursorPosX() + 8);
        ImGui.BeginGroup();
        if (ImGuiEx.DragInt(LangManager.Get(MAX) + " Z", ref CEDGame.MapManager.MaxZ, 1, CEDGame.MapManager.MinZ, 127))
        {
            // The light cache depends on the visible Z slice, so moving the clipping plane
            // requires recomputing the currently active lights.
            CEDGame.MapManager.UpdateLights();
        }
        if (ImGuiEx.DragInt(LangManager.Get(MIN) + " Z", ref CEDGame.MapManager.MinZ, 1, -128, CEDGame.MapManager.MaxZ))
        {
            CEDGame.MapManager.UpdateLights();
        }
        ImGui.EndGroup();
        ImGui.Text(LangManager.Get(GLOBAL_FILTER));
        ImGui.Checkbox(LangManager.Get(LAND), ref CEDGame.MapManager.ShowLand);
        ImGui.SameLine();
        ImGui.Checkbox(LangManager.Get(OBJECTS), ref CEDGame.MapManager.ShowStatics);
        ImGui.SameLine();
        ImGui.Checkbox(LangManager.Get(NODRAW), ref CEDGame.MapManager.ShowNoDraw);
        if (ImGui.BeginChild("Filters"))
        {
            if (ImGui.BeginTabBar("FiltersTabs"))
            {
                if (ImGui.BeginTabItem(LangManager.Get(OBJECTS)))
                {
                    // "Reversed" maps directly to the inclusive/exclusive behavior exposed by
                    // the map manager's object-id filter implementation.
                    ImGui.Checkbox(LangManager.Get(ENABLED), ref CEDGame.MapManager.ObjectIdFilterEnabled);
                    ImGui.Checkbox(LangManager.Get(REVERSED), ref CEDGame.MapManager.ObjectIdFilterInclusive);
                    if (ImGui.Button(LangManager.Get(CLEAR)))
                    {
                        ObjectIdFilter.Clear();
                    }
                    if (ImGui.BeginChild("TilesTable"))
                    {
                        if (ImGui.BeginTable("TilesTable", 3))
                        {
                            var tileToRemove = -1;
                            var clipper = ImGui.ImGuiListClipper();
                            ImGui.TableSetupColumn("Id", ImGuiTableColumnFlags.WidthFixed, ImGui.CalcTextSize(0xFFFF.FormatId()).X);
                            ImGui.TableSetupColumn("Graphic", ImGuiTableColumnFlags.WidthFixed, StaticDimensions.X);

                            // The filter list can grow large, so clip rendering to only the
                            // visible rows while preserving stable ordering from the sorted set.
                            clipper.Begin(ObjectIdFilter.Count);
                            while (clipper.Step())
                            {
                                for (var i = clipper.DisplayStart; i < clipper.DisplayEnd; i++)
                                {
                                    var tileIndex = ObjectIdFilter.ElementAt(i);
                                    var tileInfo = _tilesWindow.GetObjectInfo(tileIndex);
                                    _tilesWindow.DrawTileRow(i, (ushort)tileIndex, tileInfo);
                                    if (ImGui.BeginPopupContextItem())
                                    {
                                        if (ImGui.Button(LangManager.Get(REMOVE)))
                                        {
                                            tileToRemove = tileIndex;
                                            ImGui.CloseCurrentPopup();
                                        }
                                        ImGui.EndPopup();
                                    }
                                }
                            }
                            if (tileToRemove != -1)
                                ObjectIdFilter.Remove(tileToRemove);
                            ImGui.EndTable();
                        }
                    }
                    ImGui.EndChild();
                    if (ImGuiEx.DragDropTarget(TilesWindow.OBJECT_DRAG_DROP_TYPE, out var ids))
                    {
                        // Dragging from the tiles window is the fast path for building an
                        // object filter without manually typing ids.
                        foreach (var id in ids)
                        {
                            ObjectIdFilter.Add(id);
                        }
                    }
                    ImGui.EndTabItem();
                }
                if (ImGui.BeginTabItem(LangManager.Get(HUES)))
                {
                    ImGui.Checkbox(LangManager.Get(ENABLED), ref CEDGame.MapManager.ObjectHueFilterEnabled);
                    ImGui.Checkbox(LangManager.Get(REVERSED), ref CEDGame.MapManager.ObjectHueFilterInclusive);
                    if (ImGui.Button(LangManager.Get(CLEAR)))
                    {
                        ObjectHueFilter.Clear();
                    }
                    if (ImGui.BeginChild("HuesTable"))
                    {
                        if (ImGui.BeginTable("HuesTable", 2))
                        {
                            var hueToRemove = -1;
                            var clipper = ImGui.ImGuiListClipper();
                            var textSize = ImGui.CalcTextSize(0xFFFF.FormatId());
                            var columnHeight = textSize.Y;
                            ImGui.TableSetupColumn("Id", ImGuiTableColumnFlags.WidthFixed, textSize.X);

                            // Hue rows are also virtualized because profiles may accumulate a
                            // sizable saved filter over time.
                            clipper.Begin(ObjectHueFilter.Count);
                            while (clipper.Step())
                            {
                                for (var rowIndex = clipper.DisplayStart; rowIndex < clipper.DisplayEnd; rowIndex++)
                                {
                                    var hueIndex = ObjectHueFilter.ElementAt(rowIndex);
                                    _huesWindow.DrawHueRow(rowIndex, (ushort)hueIndex, columnHeight);
                                    if (ImGui.BeginPopupContextItem())
                                    {
                                        if (ImGui.Button(LangManager.Get(REMOVE)))
                                        {
                                            hueToRemove = hueIndex;
                                            ImGui.CloseCurrentPopup();
                                        }
                                        ImGui.EndPopup();
                                    }
                                }
                            }
                            if(hueToRemove != -1)
                                ObjectHueFilter.Remove(hueToRemove);
                            ImGui.EndTable();
                        }
                    }
                    ImGui.EndChild();

                    if (ImGuiEx.DragDropTarget(HuesWindow.Hue_DragDrop_Target_Type, out var ids))
                    {
                        // Hues can be added directly from the hue browser through drag and drop.
                        foreach (var id in ids)
                        {
                            ObjectHueFilter.Add(id);
                        }
                    }
                    ImGui.EndTabItem();
                }
                ImGui.EndTabBar();
            }
        }
        ImGui.EndChild();
    }
}