using CentrED.Map;
using Microsoft.Xna.Framework.Input;

namespace CentrED.Tools;

/// <summary>
/// Removes highlighted static tiles from the map.
/// </summary>
public class DeleteTool : BaseTool
{
    /// <inheritdoc />
    public override string Name => LangManager.Get(LangEntry.DELETE_TOOL);

    /// <inheritdoc />
    public override Keys Shortcut => Keys.F5;
    
    /// <summary>
    /// Highlights the static that would be deleted.
    /// </summary>
    /// <param name="o">The target tile.</param>
    protected override void GhostApply(TileObject? o)
    {
        if (o is StaticObject so)
        {
            so.Highlighted = true;
        }
    }

    /// <summary>
    /// Clears the deletion highlight.
    /// </summary>
    /// <param name="o">The target tile.</param>
    protected override void GhostClear(TileObject? o)
    {
        if (o is StaticObject)
        {
            o.Reset();
        }
    }

    /// <summary>
    /// Deletes the highlighted static tile.
    /// </summary>
    /// <param name="o">The target tile.</param>
    protected override void InternalApply(TileObject? o)
    {
        if(o is StaticObject { Highlighted: true } so)
            Client.Remove(so.StaticTile);
    }
}