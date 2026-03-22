using CentrED.Lights;
using CentrED.UI;
using Hexa.NET.ImGui;
using Microsoft.Xna.Framework.Input;
using static CentrED.Application;
using static CentrED.LangEntry;
using Vector4 = System.Numerics.Vector4;

namespace CentrED.UI.Windows;

/// <summary>
/// Central options window for editor-wide preferences such as rendering behavior, fonts,
/// language, lighting, and keybindings.
/// </summary>
public class OptionsWindow : Window
{
    private Keymap _keymap;
    private UIThemeModeFilter _themeModeFilter;
    private bool _themeModeFilterInitialized;

    /// <summary>
    /// Receives the shared keymap service so keybinding changes can be displayed using the same
    /// naming and ordering rules as runtime input handling.
    /// </summary>
    public OptionsWindow(Keymap keymap)
    {
        _keymap = keymap;
    }

    /// <summary>
    /// Stable ImGui title/ID pair for the options window.
    /// </summary>
    public override string Name => LangManager.Get(OPTIONS_WINDOW) + "###Options";

    /// <summary>
    /// The options dialog auto-sizes to its contents and is intentionally non-resizable.
    /// </summary>
    public override ImGuiWindowFlags WindowFlags => ImGuiWindowFlags.AlwaysAutoResize | ImGuiWindowFlags.NoResize;

    // Local color buffers mirror the live shader/effect values exposed through the color pickers.
    private int _lightLevel = 30;
    private Vector4 _virtualLayerFillColor = new(0.2f, 0.2f, 0.2f, 0.1f);
    private Vector4 _virtualLayerBorderColor = new(1.0f, 1.0f, 1.0f, 1.0f);
    private Vector4 _terrainGridFlatColor = new(0.5f, 0.5f, 0.0f, 0.5f);
    private Vector4 _terrainGridAngledColor = new(1.0f, 1.0f, 1.0f, 1.0f);

    /// <summary>
    /// Draws the tabbed options surface and applies settings that have immediate runtime effects.
    /// </summary>
    protected override void InternalDraw()
    {
        var uiManager = CEDGame.UIManager;
        if (ImGui.BeginTabBar("Options"))
        {
            if (ImGui.BeginTabItem(LangManager.Get(GENERAL)))
            {
                EnsureThemeModeFilterInitialized();
                if (ImGui.Checkbox(LangManager.Get(OPTION_PREFER_TEXMAPS), ref Config.Instance.PreferTexMaps))
                {
                    // Changing terrain-art preference requires regenerating visible tile visuals.
                    CEDGame.MapManager.UpdateAllTiles();
                }
                ImGui.Checkbox(LangManager.Get(OPTION_OBJECT_BRIGHT_HIGHLIGHT), ref Config.Instance.ObjectBrightHighlight);
                ImGui.Checkbox(LangManager.Get(OPTION_LEGACY_MOUSE_SCROLL), ref Config.Instance.LegacyMouseScroll);
                ImGuiEx.Tooltip(LangManager.Get(OPTION_LEGACY_MOUSE_SCROLL_TOOLTIP));
                var viewportsAvailable = uiManager.HasViewports;
                ImGui.BeginDisabled(!viewportsAvailable);
                if (ImGui.Checkbox(LangManager.Get(OPTION_VIEWPORTS), ref Config.Instance.Viewports))
                {
                    // Multi-viewport support is mirrored directly into ImGui's runtime config flags.
                    if (Config.Instance.Viewports)
                    {
                        ImGui.GetIO().ConfigFlags |= ImGuiConfigFlags.ViewportsEnable;
                    }
                    else
                    {
                        ImGui.GetIO().ConfigFlags &= ~ImGuiConfigFlags.ViewportsEnable;
                    }
                }
                ImGui.EndDisabled();
                var fontSize = uiManager.FontSize;
                if (ImGuiEx.DragInt(LangManager.Get(OPTION_FONT_SIZE), ref fontSize, 1, 1, 26))
                {
                    uiManager.FontSize = fontSize;
                }
                var fontIndex = uiManager.FontIndex;
                if(ImGui.Combo(LangManager.Get(OPTION_FONT), ref fontIndex, uiManager.FontNames, uiManager.FontNames.Length))
                {
                    // Fonts are preloaded by UIManager, so changing the index can switch immediately.
                    uiManager.FontIndex = fontIndex;
                }
                var langIndex = LangManager.LangIndex;
                if (ImGui.Combo(LangManager.Get(OPTION_LANGUAGE), ref langIndex, LangManager.LangNames, LangManager.LangNames.Length))
                {
                    // The localized strings update live as soon as the active language changes.
                    LangManager.LangIndex = langIndex;
                    Config.Instance.Language = LangManager.LangNames[langIndex];
                }
                DrawThemeOptions(uiManager);
                var numberFormatKeys = new [] { OPTION_NUMBER_FORMAT_HEX, OPTION_NUMBER_FORMAT_DEC, OPTION_NUMBER_FORMAT_HEX_DEC, OPTION_NUMBER_FORMAT_DEC_HEX };
                var numberFormatLabels = numberFormatKeys.Select(LangManager.Get).ToArray();
                var numberFormatIndex = (int)Config.Instance.NumberFormat;
                if (ImGui.Combo(LangManager.Get(OPTION_NUMBER_FORMAT), ref numberFormatIndex, numberFormatLabels, numberFormatLabels.Length))
                {
                    Config.Instance.NumberFormat = (NumberDisplayFormat)numberFormatIndex;
                }
                ImGui.EndTabItem();
            }
            DrawKeymapOptions();
            DrawLightOptions();
            if (ImGui.BeginTabItem(LangManager.Get(VIRTUAL_LAYER)))
            {
                if (ImGui.ColorPicker4(LangManager.Get(FILL_COLOR), ref _virtualLayerFillColor))
                {
                    // The picker writes directly into the map effect so the overlay preview updates live.
                    CEDGame.MapManager.MapEffect.VirtualLayerFillColor = new Microsoft.Xna.Framework.Vector4
                    (
                        _virtualLayerFillColor.X,
                        _virtualLayerFillColor.Y,
                        _virtualLayerFillColor.Z,
                        _virtualLayerFillColor.W
                    );
                }
                if (ImGui.ColorPicker4(LangManager.Get(BORDER_COLOR), ref _virtualLayerBorderColor))
                {
                    CEDGame.MapManager.MapEffect.VirtualLayerBorderColor = new Microsoft.Xna.Framework.Vector4
                    (
                        _virtualLayerBorderColor.X,
                        _virtualLayerBorderColor.Y,
                        _virtualLayerBorderColor.Z,
                        _virtualLayerBorderColor.W
                    );
                }
                ImGui.EndTabItem();
            }
            if (ImGui.BeginTabItem(LangManager.Get(TERRAIN_GRID)))
            {
                if (ImGui.ColorPicker4(LangManager.Get(FLAT_COLOR), ref _terrainGridFlatColor))
                {
                    // Terrain-grid colors are shader parameters, so they also update immediately.
                    CEDGame.MapManager.MapEffect.TerrainGridFlatColor = new Microsoft.Xna.Framework.Vector4
                    (
                        _terrainGridFlatColor.X,
                        _terrainGridFlatColor.Y,
                        _terrainGridFlatColor.Z,
                        _terrainGridFlatColor.W
                    );
                }
                if (ImGui.ColorPicker4(LangManager.Get(ANGLED_COLOR), ref _terrainGridAngledColor))
                {
                    CEDGame.MapManager.MapEffect.TerrainGridAngledColor = new Microsoft.Xna.Framework.Vector4
                    (
                        _terrainGridAngledColor.X,
                        _terrainGridAngledColor.Y,
                        _terrainGridAngledColor.Z,
                        _terrainGridAngledColor.W
                    );
                }
                ImGui.EndTabItem();
            }
            ImGui.EndTabBar();
        }
    }

    private string assigningActionName = "";
    private byte assignedKeyNumber = 0;

    /// <summary>
    /// Draws the lighting tab and applies any changes that require relighting or tile refreshes.
    /// </summary>
    private void DrawLightOptions()
    {
        if (ImGui.BeginTabItem(LangManager.Get(LIGHTS)))
        {
            if (LightsManager.Instance == null)
            {
                ImGui.Text(LangManager.Get(NOT_CONNECTED));
            }
            else
            {
                if (ImGui.SliderInt(LangManager.Get(LIGHT_LEVEL), ref LightsManager.Instance.GlobalLightLevel, 0, 30))
                {
                    // Global ambient intensity is recomputed immediately when the slider moves.
                    LightsManager.Instance.UpdateGlobalLight();
                }
                if (ImGui.Checkbox(LangManager.Get(COLORED_LIGHTS), ref LightsManager.Instance.ColoredLights))
                {
                    CEDGame.MapManager.UpdateLights();
                }
                ImGui.Checkbox(LangManager.Get(ALTERNATIVE_LIGHTS), ref LightsManager.Instance.AltLights);
                {
                    // No explicit refresh currently happens here; this mirrors the existing behavior.
                }
                if (ImGui.Checkbox(LangManager.Get(DARK_NIGHTS), ref LightsManager.Instance.DarkNights))
                {
                    LightsManager.Instance.UpdateGlobalLight();
                }
                if(ImGui.Checkbox(LangManager.Get(SHOW_INVISIBLE_LIGHTS), ref LightsManager.Instance.ShowInvisibleLights))
                {
                    CEDGame.MapManager.UpdateLights();
                }
                if (ImGui.Checkbox(LangManager.Get(CUO_TERRAIN_LIGHTING), ref LightsManager.Instance.ClassicUONormals))
                {
                    CEDGame.MapManager.UpdateAllTiles();
                }
                ImGuiEx.Tooltip(LangManager.Get(CUO_TERRAIN_LIGHTING_TOOLTIP));
            }
            ImGui.EndTabItem();
        }
    }

    /// <summary>
    /// Draws the keymap tab for the subset of actions currently exposed in the options UI.
    /// </summary>
    private void DrawKeymapOptions()
    {
        if (ImGui.BeginTabItem(LangManager.Get(KEYMAP)))
        {
            DrawSingleKey(Keymap.MoveUp);
            DrawSingleKey(Keymap.MoveDown);
            DrawSingleKey(Keymap.MoveLeft);
            DrawSingleKey(Keymap.MoveRight);
            ImGui.Separator();
            DrawSingleKey(Keymap.ToggleAnimatedStatics);
            DrawSingleKey(Keymap.Minimap);
            ImGui.EndTabItem();
        }
    }


    private bool _showNewKeyPopup;

    private void EnsureThemeModeFilterInitialized()
    {
        if (_themeModeFilterInitialized)
        {
            return;
        }

        _themeModeFilter = ThemeManager.GetMode(Config.Instance.ThemePreset);
        _themeModeFilterInitialized = true;
    }

    private void DrawThemeOptions(UIManager uiManager)
    {
        var themeModeLabels = new[]
        {
            LangManager.Get(OPTION_THEME_MODE_ALL),
            LangManager.Get(OPTION_THEME_MODE_LIGHT),
            LangManager.Get(OPTION_THEME_MODE_DARK),
        };

        var themeModeIndex = (int)_themeModeFilter;
        if (ImGui.Combo(LangManager.Get(OPTION_THEME_MODE), ref themeModeIndex, themeModeLabels, themeModeLabels.Length))
        {
            _themeModeFilter = (UIThemeModeFilter)themeModeIndex;
            var allowedPresets = ThemeManager.GetPresets(_themeModeFilter);
            if (!allowedPresets.Contains(Config.Instance.ThemePreset))
            {
                uiManager.ApplyTheme(allowedPresets[0]);
            }
        }

        var filteredPresets = ThemeManager.GetPresets(_themeModeFilter);
        var filteredPresetNames = ThemeManager.GetPresetNames(_themeModeFilter);
        var themePresetIndex = Array.IndexOf(filteredPresets, Config.Instance.ThemePreset);
        if (themePresetIndex == -1)
        {
            themePresetIndex = 0;
        }

        if (ImGui.Combo(LangManager.Get(OPTION_THEME_PRESET), ref themePresetIndex, filteredPresetNames, filteredPresetNames.Length))
        {
            uiManager.ApplyTheme(filteredPresets[themePresetIndex]);
        }
    }
    
    /// <summary>
    /// Draws the two binding slots for one action and handles the modal key-capture flow.
    /// </summary>
    private void DrawSingleKey(string action)
    {
        var keys = _keymap.GetKeys(action);
        ImGui.Text(_keymap.PrettyName(action));
        ImGui.SameLine();

        // While one action is waiting for input, all other key-binding buttons are disabled to
        // keep the capture flow unambiguous.
        ImGui.BeginDisabled(assigningActionName != "");
        var label1 = (assigningActionName == action && assignedKeyNumber == 1) ?
            LangManager.Get(ASSIGN_NEW_KEY) :
            string.Join(" + ", keys.Item1.Select(x => x.ToString()));
        if (ImGui.Button($"{label1}##{action}1"))
        {
            assigningActionName = action;
            assignedKeyNumber = 1;
            ImGui.OpenPopup("NewKey");
            _showNewKeyPopup = true;
        }
        ImGui.SameLine();
        var label2 = (assigningActionName == action && assignedKeyNumber == 2) ?
            LangManager.Get(ASSIGN_NEW_KEY) :
            string.Join(" + ", keys.Item2.Select(x => x.ToString()));
        if (ImGui.Button($"{label2}##{action}2"))
        {
            assigningActionName = action;
            assignedKeyNumber = 2;
            ImGui.OpenPopup("NewKey");
            _showNewKeyPopup = true;
        }
        ImGui.EndDisabled();
        if (assigningActionName == action && ImGui.BeginPopupModal
                ("NewKey", ref _showNewKeyPopup, ImGuiWindowFlags.AlwaysAutoResize | ImGuiWindowFlags.NoTitleBar))
        {
            // The keymap service already knows which keys are currently down, so the popup can
            // display and commit the combination without bespoke event handling here.
            var pressedKeys = _keymap.GetKeysDown();
            ImGui.Text(string.Format(LangManager.Get(ENTER_NEW_KEY_FOR_1NAME), assigningActionName));
            ImGui.Text(string.Join("+", pressedKeys));
            ImGui.Text(LangManager.Get(PRESS_ESC_TO_CANCEL));
            
            foreach (var pressedKey in pressedKeys)
            {
                if (pressedKey == Keys.Escape)
                {
                    // Escape cancels the capture without changing the existing binding.
                    assigningActionName = "";
                    assignedKeyNumber = 0;
                    break;
                }
                if (pressedKey is >= Keys.A and <= Keys.Z)
                {
                    // Letter keys are used as the commit trigger; modifier keys are preserved by
                    // sorting the full pressed-key set into the configured storage format.
                    var sortedKeys = pressedKeys.Order(new Keymap.LetterLastComparer()).ToArray();
                    var oldKeys = Config.Instance.Keymap[action];
                    var newKeys = assignedKeyNumber == 1 ? (sortedKeys, oldKeys.Item2) : (oldKeys.Item1, sortedKeys);
                    Config.Instance.Keymap[action] = newKeys;
                    assigningActionName = "";
                    assignedKeyNumber = 0;
                }
            }
            if (assigningActionName == "")
            {
                ImGui.CloseCurrentPopup();
            }
            ImGui.EndPopup();
        }
    }
}