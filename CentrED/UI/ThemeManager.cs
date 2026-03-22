using Hexa.NET.ImGui;
using Vector2 = System.Numerics.Vector2;
using Vector4 = System.Numerics.Vector4;

namespace CentrED.UI;

public enum UIThemeModeFilter
{
    All,
    Light,
    Dark,
}

public enum UIThemePreset
{
    GitHubLight,
    OneLight,
    CatppuccinLatte,
    GitHubDark,
    OneDark,
    TokyoNight,
    CatppuccinMocha,
}

/// <summary>
/// Applies a small set of curated global ImGui palettes inspired by popular editor themes.
/// </summary>
public static class ThemeManager
{
    public static readonly string[] PresetNames =
    [
        "GitHub Light",
        "One Light",
        "Catppuccin Latte",
        "GitHub Dark",
        "One Dark",
        "Tokyo Night",
        "Catppuccin Mocha",
    ];

    public static UIThemeModeFilter GetMode(UIThemePreset preset)
    {
        return preset switch
        {
            UIThemePreset.GitHubLight or UIThemePreset.OneLight or UIThemePreset.CatppuccinLatte => UIThemeModeFilter.Light,
            _ => UIThemeModeFilter.Dark,
        };
    }

    public static UIThemePreset[] GetPresets(UIThemeModeFilter filter)
    {
        return filter switch
        {
            UIThemeModeFilter.Light =>
            [
                UIThemePreset.GitHubLight,
                UIThemePreset.OneLight,
                UIThemePreset.CatppuccinLatte,
            ],
            UIThemeModeFilter.Dark =>
            [
                UIThemePreset.GitHubDark,
                UIThemePreset.OneDark,
                UIThemePreset.TokyoNight,
                UIThemePreset.CatppuccinMocha,
            ],
            _ => Enum.GetValues<UIThemePreset>(),
        };
    }

    public static string[] GetPresetNames(UIThemeModeFilter filter)
    {
        return GetPresets(filter).Select(GetPresetName).ToArray();
    }

    public static string GetPresetName(UIThemePreset preset)
    {
        return PresetNames[(int)preset];
    }

    public static void Apply(UIThemePreset preset)
    {
        switch (preset)
        {
            case UIThemePreset.GitHubLight:
                ApplyLightTheme(
                    text: Rgba(0x1F2328FF),
                    textDisabled: Rgba(0x6E7781FF),
                    windowBg: Rgba(0xFFFFFFFF),
                    surface: Rgba(0xF6F8FAFF),
                    surfaceAlt: Rgba(0xEFF2F5FF),
                    border: Rgba(0xD0D7DEFF),
                    accent: Rgba(0x0969DAFF),
                    accentHover: Rgba(0x1F6FEBFF),
                    accentActive: Rgba(0x0550AEFF));
                break;
            case UIThemePreset.OneLight:
                ApplyLightTheme(
                    text: Rgba(0x383A42FF),
                    textDisabled: Rgba(0x7F848EFF),
                    windowBg: Rgba(0xFAFBFCFF),
                    surface: Rgba(0xF0F2F5FF),
                    surfaceAlt: Rgba(0xE5E9F0FF),
                    border: Rgba(0xD7DAE0FF),
                    accent: Rgba(0x4078F2FF),
                    accentHover: Rgba(0x528BFFFF),
                    accentActive: Rgba(0x3767CCFF));
                break;
            case UIThemePreset.CatppuccinLatte:
                ApplyLightTheme(
                    text: Rgba(0x4C4F69FF),
                    textDisabled: Rgba(0x8C8FA1FF),
                    windowBg: Rgba(0xEFF1F5FF),
                    surface: Rgba(0xE6E9EFFF),
                    surfaceAlt: Rgba(0xDCE0E8FF),
                    border: Rgba(0xCCD0DAFF),
                    accent: Rgba(0x1E66F5FF),
                    accentHover: Rgba(0x7287FDFF),
                    accentActive: Rgba(0x114FCFFF));
                break;
            case UIThemePreset.GitHubDark:
                ApplyDarkTheme(
                    text: Rgba(0xE6EDF3FF),
                    textDisabled: Rgba(0x7D8590FF),
                    windowBg: Rgba(0x0D1117FF),
                    surface: Rgba(0x161B22FF),
                    surfaceAlt: Rgba(0x21262DFF),
                    border: Rgba(0x30363DFF),
                    accent: Rgba(0x2F81F7FF),
                    accentHover: Rgba(0x58A6FFFF),
                    accentActive: Rgba(0x1F6FEBFF));
                break;
            case UIThemePreset.OneDark:
                ApplyDarkTheme(
                    text: Rgba(0xABB2BFFF),
                    textDisabled: Rgba(0x7F848EFF),
                    windowBg: Rgba(0x282C34FF),
                    surface: Rgba(0x21252BFF),
                    surfaceAlt: Rgba(0x2C313CFF),
                    border: Rgba(0x3E4451FF),
                    accent: Rgba(0x61AFEFFF),
                    accentHover: Rgba(0x7BC6FFFF),
                    accentActive: Rgba(0x4B95D1FF));
                break;
            case UIThemePreset.TokyoNight:
                ApplyDarkTheme(
                    text: Rgba(0xC0CAF5FF),
                    textDisabled: Rgba(0x565F89FF),
                    windowBg: Rgba(0x1A1B26FF),
                    surface: Rgba(0x1F2335FF),
                    surfaceAlt: Rgba(0x24283BFF),
                    border: Rgba(0x414868FF),
                    accent: Rgba(0x7AA2F7FF),
                    accentHover: Rgba(0x9ECE6AFF),
                    accentActive: Rgba(0x7DCFFFFF));
                break;
            case UIThemePreset.CatppuccinMocha:
                ApplyDarkTheme(
                    text: Rgba(0xCDD6F4FF),
                    textDisabled: Rgba(0x7F849CFF),
                    windowBg: Rgba(0x1E1E2EFF),
                    surface: Rgba(0x181825FF),
                    surfaceAlt: Rgba(0x313244FF),
                    border: Rgba(0x45475AFF),
                    accent: Rgba(0x89B4FAFF),
                    accentHover: Rgba(0xB4BEFEFF),
                    accentActive: Rgba(0x74C7ECFF));
                break;
            default:
                Apply(UIThemePreset.GitHubDark);
                break;
        }
    }

    private static void ApplyLightTheme(Vector4 text, Vector4 textDisabled, Vector4 windowBg, Vector4 surface, Vector4 surfaceAlt, Vector4 border, Vector4 accent, Vector4 accentHover, Vector4 accentActive)
    {
        ImGui.StyleColorsLight();
        var style = ImGui.GetStyle();
        ConfigureMetrics(style, isLightTheme: true);
        ApplyPalette(style.Colors, text, textDisabled, windowBg, surface, surfaceAlt, border, accent, accentHover, accentActive, isLightTheme: true);
    }

    private static void ApplyDarkTheme(Vector4 text, Vector4 textDisabled, Vector4 windowBg, Vector4 surface, Vector4 surfaceAlt, Vector4 border, Vector4 accent, Vector4 accentHover, Vector4 accentActive)
    {
        ImGui.StyleColorsDark();
        var style = ImGui.GetStyle();
        ConfigureMetrics(style, isLightTheme: false);
        ApplyPalette(style.Colors, text, textDisabled, windowBg, surface, surfaceAlt, border, accent, accentHover, accentActive, isLightTheme: false);
    }

    private static void ConfigureMetrics(ImGuiStylePtr style, bool isLightTheme)
    {
        style.WindowPadding = new Vector2(10f, 10f);
        style.FramePadding = new Vector2(8f, 6f);
        style.CellPadding = new Vector2(8f, 6f);
        style.ItemSpacing = new Vector2(8f, 6f);
        style.ItemInnerSpacing = new Vector2(6f, 6f);
        style.IndentSpacing = 18f;
        style.ScrollbarSize = 14f;
        style.GrabMinSize = 12f;
        style.WindowBorderSize = 1f;
        style.ChildBorderSize = 1f;
        style.PopupBorderSize = 1f;
        style.FrameBorderSize = 1f;
        style.TabBorderSize = 0f;
        style.WindowRounding = 8f;
        style.ChildRounding = 6f;
        style.PopupRounding = 8f;
        style.FrameRounding = 6f;
        style.ScrollbarRounding = 999f;
        style.GrabRounding = 999f;
        style.TabRounding = 6f;
        style.Alpha = 1f;
        style.DisabledAlpha = isLightTheme ? 0.65f : 0.6f;
    }

    private static void ApplyPalette(Span<Vector4> colors, Vector4 text, Vector4 textDisabled, Vector4 windowBg, Vector4 surface, Vector4 surfaceAlt, Vector4 border, Vector4 accent, Vector4 accentHover, Vector4 accentActive, bool isLightTheme)
    {
        var borderShadow = isLightTheme ? WithAlpha(border, 0.08f) : new Vector4(0f, 0f, 0f, 0f);
        var tableHeader = Blend(surfaceAlt, border, isLightTheme ? 0.18f : 0.1f);
        var rowAlt = isLightTheme ? WithAlpha(surfaceAlt, 0.35f) : WithAlpha(surfaceAlt, 0.28f);

        colors[(int)ImGuiCol.Text] = text;
        colors[(int)ImGuiCol.TextDisabled] = textDisabled;
        colors[(int)ImGuiCol.WindowBg] = windowBg;
        colors[(int)ImGuiCol.ChildBg] = surface;
        colors[(int)ImGuiCol.PopupBg] = surface;
        colors[(int)ImGuiCol.Border] = border;
        colors[(int)ImGuiCol.BorderShadow] = borderShadow;

        colors[(int)ImGuiCol.FrameBg] = surfaceAlt;
        colors[(int)ImGuiCol.FrameBgHovered] = Blend(surfaceAlt, accentHover, 0.18f);
        colors[(int)ImGuiCol.FrameBgActive] = Blend(surfaceAlt, accentActive, 0.24f);

        colors[(int)ImGuiCol.TitleBg] = surface;
        colors[(int)ImGuiCol.TitleBgActive] = surfaceAlt;
        colors[(int)ImGuiCol.TitleBgCollapsed] = surface;
        colors[(int)ImGuiCol.MenuBarBg] = surface;

        colors[(int)ImGuiCol.ScrollbarBg] = surface;
        colors[(int)ImGuiCol.ScrollbarGrab] = Blend(border, surfaceAlt, 0.45f);
        colors[(int)ImGuiCol.ScrollbarGrabHovered] = Blend(border, accentHover, 0.3f);
        colors[(int)ImGuiCol.ScrollbarGrabActive] = Blend(border, accentActive, 0.45f);

        colors[(int)ImGuiCol.CheckMark] = accent;
        colors[(int)ImGuiCol.SliderGrab] = accent;
        colors[(int)ImGuiCol.SliderGrabActive] = accentActive;

        colors[(int)ImGuiCol.Button] = surfaceAlt;
        colors[(int)ImGuiCol.ButtonHovered] = Blend(surfaceAlt, accentHover, 0.24f);
        colors[(int)ImGuiCol.ButtonActive] = Blend(surfaceAlt, accentActive, 0.32f);

        colors[(int)ImGuiCol.Header] = Blend(surfaceAlt, accent, 0.18f);
        colors[(int)ImGuiCol.HeaderHovered] = Blend(surfaceAlt, accentHover, 0.3f);
        colors[(int)ImGuiCol.HeaderActive] = Blend(surfaceAlt, accentActive, 0.42f);

        colors[(int)ImGuiCol.Separator] = border;
        colors[(int)ImGuiCol.SeparatorHovered] = accentHover;
        colors[(int)ImGuiCol.SeparatorActive] = accentActive;

        colors[(int)ImGuiCol.ResizeGrip] = WithAlpha(accent, 0.2f);
        colors[(int)ImGuiCol.ResizeGripHovered] = WithAlpha(accentHover, 0.55f);
        colors[(int)ImGuiCol.ResizeGripActive] = WithAlpha(accentActive, 0.8f);

        colors[(int)ImGuiCol.Tab] = surface;
        colors[(int)ImGuiCol.TabHovered] = Blend(surface, accentHover, 0.28f);
        colors[(int)ImGuiCol.TabSelected] = Blend(surfaceAlt, accent, 0.22f);
        colors[(int)ImGuiCol.TabDimmed] = surface;
        colors[(int)ImGuiCol.TabDimmedSelected] = Blend(surface, accent, 0.12f);

        colors[(int)ImGuiCol.DockingPreview] = WithAlpha(accent, 0.45f);
        colors[(int)ImGuiCol.DockingEmptyBg] = surface;

        colors[(int)ImGuiCol.TableHeaderBg] = tableHeader;
        colors[(int)ImGuiCol.TableBorderStrong] = border;
        colors[(int)ImGuiCol.TableBorderLight] = WithAlpha(border, isLightTheme ? 0.65f : 0.45f);
        colors[(int)ImGuiCol.TableRowBgAlt] = rowAlt;

        colors[(int)ImGuiCol.TextSelectedBg] = WithAlpha(accent, isLightTheme ? 0.2f : 0.28f);
        colors[(int)ImGuiCol.DragDropTarget] = accentHover;
        colors[(int)ImGuiCol.NavCursor] = accentHover;
    }

    private static Vector4 Blend(Vector4 left, Vector4 right, float amount)
    {
        return new Vector4(
            left.X + ((right.X - left.X) * amount),
            left.Y + ((right.Y - left.Y) * amount),
            left.Z + ((right.Z - left.Z) * amount),
            left.W + ((right.W - left.W) * amount));
    }

    private static Vector4 WithAlpha(Vector4 color, float alpha)
    {
        return new Vector4(color.X, color.Y, color.Z, alpha);
    }

    private static Vector4 Rgba(uint rgba)
    {
        var r = ((rgba >> 24) & 0xFF) / 255f;
        var g = ((rgba >> 16) & 0xFF) / 255f;
        var b = ((rgba >> 8) & 0xFF) / 255f;
        var a = (rgba & 0xFF) / 255f;
        return new Vector4(r, g, b, a);
    }
}