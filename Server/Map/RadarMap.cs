using System.Buffers;
using CentrED.Network;
using CentrED.Utility;

namespace CentrED.Server.Map;

/// <summary>
/// Maintains the server-side radar image and serializes radar updates for clients.
/// </summary>
public class RadarMap
{
    /// <summary>
    /// Builds the initial radar image from the landscape and radar color table.
    /// </summary>
    /// <param name="landscape">The landscape that owns the radar data.</param>
    /// <param name="mapReader">The reader used to sample land tiles.</param>
    /// <param name="staidxReader">The reader used to locate static blocks.</param>
    /// <param name="staticsReader">The reader used to sample static tiles.</param>
    /// <param name="radarcolPath">The path to the radar color lookup table.</param>
    public RadarMap
    (
        ServerLandscape landscape,
        BinaryReader mapReader,
        BinaryReader staidxReader,
        BinaryReader staticsReader,
        string radarcolPath
    )
    {
        var buffer = File.ReadAllBytes(radarcolPath);
        _radarColors = new ushort[buffer.Length / sizeof(ushort)];
        Buffer.BlockCopy(buffer, 0, _radarColors, 0, buffer.Length);

        _width = landscape.Width;
        _height = landscape.Height;
        _radarMap = new ushort[_width * _height];
        for (ushort x = 0; x < _width; x++)
        {
            for (ushort y = 0; y < _height; y++)
            {
                var block = landscape.GetBlockNumber(x, y);
                mapReader.BaseStream.Seek(landscape.GetMapOffset(x, y) + 4, SeekOrigin.Begin);
                var landTile = new LandTile(mapReader, 0, 0);
                _radarMap[block] = _radarColors[landTile.Id];

                staidxReader.BaseStream.Seek(landscape.GetStaidxOffset(x, y), SeekOrigin.Begin);
                var index = new GenericIndex(staidxReader);
                var staticsBlock = new StaticBlock(landscape, x, y, staticsReader, index);

                var highestZ = landTile.Z;
                foreach (var staticTile in staticsBlock.GetTiles(0, 0))
                {
                    if (staticTile.Z >= highestZ)
                    {
                        highestZ = staticTile.Z;
                        var id = staticTile.Id + 0x4000;
                        if (id > _radarColors.Length)
                        {
                            Console.WriteLine($"Invalid static tile {staticTile.Id} at block {x},{y}");
                            id = 0x4000;
                        }
                        _radarMap[block] = _radarColors[id];
                    }
                }
            }
        }
    }
    
    private ushort _width;
    private ushort _height;
    private ushort[] _radarColors;
    private ushort[] _radarMap;
    private List<Packet>? _packets;

    /// <summary>
    /// Dispatches radar subcommands for checksum and full radar-map requests.
    /// </summary>
    /// <param name="reader">The payload reader containing the radar subcommand.</param>
    /// <param name="ns">The client session requesting radar data.</param>
    internal void OnRadarHandlingPacket(SpanReader reader, NetState<CEDServer> ns)
    {
        ns.LogDebug("OnRadarHandlingPacket");
        if (!ns.ValidateAccess(AccessLevel.View))
            return;
        var subpacket = reader.ReadByte();
        switch (subpacket)
        {
            case 0x01: ns.Send(new RadarChecksumPacket(_radarMap)); break;
            case 0x02: ns.SendCompressed(new RadarMapPacket(_radarMap)); break;
            default: throw new ArgumentException($"Invalid RadarMap SubPacket {subpacket}");
        }
    }

    /// <summary>
    /// Updates one radar pixel and either batches or broadcasts the corresponding packet.
    /// </summary>
    /// <param name="ns">The client session that initiated the change.</param>
    /// <param name="x">The radar-block X coordinate.</param>
    /// <param name="y">The radar-block Y coordinate.</param>
    /// <param name="tileId">The tile identifier used to resolve the radar color.</param>
    public void Update(NetState<CEDServer> ns, ushort x, ushort y, ushort tileId)
    {
        var block = x * _height + y;
        var color = _radarColors[tileId];
        if (_radarMap[block] != color)
        {
            _radarMap[block] = color;
            var packet = new UpdateRadarPacket(x, y, color);
            if (_packets != null)
            {
                _packets.Add(packet);
            }
            else
            {
                ns.Parent.Broadcast(packet);
            }
        }
    }

    /// <summary>
    /// Begins batching radar updates for a large multi-block edit.
    /// </summary>
    public void BeginUpdate()
    {
        if (_packets != null)
            throw new InvalidOperationException("RadarMap update is already in progress");

        _packets = new List<Packet>();
    }

    /// <summary>
    /// Ends a batched radar update and sends either individual updates or a full radar image refresh.
    /// </summary>
    /// <param name="ns">The client session that initiated the batched update.</param>
    public void EndUpdate(NetState<CEDServer> ns)
    {
        if (_packets == null)
            throw new InvalidOperationException("RadarMap update isn't in progress");

        // Large edit bursts are cheaper to resend as a complete radar image than as thousands of point updates.
        if (_packets.Count > 1024)
        {
            ns.SendCompressed(new RadarMapPacket(_radarMap));
        }
        else
        {
            foreach (var packet in _packets)
            {
                ns.Send(packet);
            }
        }
        _packets = null;
    }
}

/// <summary>
/// Sends a checksum for the current radar image so clients can detect whether they need a refresh.
/// </summary>
public class RadarChecksumPacket : Packet
{
    /// <summary>
    /// Initializes a radar checksum packet.
    /// </summary>
    /// <param name="radarMap">The radar image to checksum.</param>
    public RadarChecksumPacket(ushort[] radarMap) : base(0x0D, 0)
    {
        Writer.Write((byte)0x01);
        Writer.Write(Crypto.Crc32Checksum(radarMap));
    }
}

/// <summary>
/// Sends the full radar image to a client.
/// </summary>
public class RadarMapPacket : Packet
{
    /// <summary>
    /// Initializes a full radar-map packet.
    /// </summary>
    /// <param name="radarMap">The radar image to serialize.</param>
    public RadarMapPacket(ushort[] radarMap) : base(0x0D, 0)
    {
        Writer.Write((byte)0x02);
        byte[] buffer = new byte[Buffer.ByteLength(radarMap)];
        Buffer.BlockCopy(radarMap, 0, buffer, 0, buffer.Length);
        Writer.Write(buffer);
    }
}

/// <summary>
/// Sends a single radar pixel update.
/// </summary>
public class UpdateRadarPacket : Packet
{
    /// <summary>
    /// Initializes a radar update packet.
    /// </summary>
    /// <param name="x">The radar-block X coordinate.</param>
    /// <param name="y">The radar-block Y coordinate.</param>
    /// <param name="color">The new radar color value.</param>
    public UpdateRadarPacket(ushort x, ushort y, ushort color) : base(0x0D, 0)
    {
        Writer.Write((byte)0x03);
        Writer.Write(x);
        Writer.Write(y);
        Writer.Write(color);
    }
}