using CentrED.IO.Models;
using CentrED.Tools;
using CentrED.UI;
using Hexa.NET.ImGui;
using Microsoft.Xna.Framework.Input;
using Vector2 = System.Numerics.Vector2;
using static CentrED.Application;
using static CentrED.LangEntry;

namespace CentrED.UI.Windows;

/// <summary>
/// Displays the available editing tools, lets the user switch the active one, and hosts the
/// parameter UI for the currently selected tool.
/// </summary>
public class ToolboxWindow : Window
{
    private static readonly Vector2 SlimNavbarPadding = new(3f, 3f);
    private static readonly Vector2 SlimNavbarSpacing = new(10f, 0f);
    private static readonly Vector2 FlyoutPadding = new(6f, 4f);
    private static readonly Vector2 ToolbarSectionSpacing = new(4f, 0f);
    private const float MinHorizontalParameterWidth = 160f;
    private const float MaxHorizontalParameterWidth = 320f;
    private const float HorizontalParameterWidthRatio = 0.22f;
    private const float ToolbarBarHeight = 23f;
    private const float MaxDrawerHeight = 420f;
    private const float DrawerOffsetY = 3f;

    private Vector2 _activeToolTabMin;
    private Vector2 _activeToolTabMax;
    private Tool? _lastToolbarTool;
    private bool _toolbarStateInitialized;
    private bool _scrollActiveToolIntoView;

    /// <summary>
    /// Stable ImGui title/ID pair for the toolbox window.
    /// </summary>
    public override string Name => LangManager.Get(TOOLBOX_WINDOW) + "###Toolbox";

    /// <summary>
    /// The toolbox is part of the default layout and starts visible.
    /// </summary>
    public override WindowState DefaultState => new()
    {
        IsOpen = true
    };

    /// <summary>
    /// Draws the toolbox entry in the Tools menu together with layout options.
    /// </summary>
    public override void DrawMenuItem()
    {
        if (!Enabled)
            ImGui.BeginDisabled();

        if (ImGui.BeginMenu(LangManager.Get(TOOLBOX_WINDOW)))
        {
            var wasShown = _show;
            if (ImGui.MenuItem(LangManager.Get(SHOW), Shortcut, ref _show) && !wasShown && _show)
            {
                OnShow();
            }

            var horizontalToolbox = Config.Instance.HorizontalToolbox;
            if (ImGui.MenuItem(LangManager.Get(TOOLBOX_HORIZONTAL_MODE), string.Empty, horizontalToolbox))
            {
                Config.Instance.HorizontalToolbox = !horizontalToolbox;
                Config.Save();
            }

            ImGui.EndMenu();
        }

        if (!Enabled)
            ImGui.EndDisabled();
    }

    /// <summary>
    /// Draws the tool selector list followed by the active tool's parameter surface.
    /// </summary>
    protected override void InternalDraw()
    {
        DrawVerticalLayout();
    }

    /// <summary>
    /// Draws the fixed application toolbar beneath the main menu when enabled.
    /// </summary>
    public void DrawApplicationToolbar()
    {
        if (!Config.Instance.HorizontalToolbox)
        {
            _toolbarStateInitialized = false;
            return;
        }

        if (!_toolbarStateInitialized)
        {
            Config.Instance.HorizontalToolboxParametersOpen = false;
            _toolbarStateInitialized = true;
        }

        if (_lastToolbarTool != CEDGame.MapManager.ActiveTool)
        {
            _lastToolbarTool = CEDGame.MapManager.ActiveTool;
            _scrollActiveToolIntoView = true;
        }

        _activeToolTabMin = Vector2.Zero;
        _activeToolTabMax = Vector2.Zero;

        if (ImGuiEx.BeginToolbarBar(ToolbarBarHeight))
        {
            DrawToolNavbar();
            Application.CEDGame.UIManager.AddCurrentWindowRect();
            ImGuiEx.EndToolbarBar();
        }

        if (Config.Instance.HorizontalToolboxParametersOpen && _activeToolTabMax != Vector2.Zero)
        {
            DrawToolbarFlyout();
        }
    }

    /// <summary>
    /// Draws the original vertical selector list followed by the active tool parameters.
    /// </summary>
    private void DrawVerticalLayout()
    {
        // Every registered map tool gets a selector row in the toolbox.
        CEDGame.MapManager.Tools.ForEach(ToolButton);
        ImGui.Separator();
        ImGui.Text(LangManager.Get(PARAMETERS));
        if (ImGui.BeginChild("ToolOptionsContainer", new Vector2(-1, -1), ImGuiChildFlags.Borders))
        {
            DrawActiveToolContents();
        }
        ImGui.EndChild();
    }

    /// <summary>
    /// Draws the tool navigation row and captures the active tool tab bounds for the flyout.
    /// </summary>
    private void DrawToolNavbar()
    {
        var tools = CEDGame.MapManager.Tools;
        var activeTool = CEDGame.MapManager.ActiveTool;
        var minimapWindow = CEDGame.UIManager.GetWindow<MinimapWindow>();

        ImGui.SetCursorPosY(3f);
        ImGui.PushStyleVar(ImGuiStyleVar.FramePadding, SlimNavbarPadding);
        ImGui.PushStyleVar(ImGuiStyleVar.ItemSpacing, SlimNavbarSpacing);

        for (var index = 0; index < tools.Count; index++)
        {
            var tool = tools[index];
            var isSelected = activeTool == tool;
            var indicator = isSelected && Config.Instance.HorizontalToolboxParametersOpen ? "v" : ">";
            var buttonLabel = GetToolbarButtonLabel(tool, indicator);
            var buttonSize = GetTabButtonSize(buttonLabel, SlimNavbarPadding, 0.62f);
            if (index > 0)
            {
                ImGui.SameLine();
            }

            if (DrawTabButton($"Tool{index}", buttonLabel, isSelected, buttonSize))
            {
                var itemMin = ImGui.GetItemRectMin();
                var itemMax = ImGui.GetItemRectMax();
                var indicatorWidth = ImGui.CalcTextSize(indicator).X + SlimNavbarPadding.X * 2f;
                var chevronClicked = ImGui.GetMousePos().X >= itemMax.X - indicatorWidth;
                CEDGame.MapManager.Tools.ForEach(t => t.ClosePopup());
                if (chevronClicked)
                {
                    if (!isSelected)
                    {
                        CEDGame.MapManager.ActiveTool = tool;
                        Config.Instance.HorizontalToolboxParametersOpen = true;
                    }
                    else
                    {
                        Config.Instance.HorizontalToolboxParametersOpen = !Config.Instance.HorizontalToolboxParametersOpen;
                    }
                }
                else if (!isSelected)
                {
                    CEDGame.MapManager.ActiveTool = tool;
                }
            }

            if (CEDGame.MapManager.ActiveTool == tool)
            {
                _activeToolTabMin = ImGui.GetItemRectMin();
                _activeToolTabMax = ImGui.GetItemRectMax();
                if (_scrollActiveToolIntoView)
                {
                    ImGui.SetScrollHereX(0.5f);
                    _scrollActiveToolIntoView = false;
                }
            }
        }

        if (tools.Count > 0)
        {
            ImGui.SameLine(0f, ToolbarSectionSpacing.X);
        }

        var separatorMin = ImGui.GetCursorScreenPos();
        var separatorHeight = ImGui.GetFrameHeight();
        ImGui.Dummy(new Vector2(1f, separatorHeight));
        var separatorMax = separatorMin + new Vector2(1f, separatorHeight);
        ImGui.GetWindowDrawList().AddLine(separatorMin + new Vector2(0f, 2f), separatorMax - new Vector2(0f, 2f), ImGui.GetColorU32(ImGuiCol.Border));
        ImGui.SameLine(0f, ToolbarSectionSpacing.X);

        var minimapLabel = $"{LangManager.Get(MINIMAP_WINDOW)} (M)";
        var minimapButtonSize = GetTabButtonSize(minimapLabel, SlimNavbarPadding, 0.62f);
        if (DrawTabButton("MiniMapToggle", minimapLabel, minimapWindow.Show, minimapButtonSize))
        {
            minimapWindow.Show = !minimapWindow.Show;
        }

        ImGui.PopStyleVar(2);
    }

    /// <summary>
    /// Draws the active tool parameter flyout below the fixed toolbar.
    /// </summary>
    private void DrawToolbarFlyout()
    {
        var viewport = ImGui.GetMainViewport();
        var availableWidth = MathF.Max(MinHorizontalParameterWidth, viewport.WorkPos.X + viewport.WorkSize.X - _activeToolTabMin.X - 8f);
        var preferredWidth = MathF.Max(MinHorizontalParameterWidth,
            MathF.Max(_activeToolTabMax.X - _activeToolTabMin.X + 96f, viewport.WorkSize.X * HorizontalParameterWidthRatio));
        var parameterWidth = MathF.Min(MathF.Min(preferredWidth, MaxHorizontalParameterWidth), availableWidth);
        var posX = MathF.Min(_activeToolTabMin.X, viewport.WorkPos.X + viewport.WorkSize.X - parameterWidth - 8f);
        posX = MathF.Max(viewport.WorkPos.X + 8f, posX);
        var posY = _activeToolTabMax.Y + DrawerOffsetY;
        var maxDrawerHeight = MathF.Min(viewport.WorkSize.Y * 0.72f, MaxDrawerHeight);

        ImGui.SetNextWindowPos(new Vector2(posX, posY), ImGuiCond.Always);
        ImGui.SetNextWindowSizeConstraints(new Vector2(parameterWidth, 0f), new Vector2(parameterWidth, maxDrawerHeight));
        ImGui.PushStyleVar(ImGuiStyleVar.WindowPadding, FlyoutPadding);
        ImGui.PushStyleVar(ImGuiStyleVar.WindowRounding, 6f);
        ImGui.PushStyleVar(ImGuiStyleVar.WindowBorderSize, 1f);
        ImGui.PushStyleColor(ImGuiCol.WindowBg, ImGui.GetStyle().Colors[(int)ImGuiCol.PopupBg]);
        ImGui.PushStyleColor(ImGuiCol.Border, ImGui.GetStyle().Colors[(int)ImGuiCol.HeaderActive]);

        if (ImGui.Begin("##ToolbarParametersFlyout",
                ImGuiWindowFlags.NoDocking | ImGuiWindowFlags.NoDecoration | ImGuiWindowFlags.NoSavedSettings |
                ImGuiWindowFlags.AlwaysAutoResize))
        {
            ImGui.TextDisabled(CEDGame.MapManager.ActiveTool.Name);
            var closeLabel = Config.Instance.HorizontalToolboxParametersOpen ? LangManager.Get(HIDE) : LangManager.Get(SHOW);
            var closeButtonWidth = ImGui.CalcTextSize(closeLabel).X + ImGui.GetStyle().FramePadding.X * 2f;
            ImGui.SameLine();
            ImGui.SetCursorPosX(ImGui.GetWindowWidth() - closeButtonWidth - FlyoutPadding.X);
            if (ImGui.SmallButton(closeLabel))
            {
                Config.Instance.HorizontalToolboxParametersOpen = false;
            }
            ImGui.Dummy(new Vector2(0f, 2f));
            ImGui.Separator();
            DrawActiveToolContents();
            Application.CEDGame.UIManager.AddCurrentWindowRect();
        }
        ImGui.End();
        ImGui.PopStyleColor(2);
        ImGui.PopStyleVar(3);
    }

    /// <summary>
    /// Draws the active tool's embedded and floating UI surfaces.
    /// </summary>
    private static void DrawActiveToolContents()
    {
        // The active tool owns both its inline controls and any detached floating helper UI.
        var tool = CEDGame.MapManager.ActiveTool;
        tool.Draw();
        tool.DrawFloatingWindow();
    }

    /// <summary>
    /// Calculates a button size for navbar tabs.
    /// </summary>
    /// <param name="label">The visible tab label.</param>
    /// <param name="framePadding">Current ImGui frame padding.</param>
    /// <param name="widthMultiplier">Horizontal padding multiplier for the tab.</param>
    private static Vector2 GetTabButtonSize(string label, Vector2 framePadding, float widthMultiplier)
    {
        var textSize = ImGui.CalcTextSize(label);
        return new Vector2(textSize.X + framePadding.X * 2f * widthMultiplier, textSize.Y + framePadding.Y * 2f);
    }

    /// <summary>
    /// Builds the compact toolbar label with its shortcut and flyout-state indicator.
    /// </summary>
    /// <param name="tool">The tool being rendered.</param>
    /// <param name="indicator">The trailing drawer indicator.</param>
    private static string GetToolbarButtonLabel(Tool tool, string indicator)
    {
        return tool.Shortcut == Keys.None
            ? $"{tool.Name} {indicator}"
            : $"{tool.Name} ({tool.Shortcut}) {indicator}";
    }

    /// <summary>
    /// Draws a navbar-style tab button with active-state styling.
    /// </summary>
    /// <param name="id">Stable hidden ImGui ID suffix.</param>
    /// <param name="label">Visible tab label.</param>
    /// <param name="isSelected">Whether the tab is currently active.</param>
    /// <param name="size">Desired tab size.</param>
    private static bool DrawTabButton(string id, string label, bool isSelected, Vector2 size)
    {
        var style = ImGui.GetStyle();
        ImGui.PushStyleColor(ImGuiCol.Button, isSelected ? style.Colors[(int)ImGuiCol.Header] : style.Colors[(int)ImGuiCol.Button]);
        ImGui.PushStyleColor(ImGuiCol.ButtonHovered, isSelected ? style.Colors[(int)ImGuiCol.HeaderHovered] : style.Colors[(int)ImGuiCol.ButtonHovered]);
        ImGui.PushStyleColor(ImGuiCol.ButtonActive, isSelected ? style.Colors[(int)ImGuiCol.HeaderActive] : style.Colors[(int)ImGuiCol.ButtonActive]);
        var clicked = ImGui.Button($"{label}##{id}", size);
        ImGui.PopStyleColor(3);
        return clicked;
    }

    /// <summary>
    /// Draws one tool-selection row and its optional keyboard shortcut label.
    /// </summary>
    private void ToolButton(Tool tool)
    {
        if (ImGui.RadioButton(tool.Name, CEDGame.MapManager.ActiveTool == tool))
        {
            // Selecting a radio button swaps the map manager's active tool immediately.
            CEDGame.MapManager.ActiveTool = tool;
        }
        if (tool.Shortcut != Keys.None)
        {
            // Shortcut hints are shown as passive text because actual handling happens in the keymap/input layer.
            ImGui.SameLine();
            ImGui.TextDisabled(tool.Shortcut.ToString());
        }
    }
}