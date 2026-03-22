namespace CentrED.Map;

/// <summary>
/// Represents a map object backed by a concrete land or static tile.
/// </summary>
public abstract class TileObject : MapObject
{
    /// <summary>
    /// Gets or sets the backing tile data represented by this object.
    /// </summary>
    public BaseTile Tile;

    /// <summary>
    /// Gets or sets a cached walkability value when one has been computed.
    /// </summary>
    public bool? Walkable;

    /// <summary>
    /// Restores the tile to its default visible state after a ghost or highlight preview.
    /// </summary>
    public virtual void Reset()
    {
        Visible = true;
    }
}