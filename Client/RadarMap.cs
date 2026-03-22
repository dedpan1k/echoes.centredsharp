using System.Buffers;
using CentrED.Network;

namespace CentrED.Client;

/// <summary>
/// Represents a callback raised when a radar checksum arrives.
/// </summary>
/// <param name="checksum">The checksum value.</param>
public delegate void RadarChecksum(uint checksum);

/// <summary>
/// Represents a callback raised when a full radar payload arrives.
/// </summary>
/// <param name="data">The radar pixel data.</param>
public delegate void RadarData(ReadOnlySpan<ushort> data);

/// <summary>
/// Represents a callback raised when a single radar pixel changes.
/// </summary>
/// <param name="x">The radar X coordinate.</param>
/// <param name="y">The radar Y coordinate.</param>
/// <param name="color">The new radar color.</param>
public delegate void RadarUpdate(ushort x, ushort y, ushort color);

/// <summary>
/// Dispatches radar packets coming from the server.
/// </summary>
public class RadarMap
{
    private static PacketHandler<CentrEDClient>?[] Handlers { get; }

    static RadarMap()
    {
        Handlers = new PacketHandler<CentrEDClient>?[0x100];

        Handlers[0x01] = new PacketHandler<CentrEDClient>(0, OnRadarChecksumPacket);
        Handlers[0x02] = new PacketHandler<CentrEDClient>(0, OnRadarMapPacket);
        Handlers[0x03] = new PacketHandler<CentrEDClient>(0, OnUpdateRadarPacket);
    }

    /// <summary>
    /// Dispatches an incoming radar packet.
    /// </summary>
    /// <param name="reader">The packet reader positioned after the outer header.</param>
    /// <param name="ns">The client network session.</param>
    public static void OnRadarHandlerPacket(SpanReader reader, NetState<CentrEDClient> ns)
    {
        ns.LogDebug("Client OnRadarHandlerPacket");
        var id = reader.ReadByte();
        var packetHandler = Handlers[id];
        packetHandler?.OnReceive(reader, ns);
    }

    /// <summary>
    /// Handles a radar-checksum response.
    /// </summary>
    /// <param name="reader">The packet payload reader.</param>
    /// <param name="ns">The client network session.</param>
    private static void OnRadarChecksumPacket(SpanReader reader, NetState<CentrEDClient> ns)
    {
        var checksum = reader.ReadUInt32();
        ns.Parent.OnRadarChecksum(checksum);
    }

    /// <summary>
    /// Handles a full radar-map payload.
    /// </summary>
    /// <param name="reader">The packet payload reader.</param>
    /// <param name="ns">The client network session.</param>
    private static unsafe void OnRadarMapPacket(SpanReader reader, NetState<CentrEDClient> ns)
    {
        var length = ns.Parent.Width * ns.Parent.Height;
        var byteLength = length * 2;
        var data = new ushort[length];
        fixed (byte* bufferPtr = &reader.Buffer[reader.Position])
        fixed (ushort* dataPtr = &data[0])
        {
            Buffer.MemoryCopy(bufferPtr, dataPtr, byteLength, byteLength);
        }
        ns.Parent.OnRadarData(data);
    }

    /// <summary>
    /// Handles a single radar-pixel update.
    /// </summary>
    /// <param name="reader">The packet payload reader.</param>
    /// <param name="ns">The client network session.</param>
    private static void OnUpdateRadarPacket(SpanReader reader, NetState<CentrEDClient> ns)
    {
        var x = reader.ReadUInt16();
        var y = reader.ReadUInt16();
        var color = reader.ReadUInt16();
        ns.Parent.OnRadarUpdate(x, y, color);
    }
}