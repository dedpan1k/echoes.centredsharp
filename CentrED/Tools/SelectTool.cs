using CentrED.Map;
using CentrED.UI;
using CentrED.UI.Windows;
using Hexa.NET.ImGui;
using Microsoft.Xna.Framework.Input;
using static CentrED.LangEntry;

namespace CentrED.Tools;

/// <summary>
/// Selects map objects and optionally samples their tile ids or hues into other windows.
/// </summary>
public class SelectTool : Tool
{
    /// <inheritdoc />
    public override string Name => LangManager.Get(SELECT_TOOL);

    /// <inheritdoc />
    public override Keys Shortcut => Keys.F1;

    private bool _pressed;
    private bool _pickTile;
    private bool _pickHue;
    
    /// <summary>
    /// Draws help text for the select tool.
    /// </summary>
    internal override void Draw()
    {
        ImGui.TextDisabled("(?)"u8);
        ImGuiEx.Tooltip(LangManager.Get(SELECT_TOOL_TOOLTIP));
    }
    
    /// <summary>
    /// Starts a selection drag gesture.
    /// </summary>
    /// <param name="o">The tile under the cursor.</param>
    public override void OnMousePressed(TileObject? o)
    {
        _pressed = true;
        OnMouseEnter(o);
    }

    /// <summary>
    /// Ends the current selection drag gesture.
    /// </summary>
    /// <param name="o">The tile under the cursor.</param>
    public override void OnMouseReleased(TileObject? o)
    {
        _pressed = false;
    }

    /// <summary>
    /// Enables temporary tile or hue picking while modifier keys are held.
    /// </summary>
    /// <param name="key">The pressed key.</param>
    public sealed override void OnKeyPressed(Keys key)
    {
        if (key == Keys.LeftAlt && !_pressed)
        {
            _pickTile = true;
        }
        if (key == Keys.LeftShift && !_pressed)
        {
            _pickHue = true;
        }
    }
    
    /// <summary>
    /// Disables temporary tile or hue picking when modifier keys are released.
    /// </summary>
    /// <param name="key">The released key.</param>
    public sealed override void OnKeyReleased(Keys key)
    {
        if (key == Keys.LeftAlt && !_pressed)
        {
            _pickTile = false;
        }
        if (key == Keys.LeftShift && !_pressed)
        {
            _pickHue = false;
        }
    }

    /// <summary>
    /// Updates the info window selection and optional tile or hue pickers.
    /// </summary>
    /// <param name="o">The tile under the cursor.</param>
    public override void OnMouseEnter(TileObject? o)
    {
        if (_pressed)
        {
            UIManager.GetWindow<InfoWindow>().Selected = o;
            if (_pickTile && o != null)
            {
                UIManager.GetWindow<TilesWindow>().UpdateSelection(o);
            }
            if (_pickHue && o is StaticObject so)
            {
                UIManager.GetWindow<HuesWindow>().UpdateSelection(so);
            }
        }
    }

    /// <summary>
    /// Ensures the info window is visible when the tool activates.
    /// </summary>
    /// <param name="o">The hovered tile, if any.</param>
    public override void OnActivated(TileObject? o)
    {
        UIManager.GetWindow<InfoWindow>().Show = true;
    }
}