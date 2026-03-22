using CentrED.Map;
using CentrED.Network;
using CentrED.UI;
using Microsoft.Xna.Framework.Input;
using static CentrED.Application;

namespace CentrED.Tools;

/// <summary>
/// Provides shared behavior for tools that support continuous drawing and rectangular area operations.
/// </summary>
public abstract class BaseTool : Tool
{
    /// <summary>
    /// Gets a value indicating whether the tool is currently operating in rectangular area mode.
    /// </summary>
    public bool AreaMode { get; private set; }
    
    private RectU16 _Area;

    /// <summary>
    /// Gets the current area selection.
    /// </summary>
    public RectU16 Area => _Area;

    /// <summary>
    /// Gets the tile where the current area operation started.
    /// </summary>
    protected TileObject? AreaStartTile;

    /// <summary>
    /// Starts an area operation from the supplied tile.
    /// </summary>
    /// <param name="o">The starting tile.</param>
    protected virtual void OnAreaOperationStart(TileObject? o)
    {
        if (o == null)
            return;
        
        AreaStartTile = o;
        _Area = new RectU16(o.Tile.X, o.Tile.Y, o.Tile.X, o.Tile.Y);
    }
    
    /// <summary>
    /// Updates the area-selection rectangle as the cursor moves.
    /// </summary>
    /// <param name="to">The current tile under the cursor.</param>
    protected virtual void OnAreaOperationUpdate(TileObject? to)
    {
        if (to == null)
            return;
        
        _Area.X2 = to.Tile.X;
        _Area.Y2 = to.Tile.Y;
    }

    /// <summary>
    /// Finishes the active area operation.
    /// </summary>
    protected virtual void OnAreaOperationEnd()
    {
        AreaStartTile = null;
        _Area = default;
    }
    
    /// <summary>
    /// Applies a preview state to the supplied tile.
    /// </summary>
    /// <param name="o">The target tile.</param>
    protected abstract void GhostApply(TileObject? o);

    /// <summary>
    /// Clears any preview state from the supplied tile.
    /// </summary>
    /// <param name="o">The target tile.</param>
    protected abstract void GhostClear(TileObject? o);

    /// <summary>
    /// Performs the tool's committed mutation for the supplied tile.
    /// </summary>
    /// <param name="o">The target tile.</param>
    protected abstract void InternalApply(TileObject? o);

    protected static float _chance = 100;
    protected bool Pressed;
    protected bool TopTilesOnly = true;

    /// <summary>
    /// Draws shared tool configuration.
    /// </summary>
    internal override void Draw()
    {
        DrawChance();
    }

    /// <summary>
    /// Draws the shared random-chance control used by probabilistic tools.
    /// </summary>
    protected void DrawChance()
    {
        ImGuiEx.DragFloat(LangManager.Get(LangEntry.CHANCE), ref _chance, 0.1f, 0, 100);
    }

    /// <summary>
    /// Clears transient input state when the tool is deactivated.
    /// </summary>
    /// <param name="o">The currently hovered tile, if any.</param>
    public override void OnDeactivated(TileObject? o)
    {
        base.OnDeactivated(o);
        Pressed = false;
        AreaMode = false;
        TopTilesOnly = false;
    }

    /// <summary>
    /// Enables area mode or multi-depth mode while modifier keys are held.
    /// </summary>
    /// <param name="key">The pressed key.</param>
    public sealed override void OnKeyPressed(Keys key)
    {
        if (!Pressed)
        {
            if (key == Keys.LeftControl)
            {
                AreaMode = true;
            }
            if (key == Keys.LeftShift)
            {
                TopTilesOnly = false;
            }
        }
    }
    
    /// <summary>
    /// Disables area mode or multi-depth mode when modifier keys are released.
    /// </summary>
    /// <param name="key">The released key.</param>
    public sealed override void OnKeyReleased(Keys key)
    {
        if (!Pressed)
        {
            if (key == Keys.LeftControl)
            {
                AreaMode = false;
            }
            if (key == Keys.LeftShift)
            {
                TopTilesOnly = true;
            }
        }
    }
    
    /// <summary>
    /// Starts a draw gesture and opens an undo group.
    /// </summary>
    /// <param name="o">The tile under the cursor.</param>
    public sealed override void OnMousePressed(TileObject? o)
    {
        Pressed = true;
        if (AreaMode)
        {
            OnAreaOperationStart(o);
        }
        CEDClient.BeginUndoGroup();
    }
    
    /// <summary>
    /// Commits the active draw gesture and closes the undo group.
    /// </summary>
    /// <param name="o">The tile under the cursor.</param>
    public sealed override void OnMouseReleased(TileObject? o)
    {
        if (Pressed)
        {
            if (AreaMode)
            {
                foreach (var to in MapManager.GetTiles(AreaStartTile, o, TopTilesOnly))
                {
                    InternalApply(to);   
                    GhostClear(to);
                }
                OnAreaOperationEnd();
            }
            else
            {
                InternalApply(o);
                GhostClear(o);
            }
        }
        Pressed = false;
        
        CEDClient.EndUndoGroup();
    }

    /// <summary>
    /// Updates preview state as the cursor moves.
    /// </summary>
    /// <param name="o">The tile under the cursor.</param>
    public sealed override void OnMouseEnter(TileObject? o)
    {
        if (AreaMode && Pressed)
        {
            OnAreaOperationUpdate(o);
            foreach (var to in MapManager.GetTiles(AreaStartTile, o, TopTilesOnly))
            {
                if (Random.Shared.NextDouble() * 100 < _chance)
                {
                    GhostApply(to);
                }
            }
        }
        else
        {
            if (Random.Shared.NextDouble() * 100 < _chance)
            {
                GhostApply(o);
            }
        }
    }
    
    /// <summary>
    /// Clears preview state or performs continuous application as the cursor leaves a tile.
    /// </summary>
    /// <param name="o">The tile being left.</param>
    public sealed override void OnMouseLeave(TileObject? o)
    {
        if (Pressed)
        {
            if (AreaMode)
            {
                foreach (var to in MapManager.GetTiles(AreaStartTile, o, TopTilesOnly))
                {
                    GhostClear(to);
                }
            }
            else
            {
                InternalApply(o);
            }
        }
        GhostClear(o);
    }

    /// <summary>
    /// Applies the tool immediately without gesture tracking.
    /// </summary>
    /// <param name="o">The target tile.</param>
    public override void Apply(TileObject? o)
    {
        GhostApply(o);
        InternalApply(o);
    }
}