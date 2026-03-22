using System.Xml;
using CentrED.Network;

namespace CentrED.Server.Config;

/// <summary>
/// Defines a named set of editable rectangles used to restrict user access.
/// </summary>
public class Region(string name)
{
    /// <summary>
    /// Gets or sets the display name of the region.
    /// </summary>
    public string Name { get; set; } = name;

    /// <summary>
    /// Gets or sets the rectangles that make up the region.
    /// </summary>
    public List<RectU16> Area { get; set; } = [];
    
    /// <summary>
    /// Writes the region into the server XML format.
    /// </summary>
    /// <param name="writer">The XML writer that receives the region payload.</param>
    internal void Write(XmlWriter writer)
    {
        writer.WriteStartElement("Region");
        writer.WriteElementString("Name", Name);
        writer.WriteStartElement("Area");
        foreach (var area in Area)
        {
            writer.WriteStartElement("Rect");
            writer.WriteAttributeString("x1", XmlConvert.ToString(area.X1));
            writer.WriteAttributeString("y1", XmlConvert.ToString(area.Y1));
            writer.WriteAttributeString("x2", XmlConvert.ToString(area.X2));
            writer.WriteAttributeString("y2", XmlConvert.ToString(area.Y2));
            writer.WriteEndElement();//Rect
        }
        writer.WriteEndElement();//Area
        writer.WriteEndElement();//Region
    }

    /// <summary>
    /// Reads a region from the server XML format.
    /// </summary>
    /// <param name="reader">The XML reader positioned at the region payload.</param>
    /// <returns>The deserialized region.</returns>
    internal static Region Read(XmlReader reader)
    {
        var result = new Region("");
        using XmlReader sub = reader.ReadSubtree();
        sub.Read();
        while (sub.Read())
        {
            if (sub.NodeType == XmlNodeType.Element)
            {
                switch (sub.Name)
                {
                    case "Name":
                        result.Name = sub.ReadElementContentAsString();
                        break;
                    case "Rect":
                        var x1 = XmlConvert.ToUInt16(sub.GetAttribute("x1") ?? "0");
                        var y1 = XmlConvert.ToUInt16(sub.GetAttribute("y1") ?? "0");
                        var x2 = XmlConvert.ToUInt16(sub.GetAttribute("x2") ?? "0");
                        var y2 = XmlConvert.ToUInt16(sub.GetAttribute("y2") ?? "0");
                        result.Area.Add(new RectU16(x1, y1, x2, y2));
                        break;
                }
            }
        }
        return result;
    }

    public override string ToString()
    {
        return $"{nameof(Name)}: {Name}, {nameof(Area)}: [{String.Join(",", Area)}]";
    }
}