namespace CentrED.Server.Config;

/// <summary>
/// Stores the last known tile position for an account.
/// </summary>
public class LastPos(ushort x, ushort y)
{
    /// <summary>
    /// Initializes a new last-position record at the origin.
    /// </summary>
    public LastPos() : this(0, 0)
    {
    }

    /// <summary>
    /// Gets or sets the tile X coordinate.
    /// </summary>
    public ushort X { get; set; } = x;

    /// <summary>
    /// Gets or sets the tile Y coordinate.
    /// </summary>
    public ushort Y { get; set; } = y;

    public override string ToString()
    {
        return $"{nameof(X)}: {X}, {nameof(Y)}: {Y}";
    }
}