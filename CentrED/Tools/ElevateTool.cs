using CentrED.Map;
using CentrED.UI;
using Hexa.NET.ImGui;
using Microsoft.Xna.Framework.Input;
using static CentrED.LangEntry;

namespace CentrED.Tools;

/// <summary>
/// Adjusts land or static altitude using additive, fixed, or terrain-snapped rules.
/// </summary>
public class ElevateTool : BaseTool
{
    
    /// <inheritdoc />
    public override string Name => LangManager.Get(ELEVATE_TOOL);

    /// <inheritdoc />
    public override Keys Shortcut => Keys.F4;
    
    enum ZMode
    {
        ADD = 0,
        FIXED = 1,
        SNAP_TO_TERRAIN = 2,
    }

    private int _mode;
    private int _value;
    private int _randomPlus;
    private int _randomMinus;
    private bool _lockPlusMinus;

    /// <summary>
    /// Draws the elevate-tool configuration UI.
    /// </summary>
    internal override void Draw()
    {
        ImGui.Text(LangManager.Get(MODE));
        ImGui.RadioButton(LangManager.Get(ADD_Z), ref _mode, (int)ZMode.ADD);
        ImGui.RadioButton(LangManager.Get(FIXED_Z), ref _mode, (int)ZMode.FIXED);
        ImGui.RadioButton(LangManager.Get(SNAP_TO_TERRAIN), ref _mode, (int)ZMode.SNAP_TO_TERRAIN);
        ImGui.Separator();
        
        ImGui.BeginDisabled(_mode == (int)ZMode.SNAP_TO_TERRAIN);
        ImGuiEx.DragInt("Z", ref _value, 1, -128, 127);
        ImGui.SameLine();
        if (ImGui.Button(LangManager.Get(INVERSE)))
        {
            _value = -_value;
        }
        ImGui.EndDisabled();
        ImGui.Separator();
        
        ImGui.BeginGroup();
        if (ImGuiEx.DragInt(LangManager.Get(PLUS_RANDOM_Z), ref _randomPlus, 1, 0, 127) && _lockPlusMinus)
        {
            _randomMinus = _randomPlus;
        }
        if (ImGuiEx.DragInt(LangManager.Get(MINUS_RANDOM_Z), ref _randomMinus, 1, 0, 128) && _lockPlusMinus)
        {
            _randomPlus = _randomMinus;       
        }
        ImGui.EndGroup();
        ImGui.SameLine();
        ImGui.BeginGroup();
        if (ImGui.Checkbox($"{LangManager.Get(LOCK)}##Plus", ref _lockPlusMinus))
        {
            if (_lockPlusMinus)
                _randomMinus = _randomPlus;
        }
        if (ImGui.Checkbox($"{LangManager.Get(LOCK)}##Minus", ref _lockPlusMinus))
        {
            if (_lockPlusMinus)
                _randomPlus = _randomMinus;
        }
        ImGui.EndGroup();
        ImGui.Separator();
        DrawChance();
    }

    /// <summary>
    /// Calculates the destination altitude for a tile.
    /// </summary>
    /// <param name="tile">The source tile.</param>
    /// <returns>The clamped destination altitude.</returns>
    private sbyte NewZ(BaseTile tile)
    {
        var newZ = (ZMode)_mode switch
        {
            ZMode.ADD => tile.Z + _value,
            ZMode.FIXED => _value,
            ZMode.SNAP_TO_TERRAIN => MapManager.GetLandTile(tile.X, tile.Y)?.Z ?? tile.Z, 
            _ => throw new ArgumentOutOfRangeException("[ElevateTool] Invalid Z mode:")
        };
        newZ += Random.Shared.Next(-_randomMinus, _randomPlus + 1);

        return (sbyte)Math.Clamp(newZ, sbyte.MinValue, sbyte.MaxValue);
    }

    /// <summary>
    /// Applies an elevation preview to the target tile.
    /// </summary>
    /// <param name="o">The target tile.</param>
    protected override void GhostApply(TileObject? o)
    {
        if (o is StaticObject so)
        {
            var tile = so.StaticTile;
            so.Highlighted = true;
            var newTile = new StaticTile(tile.Id, tile.X, tile.Y, NewZ(tile), tile.Hue);
            MapManager.StaticsManager.AddGhost(so, new StaticObject(newTile));
        }
        else if (o is LandObject lo)
        {
            if (_mode == (int)ZMode.SNAP_TO_TERRAIN)
                return;
            
            var tile = lo.LandTile;
            lo.Visible = false;
            var newTile = new LandTile(tile.Id, tile.X, tile.Y, NewZ(tile));
            MapManager.GhostLandTiles[lo] = new LandObject(newTile);
            MapManager.OnLandTileElevated(newTile, newTile.Z);
        }
    }

    /// <summary>
    /// Clears any elevation preview from the target tile.
    /// </summary>
    /// <param name="o">The target tile.</param>
    protected override void GhostClear(TileObject? o)
    {
        o?.Reset();
        if (o is StaticObject)
        {
            MapManager.StaticsManager.ClearGhost(o);
        }
        else if (o is LandObject lo)
        {
            MapManager.GhostLandTiles.Remove(lo);
            MapManager.OnLandTileElevated(lo.LandTile, lo.LandTile.Z);
        }
    }

    /// <summary>
    /// Commits the elevated altitude back into the target tile.
    /// </summary>
    /// <param name="o">The target tile.</param>
    protected override void InternalApply(TileObject? o)
    {
        if (o is StaticObject)
        {
            if (MapManager.StaticsManager.TryGetGhost(o, out var ghostTile))
            {
                o.Tile.Z = ghostTile.Tile.Z;
            }
        }
        else if (o is LandObject lo)
        {
            if (MapManager.GhostLandTiles.TryGetValue(lo, out var ghostTile))
            {
                o.Tile.Z = ghostTile.Tile.Z;
            }
        }
    }

    /// <summary>
    /// Switches the elevate tool into fixed-Z mode using a captured altitude.
    /// </summary>
    /// <param name="z">The captured altitude.</param>
    public override void GrabZ(sbyte z)
    {
        _mode = (int)ZMode.FIXED;
        _value = z;
    }
}