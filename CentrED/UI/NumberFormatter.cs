namespace CentrED.UI;

/// <summary>
/// Defines the supported ways tile and object identifiers can be presented in the UI.
/// </summary>
public enum NumberDisplayFormat
{
    /// <summary>
    /// Show the identifier in hexadecimal only, padded to four digits.
    /// Example: 0x0A3F.
    /// </summary>
    HEX,

    /// <summary>
    /// Show the identifier in decimal only.
    /// Example: 2623.
    /// </summary>
    DEC,

    /// <summary>
    /// Show the hexadecimal form first, followed by the decimal form in parentheses.
    /// Example: 0x0A3F (2623).
    /// </summary>
    HEX_DEC,

    /// <summary>
    /// Show the decimal form first, followed by the hexadecimal form in parentheses.
    /// Example: 2623 (0x0A3F).
    /// </summary>
    DEC_HEX
}

/// <summary>
/// Provides extension methods that format numeric identifiers according to the user's
/// configured display preference or an explicitly supplied display mode.
/// </summary>
public static class NumberFormatter
{
    /// <summary>
    /// Formats a signed integer identifier using the current UI configuration.
    /// </summary>
    public static string FormatId(this int value)
    {
        return FormatId(value, Config.Instance.NumberFormat);
    }
    
    /// <summary>
    /// Formats an unsigned integer identifier using the current UI configuration.
    /// The value is routed through the <see cref="int"/> overload because the UI treats
    /// identifiers as signed values for display purposes.
    /// </summary>
    public static string FormatId(this uint value)
    {
        return FormatId((int)value, Config.Instance.NumberFormat);
    }
        
    /// <summary>
    /// Formats a 16-bit identifier using the current UI configuration.
    /// </summary>
    public static string FormatId(this ushort value)
    {
        return FormatId(value, Config.Instance.NumberFormat);
    }

    /// <summary>
    /// Formats a 16-bit identifier using an explicit display mode.
    /// </summary>
    public static string FormatId(this ushort value, NumberDisplayFormat format)
    {
        return FormatId((int)value, format);
    }
    
    /// <summary>
    /// Formats an identifier according to the requested display mode.
    /// Hex values are padded to four digits because Ultima-style asset identifiers are
    /// commonly discussed in that width throughout the editor.
    /// </summary>
    public static string FormatId(this int value, NumberDisplayFormat format)
    {
        return format switch
        {
            NumberDisplayFormat.HEX => $"0x{value:X4}",
            NumberDisplayFormat.DEC => $"{value}",
            NumberDisplayFormat.HEX_DEC => $"0x{value:X4} ({value})",
            NumberDisplayFormat.DEC_HEX => $"{value} (0x{value:X4})",
            _ => $"0x{value:X4}"
        };
    }
}
