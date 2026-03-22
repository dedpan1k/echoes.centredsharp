using CentrED.IO;
using CentrED.IO.Models;
using CentrED.Map;
using Hexa.NET.ImGui;
using static CentrED.Application;
using static CentrED.LangEntry;
using Vector2 = System.Numerics.Vector2;
using Rectangle = System.Drawing.Rectangle;

namespace CentrED.UI.Windows;

/// <summary>
/// Browses available hues, supports multi-selection and drag-and-drop, and manages named
/// hue sets stored in the active profile.
/// </summary>
public class HuesWindow : Window
{
    /// <summary>
    /// Subscribes the hue browser to connection events and initializes the profile-backed hue
    /// set state used by the lower half of the window.
    /// </summary>
    public HuesWindow()
    {
        CEDClient.Connected += FilterHues;
        UpdateHueSetNames();
        UpdateHueSetValues();
    }

    /// <summary>
    /// Stable ImGui title/ID pair for the hue browser.
    /// </summary>
    public override string Name => LangManager.Get(HUES_WINDOW) + "###Hues";

    /// <summary>
    /// The hue browser is part of the default workspace layout and starts open.
    /// </summary>
    public override WindowState DefaultState => new()
    {
        IsOpen = true
    };

    // Set when the next draw should scroll the list to the most recently selected hue.
    public bool UpdateScroll;
    private string _filter = "";

    private ushort _lastSelectedId;

    // Multi-select state is shared between the main hue list and the active hue-set list.
    private MultiSelectStorage<ushort> _selection = new([0]);

    /// <summary>
    /// Exposes the current hue selection for drag-and-drop and consumers such as filter tools.
    /// </summary>
    public ICollection<ushort> SelectedIds => _selection.Items;

    // Cached result set for the current text filter.
    private List<ushort> _matchedHueIds = [];

    /// <summary>
    /// Drag-drop payload type used when moving hues into hue sets or other consumers.
    /// </summary>
    public const string Hue_DragDrop_Target_Type = "HueDragDrop";
    
    /// <summary>
    /// Rebuilds the visible hue list from the active text filter.
    /// Matching supports hue names plus both hexadecimal and decimal identifier forms.
    /// </summary>
    private void FilterHues()
    {
        _matchedHueIds.Clear();
        var huesManager = HuesManager.Instance;
        if (_filter.Length == 0)
        {
            for (var i = 0; i < huesManager.HuesCount; i++)
            {
                _matchedHueIds.Add((ushort)i);
            }
        }
        else
        {
            for (var i = 0; i < huesManager.HuesCount; i++)
            {
                var name = huesManager.Names[i];
                if (
                    name.Contains(_filter, StringComparison.InvariantCultureIgnoreCase) || 
                    i.FormatId(NumberDisplayFormat.HEX).Contains(_filter, StringComparison.InvariantCultureIgnoreCase) || 
                    i.FormatId(NumberDisplayFormat.DEC).Contains(_filter, StringComparison.InvariantCultureIgnoreCase)
                    )
                    _matchedHueIds.Add((ushort)i);
            }
        }
    }

    /// <summary>
    /// Draws the main hue browser and the hue-set management area.
    /// </summary>
    protected override void InternalDraw()
    {
        if (!CEDClient.Running)
        {
            ImGui.Text(LangManager.Get(NOT_CONNECTED));
            return;
        }
        if (ImGui.Button(LangManager.Get(SCROLL_TO_SELECTED)))
        {
            UpdateScroll = true;
        }

        ImGui.Text(LangManager.Get(FILTER));
        if (ImGui.InputText("##Filter", ref _filter, 64))
        {
            // Filter results are rebuilt immediately so the list always reflects the current text.
            FilterHues();
        }
        ImGui.SetNextWindowSizeConstraints(ImGuiEx.MIN_SIZE, ImGui.GetContentRegionAvail() - ImGuiEx.MIN_HEIGHT);
        DrawHues();
        DrawHueSets();
    }

    /// <summary>
    /// Draws the scrollable hue browser with multi-selection, previews, and context actions.
    /// </summary>
    private void DrawHues()
    {
        if (ImGui.BeginChild("Hues", ImGuiChildFlags.Borders | ImGuiChildFlags.ResizeY))
        {
            if (ImGui.BeginTable("HuesTable", 2))
            {
                var clipper = ImGui.ImGuiListClipper();
                var textSize = ImGui.CalcTextSize(0xFFFF.FormatId());
                var columnHeight = textSize.Y;
                ImGui.TableSetupColumn("Id", ImGuiTableColumnFlags.WidthFixed, textSize.X);
                clipper.Begin(_matchedHueIds.Count);

                // Selection is tracked by row index, so the storage needs the current filtered
                // id list before the clipper starts emitting visible ranges.
                _selection.Begin(_matchedHueIds, clipper, ImGuiMultiSelectFlags.BoxSelect1D);
                while (clipper.Step())
                {
                    for (var rowIndex = clipper.DisplayStart; rowIndex < clipper.DisplayEnd; rowIndex++)
                    {
                        var hueIndex = _matchedHueIds[rowIndex];
                        DrawHueRow(rowIndex, hueIndex, columnHeight);
                        ImGuiEx.DragDropSource(Hue_DragDrop_Target_Type, _selection.Items);
                        if (ImGui.BeginPopupContextItem())
                        {
                            if (ImGui.Button(LangManager.Get(ADD_TO_SET)))
                            {
                                AddToHueSet(hueIndex);
                                ImGui.CloseCurrentPopup();
                            }
                            ImGui.EndPopup();
                        }
                    }
                }
                _selection.End();
                if (UpdateScroll)
                {
                    // Scroll against the clipper's start position so the selected hue can be
                    // brought into view without forcing every row to render.
                    var itemPosY = (float)clipper.StartPosY + clipper.ItemsHeight * _matchedHueIds.IndexOf(_lastSelectedId);
                    ImGui.SetScrollFromPosY(itemPosY - ImGui.GetWindowPos().Y);
                    UpdateScroll = false;
                }

                ImGui.EndTable();
            }
        }
        ImGui.EndChild();
    }

    private int _hueSetIndex;
    private string HueSetName => _hueSetNames[_hueSetIndex];
    private string _hueSetNewName = "";

    // Index 0 is a temporary working set that exists only for the current session.
    private SortedSet<ushort> _tempHueSetValues = [];

    /// <summary>
    /// Cached values for the currently selected hue set.
    /// </summary>
    public List<ushort> ActiveHueSetValues = [];

    private int _hueSetRemoveIndex = -1;

    // Named hue sets are persisted on the active profile.
    private static Dictionary<string, SortedSet<ushort>> HueSets => ProfileManager.ActiveProfile.HueSets;
    private string[] _hueSetNames = HueSets.Keys.ToArray();

    /// <summary>
    /// Draws the hue-set management UI, including set selection, CRUD actions, and the active
    /// set contents table.
    /// </summary>
    private void DrawHueSets()
    {
        if (ImGui.BeginChild("HueSets"))
        {
            ImGui.Text(LangManager.Get(HUE_SET));
            if (ImGui.Button(LangManager.Get(NEW)))
            {
                ImGui.OpenPopup("NewHueSet");
            }
            ImGui.SameLine();
            ImGui.BeginDisabled(_hueSetIndex == 0);
            if (ImGui.Button(LangManager.Get(DELETE)))
            {
                ImGui.OpenPopup("DeleteHueSet");
            }
            ImGui.EndDisabled();
            ImGui.SameLine();
            ImGui.BeginDisabled(ActiveHueSetValues.Count == 0);
            if (ImGui.Button(LangManager.Get(CLEAR)))
            {
                ClearHueSet();
            }
            ImGui.EndDisabled();
            if (ImGui.Combo("##HueSetCombo", ref _hueSetIndex, _hueSetNames, _hueSetNames.Length))
            {
                    // Switching sets refreshes the cached contents shown in the lower table.
               UpdateHueSetValues();
            }
            if (ImGui.BeginChild("HueSetTable"))
            {
                if (ImGui.BeginTable("HueSetTable", 2) && CEDClient.Running)
                {
                    var clipper = ImGui.ImGuiListClipper();
                    var textSize = ImGui.CalcTextSize(0xFFFF.FormatId());
                    var columnHeight = textSize.Y;
                    ImGui.TableSetupColumn("Id", ImGuiTableColumnFlags.WidthFixed, textSize.X);
                    var ids = ActiveHueSetValues; //We copy the array here to not crash when removing, please fix :)
                    clipper.Begin(ids.Count);
                    _selection.Begin(ids, clipper, ImGuiMultiSelectFlags.BoxSelect1D);
                    while (clipper.Step())
                    {
                        for (var rowIndex = clipper.DisplayStart; rowIndex < clipper.DisplayEnd; rowIndex++)
                        {
                            var hueIndex = ids[rowIndex];
                            DrawHueRow(rowIndex, hueIndex, columnHeight);
                            if (ImGui.BeginPopupContextItem())
                            {
                                if (ImGui.Button(LangManager.Get(REMOVE)))
                                {
                                    // Removal is deferred until after iteration so the list is
                                    // not mutated while the table is still walking it.
                                    _hueSetRemoveIndex = hueIndex;
                                    ImGui.CloseCurrentPopup();
                                }
                                ImGui.EndPopup();
                            }
                        }
                    }
                    _selection.End();
                    ImGui.EndTable();
                }
                if (_hueSetRemoveIndex != -1)
                {
                    RemoveFromHueSet((ushort)_hueSetRemoveIndex);
                    _hueSetRemoveIndex = -1;
                }
            }
            ImGui.EndChild();
            if(ImGuiEx.DragDropTarget(Hue_DragDrop_Target_Type, out var hueIds))
            {
                // Dragging hues into the active set is the fastest way to build curated groups.
                foreach (var id in hueIds)
                {
                    AddToHueSet(id);
                }
            }
            if (ImGui.BeginPopupModal("NewHueSet", ImGuiWindowFlags.AlwaysAutoResize | ImGuiWindowFlags.NoTitleBar))
            {
                ImGui.Text(LangManager.Get(NAME));
                ImGui.SameLine();
                ImGui.InputText("##NewHueSetName", ref _hueSetNewName, 32);
                ImGui.BeginDisabled(string.IsNullOrWhiteSpace(_hueSetNewName) || _hueSetNames.Contains(_hueSetNewName));
                if (ImGui.Button(LangManager.Get(CREATE)))
                {
                    HueSets.Add(_hueSetNewName, new SortedSet<ushort>());
                    UpdateHueSetNames();
                    _hueSetIndex = Array.IndexOf(_hueSetNames, _hueSetNewName);
                    UpdateHueSetValues();
                    _hueSetNewName = "";
                    ProfileManager.Save();
                    ImGui.CloseCurrentPopup();
                }
                ImGui.EndDisabled();
                ImGui.SameLine();
                if (ImGui.Button(LangManager.Get(CANCEL)))
                {
                    _hueSetNewName = "";
                    ImGui.CloseCurrentPopup();
                }
                ImGui.EndPopup();
            }
            if (ImGui.BeginPopupModal("DeleteHueSet", ImGuiWindowFlags.AlwaysAutoResize | ImGuiWindowFlags.NoTitleBar))
            {
                ImGui.Text(string.Format(LangManager.Get(DELETE_WARNING_1TYPE_2NAME), LangManager.Get(HUE_SET), HueSetName));
                if (ImGui.Button(LangManager.Get(YES)))
                {
                    HueSets.Remove(HueSetName);
                    UpdateHueSetNames();
                    _hueSetIndex--;
                    UpdateHueSetValues();
                    ProfileManager.Save();
                    ImGui.CloseCurrentPopup();
                }
                ImGui.SameLine();
                if (ImGui.Button(LangManager.Get(NO)))
                {
                    ImGui.CloseCurrentPopup();
                }
                ImGui.EndPopup();
            }
        }
        ImGui.EndChild();
    }
    
    /// <summary>
    /// Draws one hue row, including the preview strip and the selectable id cell.
    /// </summary>
    public void DrawHueRow(int rowIndex, ushort hueIndex, float height)
    {
        var realIndex = hueIndex - 1;
        var texRect = new Rectangle(realIndex % 16 * 32, realIndex / 16, 32, 1);
        
        ImGui.TableNextRow();
        ImGui.TableSetColumnIndex(1);
        if (realIndex < 0)
            ImGui.TextColored(ImGuiColor.Red, "No Hue");
        else
        {
            // The hue texture is arranged as a strip atlas, so each preview samples a 32x1 row
            // and stretches it horizontally to show the full gradient.
            CEDGame.UIManager.DrawImage
            (
                HuesManager.Instance.Texture,
                texRect,
                new Vector2(ImGui.GetContentRegionAvail().X, height),
                true
            );
        }
        ImGui.TableSetColumnIndex(0);

        // Columns are emitted in reverse order so the selectable id remains the last submitted
        // item, which keeps context menus and selection bookkeeping attached to the row itself.
        var selected = _selection.Contains(hueIndex);
        ImGui.SetNextItemSelectionUserData(rowIndex);
        if (ImGui.Selectable($"{hueIndex.FormatId()}", selected, ImGuiSelectableFlags.SpanAllColumns))
        {
            _lastSelectedId = hueIndex;
        }
        ImGuiEx.Tooltip(HuesManager.Instance.Names[hueIndex]);
    }
    
    /// <summary>
    /// Adds a hue to the active set, persisting the change when a named profile set is selected.
    /// </summary>
    private void AddToHueSet(ushort id)
    {
        if (_hueSetIndex == 0)
        {
            _tempHueSetValues.Add(id);
        }
        else
        {
            HueSets[HueSetName].Add(id);
            ProfileManager.Save();
        }
        UpdateHueSetValues();
    }

    /// <summary>
    /// Removes a hue from the active set, persisting the change when applicable.
    /// </summary>
    private void RemoveFromHueSet(ushort id)
    {
        if (_hueSetIndex == 0)
        {
            _tempHueSetValues.Remove(id);
        }
        else
        {
            HueSets[HueSetName].Remove(id);
            ProfileManager.Save();
        }
        UpdateHueSetValues();
    }

    /// <summary>
    /// Clears the active set and persists the result when operating on a named set.
    /// </summary>
    private void ClearHueSet()
    {
        if (_hueSetIndex == 0)
        {
            _tempHueSetValues.Clear();
        }
        else
        {
            HueSets[HueSetName].Clear();
            ProfileManager.Save();
        }
        UpdateHueSetValues();
    }

    /// <summary>
    /// Rebuilds the combo-box name list, keeping the temporary working set at index 0.
    /// </summary>
    private void UpdateHueSetNames()
    {
        _hueSetNames = HueSets.Keys.Prepend("").ToArray();
    }

    /// <summary>
    /// Refreshes the cached values for the currently selected hue set.
    /// </summary>
    private void UpdateHueSetValues()
    {
        if (_hueSetIndex == 0)
        {
            ActiveHueSetValues = _tempHueSetValues.ToList();
        }
        else
        {
            ActiveHueSetValues = HueSets[HueSetName].ToList();
        }
    }

    /// <summary>
    /// Synchronizes the hue selection with the supplied static object and scrolls the browser to it.
    /// </summary>
    public void UpdateSelection(StaticObject so)
    {
        _selection.SetSelection(so.StaticTile.Hue);
        _lastSelectedId = so.StaticTile.Hue;
        UpdateScroll = true;
    }
}