using System.Diagnostics.CodeAnalysis;
using System.Text.Json;

namespace CentrED.IO.Models;

/// <summary>
/// Defines a named land-brush preset and its transition rules.
/// </summary>
public class LandBrush
{
    /// <summary>
    /// Gets the JSON options used for land-brush persistence.
    /// </summary>
    public static readonly JsonSerializerOptions JsonOptions = new()
    {
        IncludeFields = true,
        WriteIndented = true
    };

    /// <summary>
    /// Gets or sets the brush name.
    /// </summary>
    public string Name = "";

    /// <summary>
    /// Gets or sets the land tile ids that belong to this brush.
    /// </summary>
    public List<ushort> Tiles = new();

    /// <summary>
    /// Gets or sets transition definitions keyed by neighboring brush name.
    /// </summary>
    public Dictionary<string, List<LandBrushTransition>> Transitions = new();

    /// <summary>
    /// Gets the smallest matching transition for a neighboring brush and direction mask.
    /// </summary>
    /// <param name="name">The neighboring brush name.</param>
    /// <param name="dir">The required direction mask.</param>
    /// <param name="result">The selected transition when one exists.</param>
    /// <returns><see langword="true"/> when a transition was found.</returns>
    public bool TryGetMinimalTransition(string name, Direction dir, [MaybeNullWhen(false)] out LandBrushTransition result)
    {
        if (Transitions.TryGetValue(name, out var transitions))
        {
            var matched = transitions.Where(lbt => lbt.Contains(dir)).GroupBy(lbt => lbt.Direction.Count()).MinBy
                (x => x.Key);
            if (matched != null)
            {
                var found = matched.ToArray();
                result = found[Random.Shared.Next(found.Length)];
                return true;
            }
        }
        result = null;
        return false;
    }
}

/// <summary>
/// Describes one transition tile between land brushes.
/// </summary>
public class LandBrushTransition
{
    /// <summary>
    /// Initializes an empty transition for deserialization.
    /// </summary>
    public LandBrushTransition(){}

    /// <summary>
    /// Initializes a transition that contains only a tile id.
    /// </summary>
    /// <param name="tileId">The transition tile id.</param>
    public LandBrushTransition(ushort tileId)
    {
        TileID = tileId;
        Direction = Direction.None;
    }
    
    /// <summary>
    /// Gets or sets the transition tile id.
    /// </summary>
    public ushort TileID;

    // The bitmask stores neighboring brush occupancy in a 3x3 stencil around the center tile.
    public Direction Direction;

    /// <summary>
    /// Determines whether the transition contains the supplied direction mask.
    /// </summary>
    /// <param name="dir">The direction mask to test.</param>
    /// <returns><see langword="true"/> when the mask is present.</returns>
    public bool Contains(Direction dir) => Direction.Contains(dir);
}

/// <summary>
/// Encodes the 8 surrounding directions used by land-brush transitions.
/// </summary>
[Flags]
public enum Direction : byte
{
    None = 0,
    North = 1 << 0,
    Right = 1 << 1,
    East = 1 << 2,
    Down = 1 << 3,
    South = 1 << 4,
    Left = 1 << 5,
    West = 1 << 6,
    Up = 1 << 7,
    All = 0xFF,
}

/// <summary>
/// Provides helper methods for working with land-brush direction masks.
/// </summary>
public static class DirectionHelper
{
    /// <summary>
    /// Gets all cardinal and diagonal directions in stencil order.
    /// </summary>
    public static readonly Direction[] All = [Direction.North, Direction.Right, Direction.East, Direction.Down, Direction.South, Direction.Left, Direction.West, Direction.Up];

    /// <summary>
    /// Gets a mask containing all diagonal directions.
    /// </summary>
    public static readonly Direction CornersMask = Direction.Up | Direction.Down | Direction.Left | Direction.Right;

    /// <summary>
    /// Gets a mask containing all cardinal directions.
    /// </summary>
    public static readonly Direction SideMask = Direction.North | Direction.South | Direction.East | Direction.West;

    /// <summary>
    /// Determines whether a direction mask fully contains another mask.
    /// </summary>
    /// <param name="dir">The source mask.</param>
    /// <param name="other">The mask to test.</param>
    /// <returns><see langword="true"/> when all flags in <paramref name="other"/> are present.</returns>
    public static bool Contains(this Direction dir, Direction other) => (dir & other) >= other;

    /// <summary>
    /// Gets the previous direction in clockwise stencil order.
    /// </summary>
    /// <param name="dir">The source direction.</param>
    /// <returns>The previous direction.</returns>
    public static Direction Prev(this Direction dir)
    {
        var newVal = (byte)((byte)dir >> 1);
        if (newVal == 0)
        {
            newVal = 1 << 7;
        }
        return (Direction)newVal;
    }

    /// <summary>
    /// Gets the next direction in clockwise stencil order.
    /// </summary>
    /// <param name="dir">The source direction.</param>
    /// <returns>The next direction.</returns>
    public static Direction Next(this Direction dir)
    {
        var newVal = (byte)((byte)dir << 1);
        if (newVal == 0)
        {
            newVal = 1;
        }
        return (Direction)newVal;
    }

    /// <summary>
    /// Gets the opposite direction.
    /// </summary>
    /// <param name="dir">The source direction.</param>
    /// <returns>The opposite direction.</returns>
    public static Direction Opposite(this Direction dir)
    {
        return dir switch
        {
            Direction.North => Direction.South,
            Direction.Right => Direction.Left,
            Direction.East => Direction.West,
            Direction.Down => Direction.Up,
            Direction.South => Direction.North,
            Direction.Left => Direction.Right,
            Direction.West => Direction.East,
            Direction.Up => Direction.Down,
            _ => dir
        };
    }

    /// <summary>
    /// Converts a direction into tile-space offsets.
    /// </summary>
    /// <param name="dir">The direction to convert.</param>
    /// <returns>The corresponding tile offset.</returns>
    public static (sbyte, sbyte) Offset(this Direction dir)
    {
        return dir switch
        {
            Direction.North => (0, -1),
            Direction.Right => (1, -1),
            Direction.East => (1, 0),
            Direction.Down => (1, 1),
            Direction.South => (0, 1),
            Direction.Left => (-1, 1),
            Direction.West => (-1, 0),
            Direction.Up => (-1, -1),
        };
    }

    /// <summary>
    /// Reverses all flags in the supplied direction mask.
    /// </summary>
    /// <param name="dir">The direction mask to reverse.</param>
    /// <returns>The reversed mask.</returns>
    public static Direction Reverse(this Direction dir)
    {
        var toAdd = Direction.None;
        var toRemove = Direction.None;
        foreach (var direction in Enum.GetValues<Direction>())
        {
            if (direction == Direction.None || direction == Direction.All)
                continue;
            if (!dir.HasFlag(direction))
                continue;

            toAdd |= direction.Opposite();
            toRemove |= direction;
        }

        dir |= toAdd;
        dir &= ~toRemove;

        return dir;
    }

    /// <summary>
    /// Counts the number of set flags in a direction mask.
    /// </summary>
    /// <param name="dir">The direction mask.</param>
    /// <returns>The number of enabled directions.</returns>
    public static byte Count(this Direction dir)
    {
        byte count = 0;
        var value = (byte)dir;
        while (value != 0)
        {
            value = (byte)(value & (value - 1));
            count++;
        }
        return count;
    }
}