using CentrED.Map;
using CentrED.UI.Windows;
using CentrED.Utils;
using Hexa.NET.ImGui;
using Microsoft.Xna.Framework.Input;
using static CentrED.LangEntry;

namespace CentrED.Tools;

/// <summary>
/// Applies hues to static tiles using either individual hues or hue sets.
/// </summary>
public class HueTool : BaseTool
{
    private readonly HuesWindow _huesWindow;
    
    /// <summary>
    /// Initializes the hue tool and caches the hues window.
    /// </summary>
    public HueTool()
    {
        _huesWindow = UIManager.GetWindow<HuesWindow>();
    }
    
    /// <inheritdoc />
    public override string Name => LangManager.Get(HUE_TOOL);

    /// <inheritdoc />
    public override Keys Shortcut => Keys.F6;

    private enum HueSource
    {
        HUE,
        HUE_SET
    }
    
    private int _hueSource;

    /// <summary>
    /// Draws the hue-tool configuration UI.
    /// </summary>
    internal override void Draw()
    {
        ImGui.Text(LangManager.Get(SOURCE));
        ImGui.RadioButton(LangManager.Get(HUES), ref _hueSource, (int)HueSource.HUE);
        ImGui.RadioButton(LangManager.Get(HUE_SET), ref _hueSource, (int)HueSource.HUE_SET);
        if (_huesWindow.ActiveHueSetValues.Count <= 0)
        {
            ImGui.SameLine();
            ImGui.TextDisabled(LangManager.Get(EMPTY));
        }
        ImGui.Separator();
        base.Draw();
    }

    /// <summary>
    /// Ensures the hues window is visible when the tool activates.
    /// </summary>
    /// <param name="o">The hovered tile, if any.</param>
    public override void OnActivated(TileObject? o)
    {
        UIManager.GetWindow<HuesWindow>().Show = true;
    }

    /// <summary>
    /// Gets the hue that should be applied for the current tool settings.
    /// </summary>
    public ushort ActiveHue => (HueSource)_hueSource switch
    {
        HueSource.HUE => _huesWindow.SelectedIds.GetRandom() ?? 0,
        HueSource.HUE_SET => _huesWindow.ActiveHueSetValues.GetRandom() ?? 0,
        _ => 0
    };

    /// <summary>
    /// Applies a ghost hue preview to the target static.
    /// </summary>
    /// <param name="o">The target tile.</param>
    protected override void GhostApply(TileObject? o)
    {
        if (o is StaticObject so)
        {
            so.GhostHue = ActiveHue;
        }
    }

    /// <summary>
    /// Clears the hue preview.
    /// </summary>
    /// <param name="o">The target tile.</param>
    protected override void GhostClear(TileObject? o)
    {
        if (o is StaticObject)
        {
            o.Reset();
        }
    }

    /// <summary>
    /// Commits the selected hue to the target static.
    /// </summary>
    /// <param name="o">The target tile.</param>
    protected override void InternalApply(TileObject? o)
    {
        if (o is StaticObject so && so.GhostHue != -1)
            so.StaticTile.Hue = (ushort)so.GhostHue;
    }
}