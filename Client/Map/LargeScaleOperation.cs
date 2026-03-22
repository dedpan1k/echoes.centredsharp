using static CentrED.Network.LSO;

namespace CentrED.Client.Map;

/// <summary>
/// Defines the serialization contract for client-side large-scale operations.
/// </summary>
public interface ILargeScaleOperation
{
    /// <summary>
    /// Writes the operation payload into a packet writer.
    /// </summary>
    /// <param name="writer">The writer receiving the payload.</param>
    public void Write(BinaryWriter writer);
}

/// <summary>
/// Describes a copy or move large-scale operation.
/// </summary>
public class LSOCopyMove : ILargeScaleOperation
{
    private readonly CopyMove type;
    private readonly int offsetX;
    private readonly int offsetY;
    private readonly bool erase;

    /// <summary>
    /// Initializes a copy or move large-scale operation.
    /// </summary>
    public LSOCopyMove(CopyMove type, bool erase, int offsetX, int offsetY)
    {
        this.type = type;
        this.erase = erase;
        this.offsetX = offsetX;
        this.offsetY = offsetY;
    }

    /// <inheritdoc />
    public void Write(BinaryWriter writer)
    {
        writer.Write((byte)type);
        writer.Write(offsetX);
        writer.Write(offsetY);
        writer.Write(erase);
    }
}

/// <summary>
/// Describes an altitude-adjustment large-scale operation.
/// </summary>
public class LSOSetAltitude : ILargeScaleOperation
{
    private SetAltitude type;
    private sbyte minZ;
    private sbyte maxZ;
    private sbyte relativeZ;

    /// <summary>
    /// Initializes a terrain-clamp altitude operation.
    /// </summary>
    public LSOSetAltitude(sbyte minZ, sbyte maxZ)
    {
        type = SetAltitude.Terrain;
        this.minZ = minZ;
        this.maxZ = maxZ;
    }

    /// <summary>
    /// Initializes a relative altitude operation.
    /// </summary>
    /// <param name="relativeZ">The relative altitude delta.</param>
    public LSOSetAltitude(sbyte relativeZ)
    {
        type = SetAltitude.Relative;
        this.relativeZ = relativeZ;
    }

    /// <inheritdoc />
    public void Write(BinaryWriter writer)
    {
        writer.Write((byte)type);
        switch (type)
        {
            case SetAltitude.Terrain:
                writer.Write(minZ);
                writer.Write(maxZ);
                break;
            case SetAltitude.Relative:
                writer.Write(relativeZ);
                break;
        }
    }
}

/// <summary>
/// Describes a terrain-drawing large-scale operation.
/// </summary>
public class LSODrawLand : ILargeScaleOperation
{
    private ushort[] tileIds;

    /// <summary>
    /// Initializes a draw-land operation.
    /// </summary>
    /// <param name="tileIds">The land tile ids to use.</param>
    public LSODrawLand(ushort[] tileIds)
    {
        this.tileIds = tileIds;
    }
    
    /// <inheritdoc />
    public void Write(BinaryWriter writer)
    {
        writer.Write((ushort)tileIds.Length);
        foreach (var tileId in tileIds)
        {
            writer.Write(tileId);
        }
    }
}

/// <summary>
/// Describes a static-deletion large-scale operation.
/// </summary>
public class LSODeleteStatics : ILargeScaleOperation
{
    private ushort[] tileIds;
    private sbyte minZ;
    private sbyte maxZ;

    /// <summary>
    /// Initializes a delete-statics operation.
    /// </summary>
    public LSODeleteStatics(ushort[] tileIds, sbyte minZ, sbyte maxZ)
    {
        this.tileIds = tileIds;
        this.minZ = minZ;
        this.maxZ = maxZ;
    }
    
    /// <inheritdoc />
    public void Write(BinaryWriter writer)
    {
        writer.Write((ushort)tileIds.Length);
        foreach (var tileId in tileIds)
        {
            writer.Write(tileId);
        }
        writer.Write(minZ);
        writer.Write(maxZ);
    }
}

/// <summary>
/// Describes a static-insertion large-scale operation.
/// </summary>
public class LSOAddStatics : ILargeScaleOperation
{
    private ushort[] tileIds;
    private byte chance;
    private StaticsPlacement placement;
    private sbyte fixedZ;

    /// <summary>
    /// Initializes an add-statics operation.
    /// </summary>
    public LSOAddStatics(ushort[] tileIds, byte chance, StaticsPlacement placement, sbyte fixedZ)
    {
        this.tileIds = tileIds;
        this.chance = chance;
        this.placement = placement;
        this.fixedZ = fixedZ;
    }

    /// <inheritdoc />
    public void Write(BinaryWriter writer)
    {
        writer.Write((ushort)tileIds.Length);
        foreach (var tileId in tileIds)
        {
            writer.Write(tileId);
        }
        writer.Write(chance);
        writer.Write((byte)placement);
        if (placement == StaticsPlacement.Fix)
        {
            writer.Write(fixedZ);
        }
    }
}