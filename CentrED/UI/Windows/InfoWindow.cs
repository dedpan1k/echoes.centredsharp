using System.Drawing;
using CentrED.IO.Models;
using CentrED.Map;
using Hexa.NET.ImGui;
using static CentrED.Application;
using static CentrED.LangEntry;

namespace CentrED.UI.Windows;

/// <summary>
/// Shows detailed information for the currently selected tile object and allows browsing the
/// other tiles that exist at the same world position.
/// </summary>
public class InfoWindow : Window
{
    /// <summary>
    /// Stable ImGui title/ID pair for the selection info window.
    /// </summary>
    public override string Name => LangManager.Get(INFO_WINDOW) + "###Info";

    /// <summary>
    /// The info window is part of the default editor layout and starts visible.
    /// </summary>
    public override WindowState DefaultState => new()
    {
        IsOpen = true
    };

    /// <summary>
    /// Mouse-wheel scrolling is repurposed for cycling the per-tile combo selection instead of
    /// scrolling the window contents.
    /// </summary>
    public override ImGuiWindowFlags WindowFlags => ImGuiWindowFlags.NoScrollWithMouse;

    /// <summary>
    /// The primary tile currently selected in the map view.
    /// Setting this also rebuilds the list of all tiles that share the same coordinates so the
    /// window can inspect stacked land/static content at that location.
    /// </summary>
    public TileObject? Selected
    {
        get => _Selected;
        set
        {
            _Selected = value;

            // Reset the secondary selection whenever the primary tile changes.
            _otherTiles.Clear();
            _otherSelected = null;
            _otherTilesNames = Array.Empty<string?>();

            // There is nothing meaningful to inspect without an active selection and loaded map state.
            if (_Selected == null || !CEDClient.Running)
                return;

            // Validate coordinates against the currently loaded land-tile grid before indexing it.
            var landTiles = CEDGame.MapManager.LandTiles;
            int w = landTiles.GetLength(0);
            int h = landTiles.GetLength(1);
            int x = _Selected.Tile.X;
            int y = _Selected.Tile.Y;

            if (x < 0 || y < 0 || x >= w || y >= h)
                return;

            // Collect the land tile plus any statics stacked on the same map coordinate.
            var landTile = landTiles[x, y];
            if (landTile != null)
            {
                _otherTiles.Add(landTile);
            }
            var staticTiles = CEDGame.MapManager.StaticsManager.Get(x, y);
            if (staticTiles != null)
            {
                _otherTiles.AddRange(staticTiles);
            }
            _otherTilesNames = _otherTiles
                .Select(o => 
                    o is LandObject land
                        ? $"Land {land.Tile.Id.FormatId()}"
                    : o is StaticObject stat
                        ? $"Object {stat.Tile.Id.FormatId()}"
                    : o.Tile.ShortString()
                ).ToArray();

            // Default the secondary browser to the first tile at the selected location.
            UpdateSelectedOtherTile(0);
        }
    }

    private TileObject? _Selected;

    private List<TileObject> _otherTiles = [];
    private string?[] _otherTilesNames = [];
    private int _otherTileIndex;
    private TileObject? _otherSelected;
    
    /// <summary>
    /// Draws information for the primary selection, then exposes a secondary picker for any
    /// other tiles found at the same location.
    /// </summary>
    protected override void InternalDraw()
    {
        if (_Selected == null) return;

        DrawTileInfo(_Selected);
        
        ImGui.SeparatorText($"{LangManager.Get(ALL_TILES_AT)} {_Selected.Tile.X},{_Selected.Tile.Y}");
        if(ImGui.Combo("##OtherTiles", ref _otherTileIndex, _otherTilesNames, _otherTiles.Count))
        {
            UpdateSelectedOtherTile(_otherTileIndex);
            
        }
        if (ImGui.GetIO().MouseWheel != 0 && ImGui.IsItemHovered())
        {
            // The combo also supports wheel cycling while hovered, which is faster when stepping
            // through a tall stack of statics at one coordinate.
            var incVal = ImGui.GetIO().MouseWheel > 0 ? -1 : 1;
            UpdateSelectedOtherTile(_otherTileIndex + incVal);
            ImGui.GetIO().MouseWheel = 0;
        }
        if (_otherSelected != null)
        {
            if (ImGui.Button(LangManager.Get(APPLY_TOOL)))
            {
                // Applying the active tool against the alternate selection lets the user target
                // a stacked tile that is not the map view's primary pick.
                CEDGame.MapManager.ActiveTool.Apply(_otherSelected);
            }
            DrawTileInfo(_otherSelected);
        }
    }

    /// <summary>
    /// Moves the secondary selection to the requested index while clamping it to the available
    /// tile list.
    /// </summary>
    private void UpdateSelectedOtherTile(int newIndex)
    {
        _otherTileIndex = newIndex;
        if (_otherTiles.Count == 0)
        {
            _otherTileIndex = 0;
            _otherSelected = null;
        }
        else {
            if (_otherTileIndex < 0)
                _otherTileIndex = 0;
            else if (_otherTileIndex >= _otherTiles.Count)
                _otherTileIndex = _otherTiles.Count - 1;
            
            _otherSelected = _otherTiles[_otherTileIndex];
        }
    }

    /// <summary>
    /// Draws either land-tile or static-tile details, including art preview, coordinates, id,
    /// and tile-data metadata.
    /// </summary>
    private void DrawTileInfo(TileObject? o)
    {
        if (o is LandObject lo)
        {
            var landTile = lo.Tile;
            ImGui.Text(LangManager.Get(LAND));

            // Some land ids have no corresponding art entry, so fall back to index 0 instead of
            // asking the art system for an invalid sprite.
            var isArtValid = CEDGame.MapManager.UoFileManager.Arts.File.GetValidRefEntry(landTile.Id).Length > 0;
            uint artIndex = isArtValid ? (uint)landTile.Id : 0; // Fallback to UNUSED placeholder if no art
            var spriteInfo = CEDGame.MapManager.Arts.GetLand(artIndex);

            if (!CEDGame.UIManager.DrawImage(spriteInfo.Texture, spriteInfo.UV))
            {
                ImGui.TextColored(ImGuiColor.Red, LangManager.Get(TEXTURE_NOT_FOUND));
            }
            var tileData = CEDGame.MapManager.UoFileManager.TileData.LandData[landTile.Id];
            ImGui.Text(tileData.Name ?? "");
            ImGui.Text($"X:{landTile.X} Y:{landTile.Y} Z:{landTile.Z}");
            ImGui.Text($"ID: {landTile.Id.FormatId()}");
            ImGui.Text(LangManager.Get(FLAGS));
            ImGui.Text(tileData.Flags.ToString().Replace(", ", "\n"));
        }
        else if (o is StaticObject so)
        {
            var staticTile = so.StaticTile;
            ImGui.Text(LangManager.Get(OBJECT));
            ref var indexEntry = ref CEDGame.MapManager.UoFileManager.Arts.File.GetValidRefEntry(staticTile.Id + 0x4000);
            var spriteInfo = CEDGame.MapManager.Arts.GetArt((uint)(staticTile.Id + indexEntry.AnimOffset));
            if(spriteInfo.Texture != null)
            {
                // Static art is cropped to its real non-transparent bounds so the preview does not
                // waste space on padding inside the source atlas rectangle.
                var realBounds =  CEDGame.MapManager.Arts.GetRealArtBounds(staticTile.Id);
                CEDGame.UIManager.DrawImage
                (
                    spriteInfo.Texture,
                    new Rectangle(spriteInfo.UV.X + realBounds.X, spriteInfo.UV.Y + realBounds.Y, realBounds.Width, realBounds.Height)
                );
            }
            else
            {
                ImGui.TextColored(ImGuiColor.Red, LangManager.Get(TEXTURE_NOT_FOUND));
            }
            var tileData = CEDGame.MapManager.UoFileManager.TileData.StaticData[staticTile.Id];
            ImGui.Text(tileData.Name ?? "");
            ImGui.Text($"X:{staticTile.X} Y:{staticTile.Y} Z:{staticTile.Z}");
            ImGui.Text($"ID: {staticTile.Id.FormatId()}");
            ImGui.Text($"{LangManager.Get(HUE)}: {staticTile.Hue.FormatId()}");
            ImGui.Text($"{LangManager.Get(HEIGHT)}: {tileData.Height}");
            ImGui.Text(LangManager.Get(FLAGS));
            ImGui.Text(tileData.Flags.ToString().Replace(", ", "\n"));
        }
    }
}