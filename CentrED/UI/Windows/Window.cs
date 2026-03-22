using CentrED.IO.Models;
using Hexa.NET.ImGui;

namespace CentrED.UI.Windows;

/// <summary>
/// Base class for all ImGui-backed editor windows, providing persisted open-state handling,
/// menu integration, and the common draw wrapper around each concrete window body.
/// </summary>
public abstract class Window
{
    /// <summary>
    /// Restores the persisted open/closed state for the concrete window when available.
    /// </summary>
    public Window()
    {
        if (Config.Instance.Layout.TryGetValue(WindowId, out var state))
        {
            Show = state.IsOpen;
        }
    }
    
    /// <summary>
    /// Visible ImGui window title. Implementations typically append a hidden ID suffix using
    /// the ### convention so the label can change without breaking persisted window identity.
    /// </summary>
    public abstract string Name { get; }

    /// <summary>
    /// Stable identifier derived from the hidden ImGui ID suffix portion of <see cref="Name"/>.
    /// This key is used to persist layout state across sessions.
    /// </summary>
    private string WindowId => Name.Split("###").Last();

    /// <summary>
    /// Optional shortcut hint shown next to the window in menus.
    /// </summary>
    public virtual string Shortcut => "";

    /// <summary>
    /// Default persisted window state used when this window has not yet been seen in the layout config.
    /// </summary>
    public virtual WindowState DefaultState => new();

    /// <summary>
    /// Additional ImGui window flags applied when the window is drawn.
    /// </summary>
    public virtual ImGuiWindowFlags WindowFlags => ImGuiWindowFlags.None;

    /// <summary>
    /// Controls whether the window should be available in menus and for drawing.
    /// </summary>
    public virtual bool Enabled => true;

    protected bool _show;

    /// <summary>
    /// Current open/closed state for the window.
    /// Transitioning to visible triggers <see cref="OnShow"/> so derived windows can refresh data.
    /// </summary>
    public bool Show
    {
        get => _show;
        set  {
            _show = value;
            if(_show)
                OnShow();
        }
    }

    /// <summary>
    /// Called whenever the window transitions into the visible state.
    /// </summary>
    public virtual void OnShow()
    {
        
    }

    /// <summary>
    /// Draws the window's entry in a menu, including enabled-state handling and open-state toggling.
    /// </summary>
    public virtual void DrawMenuItem()
    {
        if(!Enabled)
            ImGui.BeginDisabled();
        if (ImGui.MenuItem(Name, Shortcut, ref _show))
        {
            // Opening from the menu should follow the same refresh path as any other show transition.
            OnShow();
        }
        if(!Enabled)
            ImGui.EndDisabled();
    }

    /// <summary>
    /// Synchronizes layout persistence, begins the ImGui window, draws the concrete contents,
    /// and records the final rectangle for UI hit testing.
    /// </summary>
    public void Draw()
    {
        if(!Config.Instance.Layout.ContainsKey(WindowId))
        {
            // Seed the layout store the first time this window is encountered.
            Config.Instance.Layout.Add(WindowId, DefaultState);
            Show = Config.Instance.Layout[WindowId].IsOpen;
        }

        // Keep the persisted open state synchronized with the live toggle state.
        if (Show != Config.Instance.Layout[WindowId].IsOpen)
            Config.Instance.Layout[WindowId].IsOpen = Show;
        if (Show)
        {
            if (ImGui.Begin(Name, ref _show, WindowFlags))
            {
                // Derived windows only provide the body; the base class owns the surrounding frame.
                InternalDraw();
                Application.CEDGame.UIManager.AddCurrentWindowRect();
            }
            ImGui.End();
        }
    }

    /// <summary>
    /// Draws the implementation-specific contents of the window inside the common wrapper.
    /// </summary>
    protected abstract void InternalDraw();
}