using CentrED.Client;
using CentrED.Map;
using CentrED.UI;
using Hexa.NET.ImGui;
using Microsoft.Xna.Framework.Input;

namespace CentrED.Tools;

/// <summary>
/// Defines the base interaction surface for editor tools.
/// </summary>
public abstract class Tool
{
    /// <summary>
    /// Gets the active map manager.
    /// </summary>
    protected MapManager MapManager => Application.CEDGame.MapManager;

    /// <summary>
    /// Gets the active UI manager.
    /// </summary>
    protected UIManager UIManager => Application.CEDGame.UIManager;

    /// <summary>
    /// Gets the active network client.
    /// </summary>
    protected CentrEDClient Client => Application.CEDClient;

    /// <summary>
    /// Gets the localized display name of the tool.
    /// </summary>
    public abstract string Name { get; }

    /// <summary>
    /// Gets the keyboard shortcut that activates the tool.
    /// </summary>
    public virtual Keys Shortcut => Keys.None;
    private bool openPopup;

    /// <summary>
    /// Performs any post-construction initialization that depends on the map manager.
    /// </summary>
    /// <param name="mapManager">The active map manager.</param>
    public virtual void PostConstruct(MapManager mapManager)
    {
    }

    /// <summary>
    /// Draws the tool-specific popup UI.
    /// </summary>
    internal virtual void Draw()
    {
    }
    
    /// <summary>
    /// Called when the tool becomes active.
    /// </summary>
    /// <param name="o">The currently hovered tile, if any.</param>
    public virtual void OnActivated(TileObject? o)
    {
    }

    /// <summary>
    /// Called when the tool is deactivated.
    /// </summary>
    /// <param name="o">The currently hovered tile, if any.</param>
    public virtual void OnDeactivated(TileObject? o)
    {
    }
    
    /// <summary>
    /// Called when the mouse button is pressed while this tool is active.
    /// </summary>
    /// <param name="o">The hovered tile, if any.</param>
    public virtual void OnMousePressed(TileObject? o)
    {
    }

    /// <summary>
    /// Called when the mouse button is released while this tool is active.
    /// </summary>
    /// <param name="o">The hovered tile, if any.</param>
    public virtual void OnMouseReleased(TileObject? o)
    {
    }

    /// <summary>
    /// Called when a keyboard key is pressed while this tool is active.
    /// </summary>
    /// <param name="key">The pressed key.</param>
    public virtual void OnKeyPressed(Keys key)
    {
    }

    /// <summary>
    /// Called when a keyboard key is released while this tool is active.
    /// </summary>
    /// <param name="key">The released key.</param>
    public virtual void OnKeyReleased(Keys key)
    {
    }
    
    /// <summary>
    /// Called when the cursor enters a tile while this tool is active.
    /// </summary>
    /// <param name="o">The hovered tile, if any.</param>
    public virtual void OnMouseEnter(TileObject? o)
    {
    }

    /// <summary>
    /// Called when the cursor leaves a tile while this tool is active.
    /// </summary>
    /// <param name="o">The tile being left, if any.</param>
    public virtual void OnMouseLeave(TileObject? o)
    {
    }

    /// <summary>
    /// Applies the tool in one shot to the supplied tile.
    /// </summary>
    /// <param name="o">The target tile, if any.</param>
    public virtual void Apply(TileObject? o)
    {
        OnMouseEnter(o);
        OnMousePressed(o);
        OnMouseReleased(o);
    }
    
    /// <summary>
    /// Opens the floating popup window for the tool near the cursor.
    /// </summary>
    public void OpenPopup()
    {
        openPopup = true;
        ImGui.SetWindowPos("ToolPopup", ImGui.GetMousePos());
    }

    /// <summary>
    /// Closes the floating popup window for the tool.
    /// </summary>
    public void ClosePopup()
    {
        openPopup = false;
    }
    
    /// <summary>
    /// Draws the tool popup window when it is open.
    /// </summary>
    public void DrawFloatingWindow()
    {
        if (openPopup)
        {
            if (ImGui.Begin("ToolPopup", ImGuiWindowFlags.NoDecoration | ImGuiWindowFlags.NoDocking | ImGuiWindowFlags.AlwaysAutoResize))
            {
                openPopup = ImGui.IsWindowFocused(ImGuiFocusedFlags.ChildWindows);
                Draw();
            }
            ImGui.End();
        }
    }

    /// <summary>
    /// Captures a Z value from another editor action for tools that support it.
    /// </summary>
    /// <param name="z">The captured altitude.</param>
    public virtual void GrabZ(sbyte z)
    {
    }
}