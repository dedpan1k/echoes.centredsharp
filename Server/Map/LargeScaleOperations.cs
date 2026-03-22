using System.Buffers;
using static CentrED.Network.LSO;

namespace CentrED.Server.Map;

/// <summary>
/// Represents one logical operation inside a compound large-scale edit command.
/// </summary>
public abstract class LargeScaleOperation
{
    /// <summary>
    /// Validates the operation against the active landscape before any tiles are mutated.
    /// </summary>
    /// <param name="landscape">The landscape that will execute the operation.</param>
    public virtual void Validate(ServerLandscape landscape){}
}

/// <summary>
/// Describes a copy or move large-scale operation.
/// </summary>
public class LsCopyMove : LargeScaleOperation
{
    /// <summary>
    /// Gets the copy or move mode requested by the client.
    /// </summary>
    public CopyMove Type;

    /// <summary>
    /// Gets the horizontal offset applied during the operation.
    /// </summary>
    public int OffsetX;

    /// <summary>
    /// Gets the vertical offset applied during the operation.
    /// </summary>
    public int OffsetY;

    /// <summary>
    /// Gets a value indicating whether the source area should be erased after copying.
    /// </summary>
    public bool Erase;
    
    /// <summary>
    /// Initializes a copy or move operation from the large-scale payload.
    /// </summary>
    /// <param name="reader">The payload reader positioned at the copy/move definition.</param>
    public LsCopyMove(ref SpanReader reader)
    {
        Type = (CopyMove)reader.ReadByte();
        OffsetX = reader.ReadInt32();
        OffsetY = reader.ReadInt32();
        Erase = reader.ReadBoolean();
    }
}

/// <summary>
/// Describes a terrain altitude adjustment inside a large-scale edit.
/// </summary>
public class LsSetAltitude : LargeScaleOperation
{
    /// <summary>
    /// Gets the altitude adjustment mode.
    /// </summary>
    public SetAltitude Type;

    /// <summary>
    /// Gets the minimum absolute altitude for terrain clamping mode.
    /// </summary>
    public sbyte MinZ;

    /// <summary>
    /// Gets the maximum absolute altitude for terrain clamping mode.
    /// </summary>
    public sbyte MaxZ;

    /// <summary>
    /// Gets the relative altitude delta for relative mode.
    /// </summary>
    public sbyte RelativeZ;
    
    /// <summary>
    /// Initializes an altitude operation from the large-scale payload.
    /// </summary>
    /// <param name="reader">The payload reader positioned at the altitude definition.</param>
    public LsSetAltitude(ref SpanReader reader)
    {
        Type = (SetAltitude)reader.ReadByte();
        switch (Type)
        {
            case SetAltitude.Terrain:
            {
                MinZ = reader.ReadSByte();
                MaxZ = reader.ReadSByte();
                break;
            }
            case SetAltitude.Relative:
            {
                RelativeZ = reader.ReadSByte();
                break;
            }
        }
    }
}

/// <summary>
/// Describes a terrain paint operation using a supplied set of land tile identifiers.
/// </summary>
public class LsDrawTerrain : LargeScaleOperation
{
    /// <summary>
    /// Gets the land tile identifiers that may be painted by the operation.
    /// </summary>
    public ushort[] TileIds;

    /// <summary>
    /// Initializes a terrain-paint operation from the large-scale payload.
    /// </summary>
    /// <param name="reader">The payload reader positioned at the draw-terrain definition.</param>
    public LsDrawTerrain(ref SpanReader reader)
    {
        var count = reader.ReadUInt16();
        TileIds = new ushort[count];
        for (int i = 0; i < count; i++)
        {
            TileIds[i] = reader.ReadUInt16();
        }
    }

    /// <summary>
    /// Validates that every requested land tile identifier exists in tiledata.
    /// </summary>
    /// <param name="landscape">The landscape that will execute the operation.</param>
    public override void Validate(ServerLandscape landscape)
    {
        foreach (var tileId in TileIds)
        {
            landscape.AssertLandTileId(tileId);
        }
    }
}

/// <summary>
/// Describes a static-deletion operation filtered by tile identifiers and altitude range.
/// </summary>
public class LsDeleteStatics : LargeScaleOperation
{
    /// <summary>
    /// Gets the static tile identifiers eligible for deletion.
    /// </summary>
    public ushort[] TileIds;

    /// <summary>
    /// Gets the minimum allowed altitude for matching statics.
    /// </summary>
    public sbyte MinZ;

    /// <summary>
    /// Gets the maximum allowed altitude for matching statics.
    /// </summary>
    public sbyte MaxZ;
    
    /// <summary>
    /// Initializes a static-deletion operation from the large-scale payload.
    /// </summary>
    /// <param name="reader">The payload reader positioned at the delete-statics definition.</param>
    public LsDeleteStatics(ref SpanReader reader)
    {
        var count = reader.ReadUInt16();
        TileIds = new ushort[count];
        for (int i = 0; i < count; i++)
        {
            // The client transmits statics using the 0x4000 display offset also used by radar and tile pickers.
            TileIds[i] = (ushort)(reader.ReadUInt16() - 0x4000);
        }
        MinZ = reader.ReadSByte();
        MaxZ = reader.ReadSByte();
    }
}

/// <summary>
/// Describes a static-insertion operation with probability and placement rules.
/// </summary>
public class LsInsertStatics : LargeScaleOperation
{
    /// <summary>
    /// Gets the static tile identifiers that may be inserted.
    /// </summary>
    public ushort[] TileIds;

    /// <summary>
    /// Gets the per-tile insertion probability used by the operation.
    /// </summary>
    public byte Probability;

    /// <summary>
    /// Gets the placement strategy used to determine the inserted static altitude.
    /// </summary>
    public StaticsPlacement PlacementType;

    /// <summary>
    /// Gets the fixed altitude used when <see cref="PlacementType"/> is <see cref="StaticsPlacement.Fix"/>.
    /// </summary>
    public sbyte FixedZ;
    
    /// <summary>
    /// Initializes a static-insertion operation from the large-scale payload.
    /// </summary>
    /// <param name="reader">The payload reader positioned at the insert-statics definition.</param>
    public LsInsertStatics(ref SpanReader reader)
    {
        var count = reader.ReadUInt16();
        TileIds = new ushort[count];
        for (int i = 0; i < count; i++)
        {
            // The client transmits statics using the 0x4000 display offset also used by radar and tile pickers.
            TileIds[i] = (ushort)(reader.ReadUInt16() - 0x4000);
        }
        Probability = reader.ReadByte();
        PlacementType = (StaticsPlacement)reader.ReadByte();
        if (PlacementType == StaticsPlacement.Fix)
        {
            FixedZ = reader.ReadSByte();
        }
    }

    /// <summary>
    /// Validates that every requested static tile identifier exists in tiledata.
    /// </summary>
    /// <param name="landscape">The landscape that will execute the operation.</param>
    public override void Validate(ServerLandscape landscape)
    {
        foreach (var tileId in TileIds)
        {
            landscape.AssertStaticTileId(tileId);
        }
    }
}