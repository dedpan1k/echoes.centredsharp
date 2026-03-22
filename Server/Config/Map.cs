using System.Xml;

namespace CentrED.Server.Config;

/// <summary>
/// Defines the file paths and dimensions for the map and statics data served by the server.
/// </summary>
public class Map
{
    /// <summary>
    /// Gets or sets the map file path.
    /// </summary>
    public string MapPath { get; set; } = "map0.mul";

    /// <summary>
    /// Gets or sets the staidx file path.
    /// </summary>
    public string StaIdx { get; set; } = "staidx0.mul";

    /// <summary>
    /// Gets or sets the statics file path.
    /// </summary>
    public string Statics { get; set; } = "statics0.mul";

    /// <summary>
    /// Gets or sets the map width in 8x8 blocks.
    /// </summary>
    public ushort Width { get; set; } = 896;

    /// <summary>
    /// Gets or sets the map height in 8x8 blocks.
    /// </summary>
    public ushort Height { get; set; } = 512;

    /// <summary>
    /// Writes the map configuration into the server XML format.
    /// </summary>
    /// <param name="writer">The XML writer that receives the map configuration.</param>
    internal void Write(XmlWriter writer)
    {
        writer.WriteStartElement("Map");
        writer.WriteElementString("Map", MapPath);
        writer.WriteElementString("StaIdx", StaIdx);
        writer.WriteElementString("Statics", Statics);
        writer.WriteElementString("Width", XmlConvert.ToString(Width));
        writer.WriteElementString("Height", XmlConvert.ToString(Height));
        writer.WriteEndElement();
    }

    /// <summary>
    /// Reads the map configuration from the server XML format.
    /// </summary>
    /// <param name="reader">The XML reader positioned at the map payload.</param>
    /// <returns>The deserialized map configuration.</returns>
    internal static Map Read(XmlReader reader)
    {
        var result = new Map();
        using XmlReader sub = reader.ReadSubtree();
        sub.Read();
        while (sub.Read())
        {
            if (sub.NodeType == XmlNodeType.Element)
            {
                switch (sub.Name)
                {
                    case "Map":
                        result.MapPath = sub.ReadElementContentAsString();
                        break;
                    case "StaIdx":
                        result.StaIdx = sub.ReadElementContentAsString();
                        break;
                    case "Statics":
                        result.Statics = sub.ReadElementContentAsString();
                        break;
                    case "Width":
                        result.Width = (ushort)sub.ReadElementContentAsInt();
                        break;
                    case "Height":
                        result.Height = (ushort)sub.ReadElementContentAsInt();
                        break;
                }
            }
        }
        return result;
    }

    public override string ToString()
    {
        return
            $"{nameof(MapPath)}: {MapPath}, {nameof(StaIdx)}: {StaIdx}, {nameof(Statics)}: {Statics}, {nameof(Width)}: {Width}, {nameof(Height)}: {Height}";
    }
}