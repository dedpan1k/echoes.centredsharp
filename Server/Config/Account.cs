using System.Xml;
using CentrED.Utility;

namespace CentrED.Server.Config;

/// <summary>
/// Represents a persisted user account, including credentials, access level, restrictions, and last position.
/// </summary>
public class Account(string name, string password, AccessLevel accessLevel, List<String> regions)
{
    /// <summary>
    /// Initializes an empty account used during XML deserialization.
    /// </summary>
    public Account() : this("","", AccessLevel.None, [])
    {
    }
    
    /// <summary>
    /// Gets or sets the account name.
    /// </summary>
    public string Name { get; set; } = name;

    /// <summary>
    /// Gets or sets the hashed account password.
    /// </summary>
    public string PasswordHash { get; set; } = Crypto.Md5Hash(password);

    /// <summary>
    /// Gets or sets the account access level.
    /// </summary>
    public AccessLevel AccessLevel { get; set; } = accessLevel;

    /// <summary>
    /// Gets or sets the last known client position for the account.
    /// </summary>
    public LastPos LastPos { get; set; } = new();

    /// <summary>
    /// Gets or sets the region names assigned to the account.
    /// </summary>
    public List<string> Regions { get; set; } = regions;

    /// <summary>
    /// Gets or sets the last successful login time.
    /// </summary>
    public DateTime LastLogon { get; set; } = DateTime.MinValue;

    /// <summary>
    /// Updates the account password hash from a plaintext password.
    /// </summary>
    /// <param name="password">The new plaintext password.</param>
    public void UpdatePassword(string password)
    {
        PasswordHash = Crypto.Md5Hash(password);
    }

    /// <summary>
    /// Verifies whether a plaintext password matches the stored hash.
    /// </summary>
    /// <param name="password">The plaintext password to verify.</param>
    /// <returns><see langword="true"/> when the password matches; otherwise, <see langword="false"/>.</returns>
    public bool CheckPassword(string password)
    {
        return PasswordHash.Equals(Crypto.Md5Hash(password), StringComparison.InvariantCultureIgnoreCase);
    }
    
    /// <summary>
    /// Writes the account into the server XML format.
    /// </summary>
    /// <param name="writer">The XML writer that receives the account payload.</param>
    internal void Write(XmlWriter writer)
    {
        writer.WriteStartElement("Account");
        writer.WriteElementString("Name", Name);
        writer.WriteElementString("PasswordHash", PasswordHash);
        writer.WriteElementString("AccessLevel", XmlConvert.ToString((int)AccessLevel));
        
        writer.WriteStartElement("LastPos");
        writer.WriteAttributeString("x", XmlConvert.ToString(LastPos.X));
        writer.WriteAttributeString("y", XmlConvert.ToString(LastPos.Y));
        writer.WriteEndElement(); //LastPos
        
        writer.WriteStartElement("Regions");
        foreach (var region in Regions)
            writer.WriteElementString("Region", region);
        writer.WriteEndElement(); //Regions
        
        writer.WriteElementString("LastLogon", XmlConvert.ToString(LastLogon, XmlDateTimeSerializationMode.Local));
        
        writer.WriteEndElement(); //Account
    }

    /// <summary>
    /// Reads an account from the server XML format.
    /// </summary>
    /// <param name="reader">The XML reader positioned at the account payload.</param>
    /// <returns>The deserialized account.</returns>
    internal static Account Read(XmlReader reader)
    {
        var result = new Account();
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
                    case "PasswordHash":
                        result.PasswordHash = sub.ReadElementContentAsString();
                        break;
                    case "AccessLevel":
                        result.AccessLevel = (AccessLevel)sub.ReadElementContentAsInt();
                        break;
                    case "LastPos":
                        var x = XmlConvert.ToUInt16(sub.GetAttribute("x") ?? "0");
                        var y = XmlConvert.ToUInt16(sub.GetAttribute("y") ?? "0");
                        result.LastPos = new LastPos(x, y);
                        break;
                    case "Region":
                        result.Regions.Add(sub.ReadElementContentAsString());
                        break;
                    case "LastLogon":
                        result.LastLogon = sub.ReadElementContentAsDateTime();
                        break;
                }
            }
        }
        return result;
    }

    public override string ToString()
    {
        return $"{nameof(Name)}: {Name}, " + $"{nameof(PasswordHash)}: [redacted], " +
               $"{nameof(AccessLevel)}: {AccessLevel}, " + $"{nameof(LastPos)}: {LastPos}, " +
               $"{nameof(Regions)}: {String.Join(",", Regions)}";
    }
}