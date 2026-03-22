using System.Numerics;

namespace CentrED.UI;

/// <summary>
/// Centralized set of reusable RGBA colors for ImGui drawing and text helpers.
/// Keeping them here avoids repeating raw vector literals throughout the UI layer.
/// </summary>
public static class ImGuiColor
{
    /// <summary>
    /// Fully opaque red.
    /// </summary>
    public static readonly Vector4 Red = new(1, 0, 0, 1);

    /// <summary>
    /// Fully opaque green.
    /// </summary>
    public static readonly Vector4 Green = new(0, 1, 0, 1);

    /// <summary>
    /// Blue preset used by the current UI palette.
    /// </summary>
    public static readonly Vector4 Blue = new(0, 0, 1, 1);

    /// <summary>
    /// Fully opaque magenta/pink.
    /// </summary>
    public static readonly Vector4 Pink = new(1, 0, 1, 1);
}