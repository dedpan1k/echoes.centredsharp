using System.Reflection;
using CentrED.Map;
using CentrED.UI;
using ClassicUO.Utility.Logging;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Input;
using static CentrED.Application;
using static SDL3.SDL;

namespace CentrED;

/// <summary>
/// Hosts the main editor game loop, graphics device, and top-level map and UI systems.
/// </summary>
public class CentrEDGame : Game
{
    /// <summary>
    /// Provides access to the graphics device configuration for the running editor instance.
    /// </summary>
    public readonly GraphicsDeviceManager _gdm;

    private Keymap _keymap;

    /// <summary>
    /// Gets the map subsystem used for rendering and editing world content.
    /// </summary>
    public MapManager MapManager;

    /// <summary>
    /// Gets the UI subsystem used for the main window and auxiliary editor windows.
    /// </summary>
    public UIManager UIManager;

    /// <summary>
    /// Gets or sets a value indicating whether the editor is in the process of shutting down.
    /// </summary>
    public bool Closing { get; set; }
    
    /// <summary>
    /// Initializes a new instance of the <see cref="CentrEDGame"/> class.
    /// </summary>
    public CentrEDGame()
    {
        _gdm = new GraphicsDeviceManager(this)
        {
            IsFullScreen = false,
            PreferredDepthStencilFormat = DepthFormat.Depth24
        };

        _gdm.PreparingDeviceSettings += (sender, e) =>
        {
            e.GraphicsDeviceInformation.PresentationParameters.RenderTargetUsage =
                RenderTargetUsage.DiscardContents;
        };
        var appName = Assembly.GetExecutingAssembly().GetName();
        SDL_SetWindowTitle(Window.Handle, $"{appName.Name} {appName.Version}");

        SDL_ShowCursor();
        SDL_SetWindowResizable(Window.Handle, true);
        Window.ClientSizeChanged += OnWindowResized;
    }
    
    protected override void Initialize()
    {
        if (_gdm.GraphicsDevice.Adapter.IsProfileSupported(GraphicsProfile.HiDef))
        {
            _gdm.GraphicsProfile = GraphicsProfile.HiDef;
        }

        _gdm.ApplyChanges();

        Log.Start(LogTypes.All);
        LangManager.Load();
        // Create the UI manager before the map manager because editor tools may
        // depend on UI windows being available during map initialization.
        _keymap =  new Keymap();
        UIManager = new UIManager(_gdm.GraphicsDevice, Window, _keymap);
        MapManager = new MapManager(_gdm.GraphicsDevice, Window, _keymap);
        RadarMap.Initialize(_gdm.GraphicsDevice);

        base.Initialize();
    }

    protected override void BeginRun()
    {
        base.BeginRun();
        SDL_MaximizeWindow(Window.Handle);
    }

    protected override void UnloadContent()
    {
        CEDClient.Disconnect();
    }

    protected override void Update(GameTime gameTime)
    {
        try
        {
            _keymap.Update(Keyboard.GetState());
            Metrics.Start("UpdateClient");
            if(CEDClient.Running)
                CEDClient.Update();
            Metrics.Stop("UpdateClient");
            MapManager.Update(gameTime, IsActive, !UIManager.CapturingMouse, !UIManager.CapturingKeyboard);
            Config.AutoSave();
        }
        catch(Exception e)
        {
            UIManager.ReportCrash(e);
        }
        base.Update(gameTime);
    }

    protected override bool BeginDraw()
    {
        Metrics.Start("BeginDraw");
        // Use the maximum UI-managed window size so the back buffer can cover
        // the main viewport and any auxiliary editor windows.
        var maxWindowSize = UIManager.MaxWindowSize();
        var width = (int)maxWindowSize.X;
        var height = (int)maxWindowSize.Y;
        if (width > 0 && height > 0)
        {
            var pp = GraphicsDevice.PresentationParameters;
            if (width != pp.BackBufferWidth || height != pp.BackBufferHeight)
            {
                pp.BackBufferWidth = width;
                pp.BackBufferHeight = height;
                pp.DeviceWindowHandle = Window.Handle;
                GraphicsDevice.Reset(pp);
            }
        }
        Metrics.Stop("BeginDraw");
        return base.BeginDraw();
    }

    protected override void Draw(GameTime gameTime)
    {
        if (gameTime.ElapsedGameTime.Ticks > 0)
        {
            try
            {
                Metrics.Start("Draw");
                MapManager.Draw();
                UIManager.Draw();
                Present();
                UIManager.DrawExtraWindows();
                MapManager.AfterDraw();
                Metrics.Stop("Draw");
            }
            catch (Exception e)
            {
                Console.WriteLine(e);
                UIManager.ReportCrash(e);
            }
        }
        base.Draw(gameTime);
    }

    private void Present()
    {
        Rectangle bounds = Window.ClientBounds;
        GraphicsDevice.Present(
            new Rectangle(0, 0, bounds.Width, bounds.Height),
            null,
            Window.Handle
        );
    }

    protected override void EndDraw()
    {
        // Restore the main window viewport and scissor rectangle so the next
        // frame starts from a known graphics state.
        var gameWindowRect = Window.ClientBounds;
        GraphicsDevice.Viewport = new Viewport(0, 0, gameWindowRect.Width, gameWindowRect.Height);
        GraphicsDevice.ScissorRectangle = new Rectangle(0, 0, gameWindowRect.Width, gameWindowRect.Height);
    }

    private void OnWindowResized(object? sender, EventArgs e)
    {
        GameWindow window = sender as GameWindow;
        if (window != null)
            MapManager.OnWindowsResized(window);
    }
}