using System.Xml;

namespace CentrED.Server.Config;

/// <summary>
/// Represents the persisted server configuration, including network settings, file paths, accounts, and regions.
/// </summary>
public class ConfigRoot
{
    /// <summary>
    /// Gets the default configuration path derived from the current executable name.
    /// </summary>
    private static string DefaultPath =>
        Path.GetFullPath(Path.ChangeExtension(Application.GetCurrentExecutable(), ".xml"));

    /// <summary>
    /// Defines the current configuration schema version.
    /// </summary>
    public const int CurrentVersion = 5;

    /// <summary>
    /// Gets or sets the persisted configuration schema version.
    /// </summary>
    public int Version { get; set; } = CurrentVersion;

    /// <summary>
    /// Gets or sets a value indicating whether CentrED+ protocol extensions are enabled.
    /// </summary>
    public bool CentrEdPlus { get; set; }

    /// <summary>
    /// Gets or sets the TCP port used by the server listener.
    /// </summary>
    public int Port { get; set; } = 2597;

    /// <summary>
    /// Gets or sets the map-file configuration.
    /// </summary>
    public Map Map { get; set; } = new();

    /// <summary>
    /// Gets or sets the tiledata file path.
    /// </summary>
    public string Tiledata { get; set; } = "tiledata.mul";

    /// <summary>
    /// Gets or sets the radar color lookup file path.
    /// </summary>
    public string Radarcol { get; set; } = "radarcol.mul";

    /// <summary>
    /// Gets or sets the hue table file path.
    /// </summary>
    public string Hues { get; set; } = "hues.mul";

    /// <summary>
    /// Gets or sets the configured user accounts.
    /// </summary>
    public List<Account> Accounts { get; set; } = new();

    /// <summary>
    /// Gets or sets the configured editable regions.
    /// </summary>
    public List<Region> Regions { get; set; } = new();

    /// <summary>
    /// Gets or sets the automatic-backup configuration.
    /// </summary>
    public Autobackup AutoBackup { get; set; } = new();
    
    /// <summary>
    /// Gets or sets a value indicating whether the configuration has unsaved changes.
    /// </summary>
    public bool Changed { get; set; }

    /// <summary>
    /// Gets or sets the path of the configuration file currently being edited.
    /// </summary>
    public string FilePath { get; set; } = DefaultPath;

    /// <summary>
    /// Marks the configuration as dirty so it will be written on the next flush.
    /// </summary>
    public void Invalidate()
    {
        Changed = true;
    }

    /// <summary>
    /// Writes the configuration to disk when it contains unsaved changes.
    /// </summary>
    public void Flush()
    {
        if (!Changed)
            return;
        
        var settings = new XmlWriterSettings
        {
            Indent = true,
            Encoding = System.Text.Encoding.UTF8
        };
        
        using XmlWriter writer = XmlWriter.Create(FilePath, settings);
        writer.WriteStartDocument();
        Write(writer);
        writer.WriteEndDocument();
        Changed = false;
    }

    /// <summary>
    /// Loads configuration from disk or interactively creates a new configuration when none exists.
    /// </summary>
    /// <param name="args">Command-line arguments that may override the configuration path.</param>
    /// <returns>The loaded or newly created configuration.</returns>
    public static ConfigRoot Init(string[] args)
    {
        var index = Array.IndexOf(args, "-c");
        var configPath = DefaultPath;
        if (index != -1)
        {
            configPath = args[index + 1];
        }
        else if (args.Length == 1)
        {
            configPath = args[0];
        }
        Console.WriteLine($"Config file: {configPath}");

        if (File.Exists(configPath))
        {
            return Read(configPath);
        }
        else
        {
            return Prompt(configPath);
        }
    }

    /// <summary>
    /// Reads a configuration file from disk and upgrades it to the current schema version when needed.
    /// </summary>
    /// <param name="path">The configuration file path.</param>
    /// <returns>The loaded configuration.</returns>
    public static ConfigRoot Read(string path)
    {
        using var reader = XmlReader.Create(path);
        var result = Read(reader);

        if (result.Version != CurrentVersion)
        {
            result.Version = CurrentVersion;
            result.Invalidate(); // fill in missing entries with default values
            result.Flush();
        }
        
        result.Regions.RemoveAll(r => string.IsNullOrEmpty(r.Name));
        result.Accounts.RemoveAll(a => string.IsNullOrEmpty(a.Name));
        
        result.FilePath = path;
        return result;
    }

    /// <summary>
    /// Interactively prompts for enough information to create a first-run configuration file.
    /// </summary>
    /// <param name="path">The configuration path that will be written.</param>
    /// <returns>The newly created configuration.</returns>
    private static ConfigRoot Prompt(string path)
    {
        string? input;
        ConfigRoot result = new()
        {
            FilePath = path
        };
        Console.WriteLine("Configuring Network");
        Console.WriteLine("===================");
        Console.Write($"Port [{result.Port}]: ");
        input = Console.ReadLine();
        if (!string.IsNullOrEmpty(input) && Int32.TryParse(input, out int port))
        {
            result.Port = port;
        }

        Console.WriteLine("Configuring Paths");
        Console.WriteLine("=================");
        Console.Write($"map [{result.Map.MapPath}]: ");
        input = Console.ReadLine();
        if (!string.IsNullOrEmpty(input))
        {
            result.Map.MapPath = input;
        }

        Console.Write($"statics [{result.Map.Statics}]: ");
        input = Console.ReadLine();
        if (!string.IsNullOrEmpty(input))
        {
            result.Map.Statics = input;
        }

        Console.Write($"staidx [{result.Map.StaIdx}]: ");
        input = Console.ReadLine();
        if (!string.IsNullOrEmpty(input))
        {
            result.Map.StaIdx = input;
        }

        Console.Write($"tiledata [{result.Tiledata}]: ");
        input = Console.ReadLine();
        if (!string.IsNullOrEmpty(input))
        {
            result.Tiledata = input;
        }

        Console.Write($"radarcol [{result.Radarcol}]: ");
        input = Console.ReadLine();
        if (!string.IsNullOrEmpty(input))
        {
            result.Radarcol = input;
        }

        Console.WriteLine("Parameters");
        Console.WriteLine("==========");
        Console.Write($"Map width [{result.Map.Width}]: ");
        input = Console.ReadLine();
        if (!string.IsNullOrEmpty(input) && UInt16.TryParse(input, out ushort width))
        {
            result.Map.Width = width;
        }

        Console.Write($"Map height [{result.Map.Height}]: ");
        input = Console.ReadLine();
        if (!string.IsNullOrEmpty(input) && UInt16.TryParse(input, out ushort height))
        {
            result.Map.Height = height;
        }

        Console.WriteLine("Admin account");
        Console.WriteLine("=============");
        Console.Write("Account name: ");
        var accountName = Console.ReadLine()!;

        Console.Write("Password [hidden]: ");
        string password = "";
        while (true)
        {
            var key = Console.ReadKey(true);
            if (key.Key == ConsoleKey.Enter)
                break;
            if (key.Key == ConsoleKey.Backspace)
            {
                if (password.Length > 0)
                    password = password.Remove(password.Length - 1);
            }
            else
            {
                password += key.KeyChar;
            }
        }

        result.Accounts.Add(new Account(accountName, password, AccessLevel.Administrator, []));
        result.Invalidate();
        result.Flush();

        return result;
    }
    
    /// <summary>
    /// Writes the configuration object into the server XML format.
    /// </summary>
    /// <param name="writer">The XML writer that receives the configuration payload.</param>
    internal void Write(XmlWriter writer)
    {
        writer.WriteStartElement("CEDConfig");
        writer.WriteAttributeString("Version", XmlConvert.ToString(CurrentVersion));
            
        writer.WriteElementString("CentrEdPlus", XmlConvert.ToString(CentrEdPlus));
        writer.WriteElementString("Port", XmlConvert.ToString(Port));
        Map.Write(writer);
        writer.WriteElementString("Tiledata", Tiledata);
        writer.WriteElementString("Radarcol", Radarcol);
        writer.WriteElementString("Hues", Hues);
        
        writer.WriteStartElement("Accounts");
        foreach (var account in Accounts)
        {
            account.Write(writer);
        }
        writer.WriteEndElement();
        writer.WriteStartElement("Regions");
        foreach (var region in Regions)
        {
            region.Write(writer);
        }
        writer.WriteEndElement();
        AutoBackup.Write(writer);

        writer.WriteEndElement();
    }

    /// <summary>
    /// Reads a configuration object from the server XML format.
    /// </summary>
    /// <param name="reader">The XML reader positioned at the configuration payload.</param>
    /// <returns>The deserialized configuration.</returns>
    internal static ConfigRoot Read(XmlReader reader)
    {
        ConfigRoot result = new ConfigRoot();

        while (reader.Read())
        {
            if (reader.NodeType == XmlNodeType.Element)
            {
                switch (reader.Name)
                {
                    case "CEDConfig":
                        result.Version = XmlConvert.ToInt32(reader.GetAttribute("version") ?? CurrentVersion.ToString());
                        break;

                    case "CentrEdPlus":
                        result.CentrEdPlus = reader.ReadElementContentAsBoolean();
                        break;

                    case "Port":
                        result.Port = reader.ReadElementContentAsInt();
                        break;

                    case "Map":
                        result.Map = Map.Read(reader);
                        break;

                    case "Tiledata":
                        result.Tiledata = reader.ReadElementContentAsString();
                        break;

                    case "Radarcol":
                        result.Radarcol = reader.ReadElementContentAsString();
                        break;

                    case "Hues":
                        result.Hues = reader.ReadElementContentAsString();
                        break;

                    case "Account":
                        result.Accounts.Add(Account.Read(reader));
                        break;

                    case "Region":
                        result.Regions.Add(Region.Read(reader));
                        break;

                    case "AutoBackup":
                        result.AutoBackup = Autobackup.Read(reader);
                        break;
                }
            }
        }
        return result;
    }
}