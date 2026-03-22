using System.Globalization;
using System.Numerics;
using System.Xml.Serialization;
using CentrED.IO;
using CentrED.IO.Models;
using CentrED.IO.Models.Centredplus;
using Hexa.NET.ImGui;
using static CentrED.Application;
using static CentrED.IO.Models.Direction;
using static CentrED.LangEntry;
using Vector2 = System.Numerics.Vector2;

namespace CentrED.UI.Windows;

/// <summary>
/// Manages profile-backed land brushes and their transition tiles, including import support
/// for legacy CED+ brush definitions.
/// </summary>
public class LandBrushManagerWindow : Window
{
    /// <summary>
    /// Subscribes initialization so the tile-to-brush lookup can be rebuilt when a session connects.
    /// </summary>
    public LandBrushManagerWindow()
    {
        CEDClient.Connected += InitLandBrushes;
    }
    
    /// <summary>
    /// Stable ImGui title/ID pair for the land brush manager.
    /// </summary>
    public override string Name => LangManager.Get(LANDBRUSH_MANAGER_WINDOW) + "###LandBrush Manager";

    /// <summary>
    /// Standard preview size used for full tiles and add targets.
    /// </summary>
    public static readonly Vector2 FullSize = new(44, 44);

    /// <summary>
    /// Compact preview size used inside combo boxes.
    /// </summary>
    public static readonly Vector2 HalfSize = FullSize / 2;

    private string? _tilesBrushPath = "TilesBrush.xml";

    // The legacy CED+ import format is XML-based, so a cached serializer keeps repeated imports cheap.
    private static XmlSerializer _xmlSerializer = new(typeof(TilesBrush));
    private string _importStatusText = "";

    private string _landBrushNewName = "";
    private string _selectedLandBrushName = "";
    private string _selectedTransitionBrushName = "";

    // Brushes are stored on the active profile so each user profile can maintain its own presets.
    private Dictionary<string, LandBrush> _landBrushes => ProfileManager.ActiveProfile.LandBrush;

    /// <summary>
    /// Returns the currently selected source brush, if any.
    /// </summary>
    public LandBrush? Selected => _landBrushes.GetValueOrDefault(_selectedLandBrushName);

    // Match the combo height to the preview thumbnail so brush selectors remain visually aligned.
    private static readonly Vector2 ComboFramePadding = ImGui.GetStyle().FramePadding with{ Y = (float)((HalfSize.Y - ImGui.GetTextLineHeight()) * 0.5) };
    
    /// <summary>
    /// Reverse index mapping tile ids to the brushes and transitions that reference them.
    /// </summary>
    public Dictionary<ushort, List<(string, string)>> tileToLandBrushNames = new();

    private bool _unsavedChanges;

    /// <summary>
    /// Draws the full land-brush editor, including import, brush management, and transition editing.
    /// </summary>
    protected override void InternalDraw()
    {
        if (!CEDClient.Running)
        {
            ImGui.Text(LangManager.Get(NOT_CONNECTED));
            return;
        }
        
        DrawImport();

        ImGui.BeginDisabled(!_unsavedChanges);
        if (ImGui.Button(LangManager.Get(SAVE)))
        {
            ProfileManager.Save();
            _unsavedChanges = false;
        }
        ImGui.EndDisabled();
        if (_unsavedChanges)
        {
            ImGui.SameLine();
            ImGui.TextColored(ImGuiColor.Green, LangManager.Get(UNSAVED_CHANGES));
        }
        ImGui.Separator();
        
        // The left column edits full tiles on the selected brush; the right column edits outgoing transitions.
        ImGui.Columns(2);
        if(ImGui.BeginChild("Brushes"))
        {
            ImGui.Text(LangManager.Get(LAND_BRUSH));
            if (LandBrushCombo(ref _selectedLandBrushName))
            {
                // Changing the active source brush resets the transition target to its first available entry.
                _selectedTransitionBrushName = Selected?.Transitions.Keys.FirstOrDefault("") ?? "";
            }
            if (ImGui.Button(LangManager.Get(NEW)))
            {
                ImGui.OpenPopup("LandBrushAdd");
            }
            ImGui.SameLine();
            ImGui.BeginDisabled(_landBrushes.Count <= 0);
            if (ImGui.Button(LangManager.Get(DELETE)))
            {
                ImGui.OpenPopup("LandBrushDelete");
            }
            ImGui.EndDisabled();
            ImGui.Separator();
            if (Selected != null)
            {
                DrawFullTiles();
            }
            DrawBrushPopups();
        }
        ImGui.EndChild();
        ImGui.NextColumn();
        if(ImGui.BeginChild("Transitions"))
        {
            if (Selected != null)
            {
                DrawTransitions();
            }
            DrawTransitionPopups();
        }
        ImGui.EndChild();
    }

    /// <summary>
    /// Draws a half-size preview for the named brush.
    /// </summary>
    public void DrawPreview(string name)
    {
        DrawPreview(name, HalfSize);
    }

    /// <summary>
    /// Draws a preview for the named brush using its first full tile as representative art.
    /// </summary>
    public void DrawPreview(string name, Vector2 size)
    {
        if (_landBrushes.TryGetValue(name, out var brush))
        {
            if (brush.Tiles.Count > 0)
            {
                DrawTile(brush.Tiles[0], size);
            }
            else
            {
                ImGui.Dummy(size);
            } 
        }
        else
        {
            ImGui.Dummy(size);
        }
    }

    /// <summary>
    /// Draws a land texmap preview for the supplied tile id.
    /// </summary>
    private void DrawTile(int id, Vector2 size)
    {
        var spriteInfo = CEDGame.MapManager.Texmaps.GetTexmap(CEDGame.MapManager.UoFileManager.TileData.LandData[id].TexID);
        if (spriteInfo.Texture != null)
        {
            CEDGame.UIManager.DrawImage(spriteInfo.Texture, spriteInfo.UV, size, true);
        }
        else
        {
            ImGui.Dummy(size);
        }
    }

    /// <summary>
    /// Convenience overload for the main land-brush selector.
    /// </summary>
    public bool LandBrushCombo(ref string selectedName)
    {
        return LandBrushCombo("##landBrush", _landBrushes, ref selectedName);
    }

    /// <summary>
    /// Draws a brush-selection combo with thumbnail previews.
    /// </summary>
    private bool LandBrushCombo<T>(string id, Dictionary<string, T> dictionary, ref string selectedName, ImGuiComboFlags flags = ImGuiComboFlags.HeightLarge)
    {
        var result = false;
        var names = dictionary.Keys.ToArray();

        // The currently selected brush is previewed inline before the combo opens.
        DrawPreview(selectedName);
        ImGui.SameLine();
        ImGui.PushItemWidth(ImGui.GetContentRegionAvail().X);
        ImGui.PushStyleVar(ImGuiStyleVar.FramePadding, ComboFramePadding);
        if(ImGui.BeginCombo(id, selectedName, flags))
        {
            foreach (var name in names)
            {
                var is_selected = name == selectedName;

                // Each entry mirrors the inline selector layout so the user can identify brushes visually.
                DrawPreview(name);
                ImGui.SameLine();
                if (ImGui.Selectable(name, is_selected, ImGuiSelectableFlags.None, HalfSize with { X = 0 }))
                {
                    result = true;
                    selectedName = name;
                }
                if (is_selected)
                {
                    ImGui.SetItemDefaultFocus();
                }
            }
            ImGui.EndCombo();
        }
        ImGui.PopStyleVar();
        ImGui.PopItemWidth();
        return result;
    }

    /// <summary>
    /// Draws the selected brush's full-tile list and accepts drag-dropped terrain tiles.
    /// </summary>
    private void DrawFullTiles()
    {
        foreach (var fullTile in Selected.Tiles.ToArray())
        {
            DrawTile(fullTile, FullSize);
            ImGuiEx.Tooltip(fullTile.FormatId());
            ImGui.SameLine();
            ImGui.BeginGroup();

            // Destructive actions are colored red to distinguish them from the preview and id label.
            ImGui.PushStyleColor(ImGuiCol.Button, new Vector4(1, 0, 0, .2f));
            ImGui.PushStyleColor(ImGuiCol.ButtonHovered, new Vector4(1, 0, 0, 1));
            if (ImGui.SmallButton($"x##{fullTile}"))
            {
                Selected.Tiles.Remove(fullTile);
                RemoveLandBrushEntry(fullTile, _selectedLandBrushName, _selectedLandBrushName);
                _unsavedChanges = true;
            }
            ImGui.PopStyleColor(2);
            ImGui.Text(fullTile.FormatId());
            ImGui.EndGroup();
        }
        ImGui.Button("+##AddFullTile", FullSize);
        ImGuiEx.Tooltip(LangManager.Get(DRAG_AND_DROP_TILE_HERE));
        if (ImGuiEx.DragDropTarget(TilesWindow.TERRAIN_DRAG_DROP_TYPE, out var ids))
        {
            // Only unique tile ids are added so the brush stays normalized.
            foreach (var id in ids)
            {
                if(!Selected.Tiles.Contains(id))
                {
                    Selected.Tiles.Add(id);
                    AddLandBrushEntry(id, _selectedLandBrushName, _selectedLandBrushName);
                    _unsavedChanges = true;
                }
            }
        }
    }

    /// <summary>
    /// Draws the transition editor for the selected source brush.
    /// </summary>
    private void DrawTransitions()
    {
        ImGui.Text(LangManager.Get(TRANSITIONS));
        LandBrushCombo("transitions", Selected.Transitions, ref _selectedTransitionBrushName);
        if (ImGui.Button(LangManager.Get(NEW)))
        {
            ImGui.OpenPopup("TransitionsAdd");
        }
        ImGui.SameLine();
        ImGui.BeginDisabled(Selected.Transitions.Count == 0);
        if (ImGui.Button(LangManager.Get(DELETE)))
        {
            ImGui.OpenPopup("TransitionsDelete");
        }
        ImGui.EndDisabled();
        ImGui.Separator();
        
        if(Selected.Transitions.Count == 0)
            return;
        
        var targetBrush = _landBrushes[_selectedTransitionBrushName];

        // Transition direction buttons rely on both brushes having at least one representative full tile.
        if(Selected.Tiles.Count == 0 || targetBrush.Tiles.Count == 0)
        {
            ImGui.Text(LangManager.Get(MISSING_FULL_TILES_WARNING));
            return;
        }
        var sourceTexture = CalculateButtonTexture(Selected.Tiles[0]);
        var targetTexture = CalculateButtonTexture(targetBrush.Tiles[0]);
        var transitions = Selected.Transitions[_selectedTransitionBrushName];
        foreach (var transition in transitions.ToArray())
        {
            var tileId = transition.TileID;
            DrawTile(tileId, FullSize);
            ImGui.SameLine();
            ImGui.BeginGroup();
            ImGui.PushStyleColor(ImGuiCol.Button, new Vector4(1, 0, 0, .2f));
            ImGui.PushStyleColor(ImGuiCol.ButtonHovered, new Vector4(1, 0, 0, 1));
            if (ImGui.SmallButton($"x##{transition.TileID}"))
            {
                transitions.Remove(transition);
                RemoveLandBrushEntry(transition.TileID, _selectedLandBrushName, _selectedTransitionBrushName);
                _unsavedChanges = true;
            }
            ImGui.PopStyleColor(2);
            ImGui.Text(tileId.FormatId());            
            ImGui.EndGroup();
            ImGui.SameLine();
            ImGui.BeginGroup();
            ImGui.PushStyleVar(ImGuiStyleVar.FramePadding, Vector2.One);
            ImGui.PushStyleVar(ImGuiStyleVar.ItemSpacing, Vector2.Zero);
            ImGui.PushStyleColor(ImGuiCol.Button, new Vector4(0, 0, 0, 0));

            // The 3x3 direction pad shows whether each edge/corner points toward the source or target brush.
            ToggleDirButton(transition, Up, sourceTexture, targetTexture);
            ImGui.SameLine();
            ToggleDirButton(transition, North, sourceTexture, targetTexture);
            ImGui.SameLine();
            ToggleDirButton(transition, Right, sourceTexture, targetTexture);
            ToggleDirButton(transition, West, sourceTexture, targetTexture);
            ImGui.SameLine();
            unsafe
            {
                ImGui.Image(new ImTextureRef(null, sourceTexture.texPtr), new Vector2(11, 11), sourceTexture.uv0, sourceTexture.uv1);
            }
            ImGui.SameLine();
            ToggleDirButton(transition, East, sourceTexture, targetTexture);
            ToggleDirButton(transition, Left, sourceTexture, targetTexture);
            ImGui.SameLine();
            ToggleDirButton(transition, South, sourceTexture, targetTexture);
            ImGui.SameLine();
            ToggleDirButton(transition, Down, sourceTexture, targetTexture);
            ImGui.PopStyleColor();
            ImGui.PopStyleVar(2);
            ImGui.EndGroup();
        }
        ImGui.Button("+##AddTransition", FullSize);
        ImGuiEx.Tooltip(LangManager.Get(DRAG_AND_DROP_TILE_HERE));
        if (ImGuiEx.DragDropTarget(TilesWindow.TERRAIN_DRAG_DROP_TYPE, out var ids))
        {
            // Transition tiles are keyed by tile id, so duplicates are ignored.
            foreach (var id in ids)
            {
                if(transitions.All(t => t.TileID != id))
                {
                    transitions.Add(new LandBrushTransition(id));
                    AddLandBrushEntry(id, _selectedLandBrushName, _selectedTransitionBrushName);
                    _unsavedChanges = true;
                }
            }
        }
    }

    /// <summary>
    /// Toggles one direction flag on a transition, swapping the preview thumbnail between the
    /// source and target brush to visualize the chosen edge ownership.
    /// </summary>
    private unsafe void ToggleDirButton(LandBrushTransition transition, Direction dir, (ImTextureID texPtr, Vector2 uv0, Vector2 uv1) sourceTexture, (ImTextureID texPtr, Vector2 uv0, Vector2 uv1) targetTexture)
    {
        var isSet = transition.Direction.Contains(dir);
        var tex = isSet ? targetTexture : sourceTexture;
        if (ImGui.ImageButton($"{transition.TileID}{dir}", new ImTextureRef(null, tex.texPtr), new Vector2(11,11), tex.uv0, tex.uv1))
        {
            if (isSet)
            {
                transition.Direction &= ~dir;
            }
            else
            {
                transition.Direction |= dir;
            }
            _unsavedChanges = true;
        }
        ImGuiEx.Tooltip(isSet ? _selectedTransitionBrushName : _selectedLandBrushName);
    }

    /// <summary>
    /// Precomputes the bound texture handle and UV coordinates used by the direction-pad buttons.
    /// </summary>
    private (ImTextureID texPtr, Vector2 uv0, Vector2 uv1) CalculateButtonTexture(ushort tileId)
    {
        var spriteInfo = CEDGame.MapManager.Texmaps.GetTexmap(CEDGame.MapManager.UoFileManager.TileData.LandData[tileId].TexID);
        if (spriteInfo.Texture == null)
        {
            // Fall back to a known texmap so the UI remains drawable even when a tile has no texture.
            spriteInfo = CEDGame.MapManager.Texmaps.GetTexmap(0x0001);
        }
        var tex = spriteInfo.Texture;
        var bounds = spriteInfo.UV;
        var texPtr = CEDGame.UIManager._uiRenderer.BindTexture(tex);
        var fWidth = (float)tex.Width;
        var fHeight = (float)tex.Height;
        var uv0 = new Vector2(bounds.X / fWidth, bounds.Y / fHeight);
        var uv1 = new Vector2((bounds.X + bounds.Width) / fWidth, (bounds.Y + bounds.Height) / fHeight);
        return (texPtr, uv0, uv1);
    }

    /// <summary>
    /// Draws the create/delete popups for top-level land brushes.
    /// </summary>
    private void DrawBrushPopups()
    {
        if (ImGui.BeginPopupModal("LandBrushAdd", ImGuiWindowFlags.AlwaysAutoResize | ImGuiWindowFlags.NoDecoration))
        {
            ImGui.InputText(LangManager.Get(NAME), ref _landBrushNewName, 64);
            ImGui.BeginDisabled(_landBrushes.ContainsKey(_landBrushNewName) || string.IsNullOrWhiteSpace(_landBrushNewName));
            if (ImGui.Button(LangManager.Get(CREATE)))
            {
                if (!_landBrushes.ContainsKey(_landBrushNewName))
                {
                    _landBrushes.Add(_landBrushNewName, new LandBrush
                    {
                        Name = _landBrushNewName
                    });
                    _selectedLandBrushName = _landBrushNewName;
                    _selectedTransitionBrushName = Selected.Transitions.Keys.FirstOrDefault("");
                    _landBrushNewName = "";
                    _unsavedChanges = true;
                    ImGui.CloseCurrentPopup();
                }
            }
            ImGui.EndDisabled();
            ImGui.SameLine();
            if (ImGui.Button(LangManager.Get(CANCEL)))
            {
                ImGui.CloseCurrentPopup();
            }
            ImGui.EndPopup();
        }
        if (ImGui.BeginPopupModal("LandBrushDelete", ImGuiWindowFlags.AlwaysAutoResize | ImGuiWindowFlags.NoDecoration))
        {
            ImGui.Text(string.Format(LangManager.Get(DELETE_WARNING_1TYPE_2NAME), LangManager.Get(LAND_BRUSH), Selected.Name));
            if (ImGui.Button(LangManager.Get(YES), new Vector2(100, 0)))
            {
                // Remove reverse-index entries that point to the deleted brush as a transition target.
                foreach (var landBrush in _landBrushes.Values)
                {
                    if(landBrush.Transitions.Remove(Selected.Name, out var removed))
                    {
                        foreach (var transition in removed)
                        {
                            RemoveLandBrushEntry(transition.TileID, landBrush.Name, _selectedLandBrushName);
                        }
                    }
                }

                // Remove reverse-index entries owned by the deleted brush itself.
                foreach (var (name, transitions) in Selected.Transitions)
                {
                    foreach (var transition in transitions)
                    {
                        RemoveLandBrushEntry(transition.TileID, _selectedLandBrushName, name);
                    }
                }
                Selected.Tiles.ForEach(t => RemoveLandBrushEntry(t, _selectedLandBrushName, _selectedLandBrushName));
                _landBrushes.Remove(Selected.Name);
                _selectedLandBrushName = _landBrushes.Keys.FirstOrDefault("");
                _selectedTransitionBrushName = Selected?.Transitions.Keys.FirstOrDefault("") ?? "";
                _unsavedChanges = true;
                ImGui.CloseCurrentPopup();
            }
            ImGui.SameLine();
            if (ImGui.Button(LangManager.Get(NO), new Vector2(100, 0)))
            {
                ImGui.CloseCurrentPopup();
            }
            ImGui.EndPopup();
        }
    }

    private string _transitionAddName = "";

    /// <summary>
    /// Draws the create/delete popups for transition targets on the selected land brush.
    /// </summary>
    private void DrawTransitionPopups()
    {
        if (ImGui.BeginPopupModal("TransitionsAdd", ImGuiWindowFlags.NoDecoration))
        {
            // Only brushes not already used as a transition target are offered here.
            var notUsedBruses = _landBrushes.Where(lb => lb.Key != Selected.Name && !Selected.Transitions.Keys.Contains(lb.Key)).ToDictionary();
            if(_transitionAddName == "")
                _transitionAddName = notUsedBruses.Keys.FirstOrDefault("");
            LandBrushCombo("##addTransition", notUsedBruses, ref _transitionAddName);
            ImGui.BeginDisabled(notUsedBruses.Count == 0);
            if (ImGui.Button(LangManager.Get(CREATE), new Vector2(100, 0)))
            {
                Selected.Transitions.Add(_transitionAddName, new List<LandBrushTransition>());
                _selectedTransitionBrushName = _transitionAddName;
                _transitionAddName = "";
                _unsavedChanges = true;
                ImGui.CloseCurrentPopup();
            }
            ImGui.EndDisabled();
            ImGui.SameLine();
            if (ImGui.Button(LangManager.Get(CANCEL), new Vector2(100, 0)))
            {
                ImGui.CloseCurrentPopup();
                _transitionAddName = "";
            }
            ImGui.EndPopup();
        }
        if (ImGui.BeginPopupModal("TransitionsDelete", ImGuiWindowFlags.AlwaysAutoResize | ImGuiWindowFlags.NoDecoration))
        {
            ImGui.Text(string.Format(LangManager.Get(DELETE_WARNING_1TYPE_2NAME), LangManager.Get(TRANSITION), _selectedTransitionBrushName));
            if (ImGui.Button(LangManager.Get(YES), new Vector2(100, 0)))
            {
                //Remove all entries that have removed brush as to-transition
                if (Selected!.Transitions.Remove(_selectedTransitionBrushName, out var removed))
                {
                    removed.ForEach(t => RemoveLandBrushEntry(t.TileID, Selected.Name, _selectedTransitionBrushName));
                }
                if(Selected.Transitions.Count > 0)
                    _selectedTransitionBrushName = Selected.Transitions.Keys.FirstOrDefault("");
                else
                    _selectedTransitionBrushName = "";
                _selectedTransitionBrushName = Selected.Transitions.Keys.FirstOrDefault("");
                _unsavedChanges = true;
                ImGui.CloseCurrentPopup();
            }
            ImGui.SameLine();
            if (ImGui.Button(LangManager.Get(NO), new Vector2(100, 0)))
            {
                ImGui.CloseCurrentPopup();
            }
            ImGui.EndPopup();
        }
    }
    
    /// <summary>
    /// Adds a tile-to-brush mapping to the reverse lookup index.
    /// </summary>
    public void AddLandBrushEntry(ushort tileId, string from, string to)
    {
        if (!tileToLandBrushNames.ContainsKey(tileId))
        {
            tileToLandBrushNames.Add(tileId, new List<(string, string)>());
        }
        tileToLandBrushNames[tileId].Add((from, to));
    }

    /// <summary>
    /// Removes a tile-to-brush mapping from the reverse lookup index and drops empty buckets.
    /// </summary>
    public void RemoveLandBrushEntry(ushort tileId, string from, string to)
    {
        if (tileToLandBrushNames.ContainsKey(tileId))
        {
            tileToLandBrushNames[tileId].Remove((from, to));
        }
        if (tileToLandBrushNames[tileId].Count <= 0)
        {
            tileToLandBrushNames.Remove(tileId);
        }
    }

    #region Import

    /// <summary>
    /// Draws the legacy CED+ import controls for TilesBrush.xml files.
    /// </summary>
    private void DrawImport()
    {
        if(ImGui.CollapsingHeader("Import CED+ TileBrush.xml"))
        {
            _tilesBrushPath ??= "";
            ImGui.InputText("File", ref _tilesBrushPath, 512);
            ImGui.SameLine();
            if (ImGui.Button("..."))
            {
                if (TinyFileDialogs.TryOpenFile
                        ("Select TilesBrush file", Environment.CurrentDirectory, ["*.xml"], null, false, out var newPath))
                {
                    _tilesBrushPath = newPath;
                }
            }
            if (ImGui.Button("Import"))
            {
                ImportLandBrush();
                _selectedLandBrushName = _landBrushes.Keys.FirstOrDefault("");
            }
            ImGui.TextColored(ImGuiColor.Green, _importStatusText);
        }
    }
    
    /// <summary>
    /// Imports land brushes from a legacy CED+ TilesBrush.xml document and replaces the current
    /// profile brush set with the imported data.
    /// </summary>
    private void ImportLandBrush()
    {
        try
        {
            using var reader = new FileStream(_tilesBrushPath!, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
            var tilesBrush = (TilesBrush)_xmlSerializer.Deserialize(reader)!;
            var target = ProfileManager.ActiveProfile.LandBrush;
            target.Clear();
            foreach (var brush in tilesBrush.Brush)
            {
                var newBrush = new LandBrush();
                newBrush.Name = brush.Name;
                foreach (var land in brush.Land)
                {
                    if (TryParseHex(land.ID, out var newId))
                    {
                        newBrush.Tiles.Add(newId);
                    }
                    else
                    {
                        Console.WriteLine($"Unable to parse land ID {land.ID} in brush {brush.Id}");
                    }
                }
                foreach (var edge in brush.Edge)
                {
                    // Transition targets are referenced by brush id in the import format and then
                    // re-keyed by brush name in the in-memory profile model.
                    var to = tilesBrush.Brush.Find(b => b.Id == edge.To);
                    var newList = new List<LandBrushTransition>();
                    foreach (var edgeLand in edge.Land)
                    {
                        if (TryParseHex(edgeLand.ID, out var newId))
                        {
                            var newType = ConvertType(edgeLand.Type);
                            newList.Add
                            (
                                new LandBrushTransition
                                {
                                    TileID = newId,
                                    Direction = newType
                                }
                            );
                        }
                        else
                        {
                            Console.WriteLine($"Unable to parse edgeland ID {edgeLand.ID} in brush {brush.Id}");
                        }
                    }
                    newBrush.Transitions.Add(to.Name, newList);
                }
                target.Add(newBrush.Name, newBrush);
            }
            InitLandBrushes();
            ProfileManager.Save();
            _selectedLandBrushName = ProfileManager.ActiveProfile.LandBrush.Keys.FirstOrDefault("");
            _selectedTransitionBrushName = Selected?.Transitions.Keys.FirstOrDefault("") ?? "";
            _importStatusText = "Import Successful";
        }
        catch (Exception e)
        {
            Console.WriteLine(e);
        }
    }
    
    /// <summary>
    /// Rebuilds the reverse lookup table from the current profile brush definitions.
    /// </summary>
    public void InitLandBrushes()
    {
        tileToLandBrushNames.Clear();
        var landBrushes = ProfileManager.ActiveProfile.LandBrush;
        foreach (var keyValuePair in landBrushes)
        {
            var name = keyValuePair.Key;
            var brush = keyValuePair.Value;
            var fullTiles = brush.Tiles;
            foreach (var fullTile in fullTiles)
            {
                AddLandBrushEntry(fullTile, name, name);
            }
            var transitions = brush.Transitions;
            foreach (var valuePair in transitions)
            {
                var toName = valuePair.Key;
                var tiles = valuePair.Value;
                foreach (var tile in tiles)
                {
                    AddLandBrushEntry(tile.TileID, name, toName);
                }
            }
        }
    }

    /// <summary>
    /// Parses a hexadecimal tile id in the legacy 0xNNNN import format.
    /// </summary>
    private bool TryParseHex(string value, out ushort result)
    {
        // Substring removes the leading 0x prefix before TryParse handles the remaining hex digits.
        return ushort.TryParse(value.Substring(2), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out result);
    }

    /// <summary>
    /// Converts legacy CED+ edge codes into the current directional flag enum.
    /// </summary>
    private Direction ConvertType(string oldType)
    {
        switch (oldType)
        {
            case "DR": return Up;
            case "DL": return Right;
            case "UL": return Down;
            case "UR": return Left;
            case "LL": return Down | East | Right;
            case "UU": return Left | South | Down;
            // The import file format mentions type FF, but no known data appears to use it.
            // "FF" => 
            default:
                Console.WriteLine("Unknown type " + oldType);
                return 0;
        }
    }
    #endregion
}