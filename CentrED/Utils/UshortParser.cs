using System.Globalization;

namespace CentrED.Utils;

/// <summary>
/// Parses unsigned 16-bit integer values from decimal or hexadecimal text.
/// </summary>
public static class UshortParser
{
    /// <summary>
    /// Parses a string into a <see cref="ushort"/> value.
    /// </summary>
    /// <param name="s">The input text to parse.</param>
    /// <returns>The parsed unsigned 16-bit integer.</returns>
    public static ushort Apply(string s)
    {
        // Treat a leading 0x prefix as hexadecimal; otherwise parse decimal.
        if (s.StartsWith("0x"))
        {
            return ushort.Parse(s[2..], NumberStyles.HexNumber);
        }
        return ushort.Parse(s, NumberStyles.Integer);
    }
}