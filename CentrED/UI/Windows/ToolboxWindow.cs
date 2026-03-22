using CentrED.IO.Models;
using CentrED.Tools;
using Hexa.NET.ImGui;
using Microsoft.Xna.Framework.Input;
using static CentrED.Application;
using static CentrED.LangEntry;

namespace CentrED.UI.Windows;

/// <summary>
/// Displays the available editing tools, lets the user switch the active one, and hosts the
/// parameter UI for the currently selected tool.
/// </summary>
public class ToolboxWindow : Window
{
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
    /// Draws the tool selector list followed by the active tool's parameter surface.
    /// </summary>
    protected override void InternalDraw()
    {
        // Every registered map tool gets a selector row in the toolbox.
        CEDGame.MapManager.Tools.ForEach(ToolButton);
        ImGui.Separator();
        ImGui.Text(LangManager.Get(PARAMETERS));
        if (ImGui.BeginChild("ToolOptionsContainer", new System.Numerics.Vector2(-1, -1), ImGuiChildFlags.Borders))
        {
            // The active tool owns both its inline controls and any detached floating helper UI.
            var tool = CEDGame.MapManager.ActiveTool;
            tool.Draw();
            tool.DrawFloatingWindow();
        }
        ImGui.EndChild();
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