using System.Xml;

namespace CentrED.Server.Config;

/// <summary>
/// Configures automatic backup rotation for the server map files.
/// </summary>
public class Autobackup
{
    /// <summary>
    /// Gets or sets a value indicating whether automatic backups are enabled.
    /// </summary>
    public bool Enabled { get; set; } = false;

    /// <summary>
    /// Gets or sets the directory used to store rotated backups.
    /// </summary>
    public string Directory { get; set; } = "backups";

    /// <summary>
    /// Gets or sets the number of rotated backups to keep.
    /// </summary>
    public uint MaxBackups { get; set; } = 7;

    /// <summary>
    /// Gets or sets the interval between automatic backups.
    /// </summary>
    public TimeSpan Interval { get; set; } = TimeSpan.FromHours(12);
    
    /// <summary>
    /// Writes the autobackup settings into the server XML format.
    /// </summary>
    /// <param name="writer">The XML writer that receives the autobackup payload.</param>
    internal void Write(XmlWriter writer)
    {
        writer.WriteStartElement("AutoBackup");
        writer.WriteElementString("Enabled", XmlConvert.ToString(Enabled));
        writer.WriteElementString("Directory", Directory);
        writer.WriteElementString("MaxBackups", XmlConvert.ToString(MaxBackups));
        writer.WriteElementString("Interval", XmlConvert.ToString(Interval));
        writer.WriteEndElement();
    }

    /// <summary>
    /// Reads the autobackup settings from the server XML format.
    /// </summary>
    /// <param name="reader">The XML reader positioned at the autobackup payload.</param>
    /// <returns>The deserialized autobackup settings.</returns>
    internal static Autobackup Read(XmlReader reader)
    {
        var result = new Autobackup();
        using XmlReader sub = reader.ReadSubtree();
        sub.Read();
        while (sub.Read())
        {
            if (sub.NodeType == XmlNodeType.Element)
            {
                switch (sub.Name)
                {
                    case "Enabled":
                        result.Enabled = sub.ReadElementContentAsBoolean();
                        break;
                    case "Directory":
                        result.Directory = sub.ReadElementContentAsString();
                        break;
                    case "MaxBackups":
                        result.MaxBackups = XmlConvert.ToUInt32(sub.ReadElementContentAsString());
                        break;
                    case "Interval":
                        result.Interval = XmlConvert.ToTimeSpan(sub.ReadElementContentAsString());
                        break;
                }
            }
        }
        return result;
    }
}