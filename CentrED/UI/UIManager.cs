using CentrED.IO;
using CentrED.IO.Models;
using CentrED.Map;
using CentrED.Renderer;
using CentrED.Tools;
using CentrED.UI.Windows;
using Hexa.NET.ImGui;
using Hexa.NET.ImGui.Backends.SDL3;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using static SDL3.SDL;
using static CentrED.Application;
using static CentrED.LangEntry;
using Rectangle = System.Drawing.Rectangle;
using Vector2 = System.Numerics.Vector2;
using Vector4 = System.Numerics.Vector4;
using FNARectangle = Microsoft.Xna.Framework.Rectangle;

namespace CentrED.UI;

/// <summary>
/// Owns the ImGui context for the editor, routes SDL events into ImGui, and coordinates
/// the set of application windows that participate in the main docking layout.
/// </summary>
public class UIManager
{
    public enum Category
    {
        Menu, 
        Main,
        Tools,
    }

    internal UIRenderer _uiRenderer;
    private GraphicsDevice _graphicsDevice;
    private Keymap _keymap;
    
    // SDL keeps a single global event filter, so UIManager has to wrap the previous one
    // rather than replacing it outright.
    private SDL_EventFilter _EventFilter;
    private SDL_EventFilter _PrevEventFilter;

    public readonly bool HasViewports;

    // Windows are stored both by concrete type for direct access and by category so they
    // can be exposed in the appropriate menu.
    internal Dictionary<Type, Window> AllWindows = new();
    internal List<Window> MainWindows = new();
    internal List<Window> ToolsWindows = new();
    internal List<Window> MenuWindows = new();

    internal DebugWindow DebugWindow;
    public bool ShowTestWindow;
    private ImFontPtr[] _Fonts;
    public string[] FontNames { get; }
    private int _FontIndex;
    private int _FontSize;
    public event Action? FontChanged;
    private const float FooterDrawerMargin = 8f;
    private const float FooterDrawerGap = 8f;
    private const float FooterDrawerAnimationSpeed = 8f;
    private bool _showConnectDrawer;
    private bool _showServerDrawer;
    private float _connectDrawerProgress;
    private float _serverDrawerProgress;

    /// <summary>
    /// Notifies windows that cached measurements or layout may need to be refreshed after
    /// the current font family or size changes.
    /// </summary>
    public void OnFontChanged()
    {
        FontChanged?.Invoke();
    }

    /// <summary>
    /// Updates the active font size and immediately pushes the new font onto the current
    /// ImGui font stack so subsequent widgets render with the new size in the same frame.
    /// </summary>
    public int FontSize
    {
        get => _FontSize;
        set
        {
            _FontSize = value;
            Config.Instance.FontSize = _FontSize;
            ImGui.PopFont();
            ImGui.PushFont(_Fonts[_FontIndex], _FontSize);
            OnFontChanged();
        }
    }

    /// <summary>
    /// Switches to a different loaded font while preserving the configured font size.
    /// </summary>
    public int FontIndex
    {
        get => _FontIndex;
        set
        {
            _FontIndex = value;
            Config.Instance.FontName = FontNames[_FontIndex];
            ImGui.PopFont();
            ImGui.PushFont(_Fonts[_FontIndex], _FontSize);
            OnFontChanged();
        }
    }

    /// <summary>
    /// Creates the ImGui context, configures platform backends, registers the default set
    /// of editor windows, and restores persisted font preferences.
    /// </summary>
    public unsafe UIManager(GraphicsDevice gd, GameWindow window, Keymap keymap)
    {
        _graphicsDevice = gd;
        _keymap = keymap;

        var context = ImGui.CreateContext();
        ImGui.SetCurrentContext(context);
        ImGuiImplSDL3.SetCurrentContext(context);
        var io = ImGui.GetIO();

        var glContext = SDL_GL_GetCurrentContext();
        if (glContext == IntPtr.Zero) 
        {
            // FNA can run without an OpenGL context; in that case SDL3's GPU path is used.
            ImGuiImplSDL3.InitForSDLGPU((SDLWindow*)window.Handle);
            // Multi-viewport support is still available on the GPU backend.
            io.BackendFlags |= ImGuiBackendFlags.RendererHasViewports;
        }
        else
        {
            ImGuiImplSDL3.InitForOpenGL((SDLWindow*)window.Handle, (void*)glContext);
        }
        io.BackendFlags |= ImGuiBackendFlags.RendererHasVtxOffset;
        io.BackendFlags |= ImGuiBackendFlags.RendererHasTextures;
        
        HasViewports = io.BackendFlags.HasFlag(ImGuiBackendFlags.RendererHasViewports) && io.BackendFlags.HasFlag(ImGuiBackendFlags.PlatformHasViewports);

        io.ConfigFlags |= ImGuiConfigFlags.DockingEnable;
        if(Config.Instance.Viewports)
            io.ConfigFlags |= ImGuiConfigFlags.ViewportsEnable;
        io.ConfigInputTrickleEventQueue = false;
        ApplyTheme(Config.Instance.ThemePreset);
        
        if (!File.Exists("imgui.ini") && File.Exists("imgui.ini.default"))
        {
            // Seed the first launch with the repository's default layout when the user has
            // not created a personal imgui.ini yet.
            ImGui.LoadIniSettingsFromDisk("imgui.ini.default");
        }

        _uiRenderer = new UIRenderer(_graphicsDevice, HasViewports);
        
        AddWindow(Category.Main, new ConnectWindow());
        AddWindow(Category.Main, new ServerWindow());
        AddWindow(Category.Main, new OptionsWindow(_keymap));
        AddWindow(Category.Main, new ExportWindow());

        AddWindow(Category.Tools, new InfoWindow());
        AddWindow(Category.Tools, new ToolboxWindow());
        AddWindow(Category.Tools, new FilterWindow());
        AddWindow(Category.Tools, new TilesWindow());
        AddWindow(Category.Tools, new HuesWindow());
        AddWindow(Category.Tools, new BlueprintsWindow());
        AddWindow(Category.Tools, new AssetWorkspaceWindow());
        AddWindow(Category.Tools, new LandBrushManagerWindow());
        AddWindow(Category.Tools, new LSOWindow());
        AddWindow(Category.Tools, new ChatWindow());
        AddWindow(Category.Tools, new HistoryWindow());
        AddWindow(Category.Tools, new ServerAdminWindow());
        AddWindow(Category.Tools, new ImageOverlayWindow());

        AddWindow(Category.Menu, new MinimapWindow());
        DebugWindow = new DebugWindow();
        
        // Hook the global SDL event filter so detached ImGui platform windows continue to
        // forward input events to the current ImGui context.
        IntPtr prevUserData;
        SDL_GetEventFilter(
            out _PrevEventFilter,
            out prevUserData
        );
        _EventFilter = EventFilter;
        SDL_SetEventFilter(
            _EventFilter,
            prevUserData
        );
        
        LoadFonts();
        FontNames = _Fonts.Select(x => x.GetDebugNameS()).ToArray();
        var fontIndex = Array.IndexOf(FontNames, Config.Instance.FontName);
        if (fontIndex != -1)
        {
            _FontIndex = fontIndex;
        }
        _FontSize = Config.Instance.FontSize;
    }

    /// <summary>
    /// Applies the configured global ImGui theme and remembers the chosen preset.
    /// </summary>
    public void ApplyTheme(UIThemePreset preset)
    {
        Config.Instance.ThemePreset = preset;
        ThemeManager.Apply(preset);
    }
    
    /// <summary>
    /// Forwards every SDL event to the SDL3 ImGui backend before handing it back to any
    /// previously registered filter in the chain.
    /// </summary>
    private unsafe bool EventFilter(IntPtr userdata, SDL_Event* evt)
    {
        ImGuiImplSDL3.ProcessEvent((SDLEvent*)evt);
        if (_PrevEventFilter != null)
        {
            return _PrevEventFilter(userdata, evt);
        }
        return true;
    }

    /// <summary>
    /// Loads the built-in default font plus any TrueType fonts located next to the game
    /// executable so they can be selected at runtime.
    /// </summary>
    private unsafe void LoadFonts()
    {
        var io = ImGui.GetIO();
        var fontFiles = Directory.GetFiles(".", "*.ttf");
        _Fonts = new ImFontPtr[fontFiles.Length + 1];
        _Fonts[0] = io.Fonts.AddFontDefault();
        var fontIndex = 1;
        foreach (var fontFile in fontFiles)
        {
            _Fonts[fontIndex] = io.Fonts.AddFontFromFileTTF(fontFile);
            fontIndex++;
        }
    }

    /// <summary>
    /// Registers a window for lookup and for menu placement based on its category.
    /// </summary>
    public void AddWindow(Category category, Window window)
    {
        AllWindows.Add(window.GetType(), window);
        switch (category)
        {
            case Category.Main: 
                MainWindows.Add(window);
                break;
            case Category.Tools:
                ToolsWindows.Add(window);
                break;
            case Category.Menu:
                MenuWindows.Add(window); 
                break;
        }
    }
    
    public bool CapturingMouse => ImGui.GetIO().WantCaptureMouse;
    public bool CapturingKeyboard => ImGui.GetIO().WantCaptureKeyboard;
    
    private bool openContextMenu;
    private TileObject? contextMenuTile;

    /// <summary>
    /// Returns the largest viewport dimensions currently managed by ImGui. Detached windows
    /// can live on different monitors, so the maximum viewport size is more useful than the
    /// main viewport alone when sizing popups or auxiliary windows.
    /// </summary>
    public Vector2 MaxWindowSize()
    {
        int x = 0;
        int y = 0;
        var platformIO = ImGui.GetPlatformIO();
        for (int i = 0; i < platformIO.Viewports.Size; i++)
        {
            ImGuiViewportPtr vp = platformIO.Viewports[i];
            x = Math.Max(x, (int)vp.Size.X);
            y = Math.Max(y, (int)vp.Size.Y);
        }
        return new Vector2(x, y);
    }

    /// <summary>
    /// Starts a new ImGui frame and clears the list of rectangles used for hit-testing UI
    /// coverage against world interactions.
    /// </summary>
    public void NewFrame()
    {
        if(ImGui.GetMainViewport().PlatformRequestClose)
            CEDGame.Exit();
        Metrics.Start("NewFrameUI");
        ImGuiImplSDL3.NewFrame();
        Metrics.Stop("NewFrameUI");
        _windowRects.Clear();
    }

    private List<Rectangle> _windowRects = [];

    /// <summary>
    /// Captures the current ImGui window bounds so the game can tell whether mouse input is
    /// over the editor UI instead of over the world viewport.
    /// </summary>
    public void AddCurrentWindowRect()
    {
        var pos = ImGui.GetWindowPos();
        var size =  ImGui.GetWindowSize();
        _windowRects.Add(new Rectangle((int)pos.X, (int)pos.Y, (int)size.X, (int)size.Y));
    }

    /// <summary>
    /// Checks whether a screen position falls inside any window rectangle recorded during the
    /// current frame.
    /// </summary>
    public bool IsOverUI(int x, int y)
    {
        foreach (var rect in _windowRects)
        {
            if (rect.Contains(x, y))
                return true;
        }
        return false;
    }
    

    /// <summary>
    /// Executes the main UI pass for the primary window.
    /// </summary>
    public void Draw()
    {
        NewFrame();
        Metrics.Start("DrawUI");
        ImGui.NewFrame();
        DrawUI();
        ImGui.Render();
        _uiRenderer.RenderMainWindow();
        Metrics.Stop("DrawUI");
    }

    /// <summary>
    /// Renders ImGui platform windows when multi-viewport support is enabled.
    /// </summary>
    public void DrawExtraWindows()
    {
        if (ImGui.GetIO().ConfigFlags.HasFlag(ImGuiConfigFlags.ViewportsEnable))
        {
            Metrics.Start("DrawUIWindows");
            ImGui.UpdatePlatformWindows();
            ImGui.RenderPlatformWindowsDefault();
            Metrics.Stop("DrawUIWindows");
        }
    }

    /// <summary>
    /// Schedules the tile context menu to open on the next frame so it runs inside a valid
    /// ImGui popup lifecycle.
    /// </summary>
    public void OpenContextMenu(TileObject? selected)
    {
        openContextMenu = true;
        contextMenuTile = selected;
    }

    private bool _resetLayout;

    /// <summary>
    /// Draws the full editor UI for the current frame, including the docking host, menus,
    /// status surfaces, registered windows, and transient popups.
    /// </summary>
    protected virtual void DrawUI()
    {
        ShowCrashInfo();
        if (CEDGame.Closing)
            return;
        ImGui.PushFont(_Fonts[_FontIndex], Config.Instance.FontSize);
        ServerStatePopup();
        
        if (_resetLayout)
        {
            // Reset both ImGui's persisted layout file and the in-memory window state cache
            // so the default layout applies cleanly on the next frame.
            ImGui.LoadIniSettingsFromDisk("imgui.ini.default");
            Config.Instance.Layout = new Dictionary<string, WindowState>();
            _resetLayout = false;
        }
        DrawMainMenu();
        DrawApplicationToolbar();
        DrawStatusBar();
        DrawFooterDrawers();
        ImGui.DockSpaceOverViewport(ImGuiDockNodeFlags.PassthruCentralNode | ImGuiDockNodeFlags.NoDockingOverCentralNode);
        DrawContextMenu();
        foreach (var window in AllWindows.Values)
        { 
            window.Draw();   
        }
        DebugWindow.Draw();
        if (ShowTestWindow)
        {
            ImGui.SetNextWindowPos(new Vector2(650, 20), ImGuiCond.FirstUseEver);
            ImGui.ShowDemoWindow(ref ShowTestWindow);
        }
        ImGui.PopFont();
    }
    
    /// <summary>
    /// Draws the right-click context menu for the currently selected tile, exposing only the
    /// actions that make sense for the selected object type.
    /// </summary>
    private void DrawContextMenu()
    {
        var selected = contextMenuTile;
        if (openContextMenu)
        {
            if(selected != null)
                ImGui.OpenPopup("MainPopup");
            openContextMenu = false;
        }
        if (ImGui.BeginPopup("MainPopup"))
        {
            var close = false;
            if (selected != null)
            {
                if (ImGui.Button(LangManager.Get(GRAB_TILE)))
                {
                    GetWindow<TilesWindow>().UpdateSelection(selected);
                    close = true;
                }
                if (ImGui.Button(LangManager.Get(GRAB_Z)))
                {
                    CEDGame.MapManager.ActiveTool.GrabZ(selected.Tile.Z);
                    close = true;
                }
                if (selected is StaticObject so)
                {
                    if (ImGui.Button(LangManager.Get(GRAB_HUE)))
                    {
                        GetWindow<HuesWindow>().UpdateSelection(so);
                        close = true;
                    }
                    if (ImGui.Button(LangManager.Get(FILTER_TILE)))
                    {
                        if (!CEDGame.MapManager.ObjectIdFilter.Add(so.Tile.Id))
                            CEDGame.MapManager.ObjectIdFilter.Remove(so.Tile.Id);
                        close = true;
                    }
                }
            }
            if(close)
            {
                contextMenuTile = null;
                ImGui.CloseCurrentPopup();
            }
            ImGui.EndPopup();
        }
    }

    /// <summary>
    /// Builds the top-level menu bar and delegates per-window menu entries to the registered
    /// window instances.
    /// </summary>
    private void DrawMainMenu()
    {
        if (ImGui.BeginMainMenuBar())
        {
            if (ImGui.BeginMenu("CentrED"))
            {
                MainWindows.ForEach(w => w.DrawMenuItem());
                ImGui.Separator();
                if (ImGui.MenuItem(LangManager.Get(QUIT)))
                    CEDGame.Exit();
                ImGui.EndMenu();
            }
            if (ImGui.BeginMenu(LangManager.Get(EDIT)))
            {
                if (ImGui.MenuItem(LangManager.Get(UNDO), "Ctrl+Z", false, CEDClient.CanUndo))
                {
                    CEDClient.Undo();
                }
                if (ImGui.MenuItem(LangManager.Get(REDO), "Ctrl+Shift+Z", false, CEDClient.CanRedo))
                {
                    CEDClient.Redo();
                }
                ImGui.EndMenu();
            }
            if (ImGui.BeginMenu(LangManager.Get(VIEW)))
            {
                if (ImGui.MenuItem(LangManager.Get(RESET_ZOOM), "ESC"))
                {
                    CEDGame.MapManager.Camera.ResetCamera();
                }
                ImGui.MenuItem(LangManager.Get(WALKABLE_SURFACES), "Ctrl + W", ref CEDGame.MapManager.WalkableSurfaces);
                if (ImGui.BeginMenu(LangManager.Get(FLAT_VIEW)))
                {
                    if (ImGui.MenuItem(LangManager.Get(ENABLED), "Ctrl + F", ref CEDGame.MapManager.FlatView));
                    {
                        CEDGame.MapManager.UpdateAllTiles();
                    }
                    ImGui.MenuItem(LangManager.Get(SHOW_HEIGHT), "Ctrl + H", ref CEDGame.MapManager.FlatShowHeight);
                    ImGui.EndMenu();
                }
                ImGui.MenuItem(LangManager.Get(ANIMATE_OBJECTS), _keymap.GetShortcut(Keymap.ToggleAnimatedStatics), ref CEDGame.MapManager.AnimatedStatics);
                ImGui.MenuItem(LangManager.Get(TERRAIN_GRID), "Ctrl + G", ref CEDGame.MapManager.ShowGrid);
                ImGui.MenuItem(LangManager.Get(NODRAW_TILES), "", ref CEDGame.MapManager.ShowNoDraw);
                ImGui.EndMenu();
            }
            if (ImGui.BeginMenu(LangManager.Get(TOOLS)))
            {
                ToolsWindows.ForEach(w => w.DrawMenuItem());
                ImGui.EndMenu();
            }
            
            MenuWindows.ForEach(w => w.DrawMenuItem());
            if (ImGui.BeginMenu(LangManager.Get(HELP)))
            {
                if (ImGui.MenuItem(LangManager.Get(RESET_LAYOUT), false, File.Exists("imgui.ini.default")))
                {
                    _resetLayout = true;
                }
                if (ImGui.MenuItem(LangManager.Get(CLEAR_CACHE), "CTRL+R"))
                {
                    CEDGame.MapManager.Reset();
                }
                //Credits
                //About
                ImGui.Separator();
                DebugWindow.DrawMenuItem();
                ImGui.EndMenu();
            }
            CEDGame.UIManager.AddCurrentWindowRect();
            ImGui.EndMainMenuBar();
        }
    }

    /// <summary>
    /// Draws the fixed application toolbar beneath the main menu when the compact navbar mode is enabled.
    /// </summary>
    private void DrawApplicationToolbar()
    {
        GetWindow<ToolboxWindow>().DrawApplicationToolbar();
    }

    /// <summary>
    /// Displays connection state, selection details, tool metadata, and runtime performance
    /// information in the bottom status bar.
    /// </summary>
    private void DrawStatusBar()
    {
        if (ImGuiEx.BeginStatusBar())
        {
            var connectWindow = GetWindow<ConnectWindow>();
            var serverWindow = GetWindow<ServerWindow>();

            if (DrawFooterToggle($"{connectWindow.StatusText}###FooterConnectToggle", connectWindow.StatusColor, _showConnectDrawer))
            {
                _showConnectDrawer = !_showConnectDrawer;
                if (_showConnectDrawer)
                {
                    connectWindow.OnShow();
                }
            }
            ImGui.SameLine();
            if (DrawFooterToggle($"{serverWindow.Name}: {serverWindow.StatusText}###FooterServerToggle", serverWindow.StatusColor, _showServerDrawer))
            {
                _showServerDrawer = !_showServerDrawer;
                if (_showServerDrawer)
                {
                    serverWindow.OnShow();
                }
            }
            if(CEDClient.Running)
            {
                ImGui.SameLine();
                ImGui.Text($"{ProfileManager.ActiveProfile.Name} ({CEDClient.AccessLevel})");
                ImGui.SameLine();
                ImGui.TextDisabled("|");
                ImGui.SameLine();
                var mapManager = CEDGame.MapManager;
                if (mapManager.Selected != null)
                {
                    string tileDisplay = mapManager.Selected switch
                    {
                        LandObject land => $"Land {land.Tile.Id.FormatId()} <{land.Tile.X},{land.Tile.Y},{land.Tile.Z}>",
                        StaticObject stat => $"Object {stat.Tile.Id.FormatId()} <{stat.Tile.X},{stat.Tile.Y},{stat.Tile.Z}> Hue:{((StaticTile)stat.Tile).Hue.FormatId()}",
                        _ => mapManager.Selected.Tile?.ToString() ?? "Unknown"
                    };
                    ImGui.Text(tileDisplay);
                }
                ImGui.SameLine();
                if (mapManager.ActiveTool is BaseTool bt && bt.AreaMode)
                {
                    ImGui.Text($"Area: {bt.Area.Width}x{bt.Area.Height}");
                    ImGui.SameLine();
                }
                var rightAligned = $"X: {mapManager.TilePosition.X} Y: {mapManager.TilePosition.Y} ({CEDClient.WidthInTiles}x{CEDClient.HeightInTiles}) Zoom: {mapManager.Camera.Zoom:F1} | FPS: {ImGui.GetIO().Framerate:F1}";
                ImGui.SetCursorPosX(ImGui.GetWindowWidth() - ImGui.CalcTextSize(rightAligned).X - ImGui.GetStyle().WindowPadding.X);
                ImGui.Text(rightAligned);
            }
            AddCurrentWindowRect();
            ImGuiEx.EndStatusBar();
        }
    }

    private bool DrawFooterToggle(string label, Vector4 color, bool isOpen)
    {
        var buttonColor = new Vector4(color.X, color.Y, color.Z, isOpen ? 0.38f : 0.22f);
        var hoveredColor = new Vector4(color.X, color.Y, color.Z, isOpen ? 0.5f : 0.34f);
        var activeColor = new Vector4(color.X, color.Y, color.Z, 0.58f);

        ImGui.PushStyleColor(ImGuiCol.Button, buttonColor);
        ImGui.PushStyleColor(ImGuiCol.ButtonHovered, hoveredColor);
        ImGui.PushStyleColor(ImGuiCol.ButtonActive, activeColor);
        ImGui.PushStyleColor(ImGuiCol.Text, color);
        var clicked = ImGui.Button(label);
        ImGui.PopStyleColor(4);
        return clicked;
    }

    private void DrawFooterDrawers()
    {
        _connectDrawerProgress = UpdateDrawerProgress(_connectDrawerProgress, _showConnectDrawer);
        _serverDrawerProgress = UpdateDrawerProgress(_serverDrawerProgress, _showServerDrawer);

        var connectWindow = GetWindow<ConnectWindow>();
        var serverWindow = GetWindow<ServerWindow>();
        var drawConnect = _connectDrawerProgress > 0f;
        var drawServer = _serverDrawerProgress > 0f;
        if (!drawConnect && !drawServer)
        {
            return;
        }

        var viewport = ImGui.GetMainViewport();
        var availableWidth = viewport.WorkSize.X - FooterDrawerMargin * 2f;
        var connectWidth = 0f;
        var serverWidth = 0f;

        if (drawConnect && drawServer)
        {
            availableWidth -= FooterDrawerGap;
            var totalPreferredWidth = connectWindow.PreferredDrawerSize.X + serverWindow.PreferredDrawerSize.X;
            var widthScale = totalPreferredWidth > availableWidth ? availableWidth / totalPreferredWidth : 1f;
            connectWidth = connectWindow.PreferredDrawerSize.X * widthScale;
            serverWidth = serverWindow.PreferredDrawerSize.X * widthScale;
        }
        else if (drawConnect)
        {
            connectWidth = MathF.Min(connectWindow.PreferredDrawerSize.X, availableWidth);
        }
        else if (drawServer)
        {
            serverWidth = MathF.Min(serverWindow.PreferredDrawerSize.X, availableWidth);
        }

        var currentOffsetX = FooterDrawerMargin;
        var bottomOffset = ImGui.GetFrameHeight() + FooterDrawerMargin;

        if (drawConnect)
        {
            DrawFooterDrawer(
                "Connect###FooterConnectDrawer",
                connectWindow.Name,
                connectWidth,
                connectWindow.PreferredDrawerSize.Y * _connectDrawerProgress,
                currentOffsetX,
                bottomOffset,
                ref _showConnectDrawer,
                connectWindow.DrawDrawerContents);
            currentOffsetX += connectWidth + FooterDrawerGap;
        }

        if (drawServer)
        {
            DrawFooterDrawer(
                "Local Server###FooterServerDrawer",
                serverWindow.Name,
                serverWidth,
                serverWindow.PreferredDrawerSize.Y * _serverDrawerProgress,
                currentOffsetX,
                bottomOffset,
                ref _showServerDrawer,
                serverWindow.DrawDrawerContents);
        }
    }

    private void DrawFooterDrawer(
        string id,
        string title,
        float width,
        float height,
        float offsetX,
        float bottomOffset,
        ref bool isOpen,
        Action drawContents)
    {
        if (width <= 0f || height <= 1f)
        {
            return;
        }

        var viewport = ImGui.GetMainViewport();
        var position = new Vector2(
            viewport.WorkPos.X + offsetX,
            viewport.WorkPos.Y + viewport.WorkSize.Y - bottomOffset - height);
        var size = new Vector2(width, height);

        if (ImGuiEx.BeginFooterDrawer(id, position, size))
        {
            ImGui.TextDisabled(title.Split("###")[0]);
            var closeLabel = LangManager.Get(HIDE);
            var closeWidth = ImGui.CalcTextSize(closeLabel).X + ImGui.GetStyle().FramePadding.X * 2f;
            ImGui.SameLine();
            ImGui.SetCursorPosX(MathF.Max(ImGui.GetCursorPosX(), ImGui.GetWindowWidth() - closeWidth - 10f));
            if (ImGui.SmallButton($"{closeLabel}##{id}"))
            {
                isOpen = false;
            }
            ImGui.Separator();
            drawContents();
            AddCurrentWindowRect();
        }
        ImGuiEx.EndFooterDrawer();
    }

    private static float UpdateDrawerProgress(float progress, bool show)
    {
        var delta = ImGui.GetIO().DeltaTime * FooterDrawerAnimationSpeed;
        return show ? MathF.Min(1f, progress + delta) : MathF.Max(0f, progress - delta);
    }

    internal bool DrawImage(Texture2D? tex, FNARectangle bounds)
    {
        return DrawImage(tex, new Rectangle(bounds.X, bounds.Y, bounds.Width, bounds.Height), new Vector2(bounds.Width, bounds.Height));
    }
    
    internal bool DrawImage(Texture2D? tex, FNARectangle bounds, Vector2 size, bool stretch = false)
    {
        return DrawImage(tex,  new Rectangle(bounds.X, bounds.Y, bounds.Width, bounds.Height), size, stretch);
    }

    
    internal bool DrawImage(Texture2D? tex, Rectangle bounds)
    {
        return DrawImage(tex, bounds, new Vector2(bounds.Width, bounds.Height));
    }
    
    /// <summary>
    /// Draws either a cropped or stretched view of a texture into the current ImGui layout.
    /// The supplied bounds describe the source rectangle inside the texture.
    /// </summary>
    internal unsafe bool DrawImage(Texture2D? tex, Rectangle bounds, Vector2 size, bool stretch = false)
    {
        var safeSize = new Vector2(MathF.Max(0, size.X), MathF.Max(0, size.Y));
        if (safeSize.X <= 0 || safeSize.Y <= 0 || bounds.Width <= 0 || bounds.Height <= 0)
        {
            ImGui.Dummy(safeSize);
            return false;
        }

        if (tex == null)
        {
            ImGui.Dummy(safeSize);
            return false;
        }
        var texPtr = _uiRenderer.BindTexture(tex);
        var oldPos = ImGui.GetCursorPos();

        // When the source region is smaller than the requested draw size, center it within
        // the reserved layout space rather than anchoring it to the top-left corner.
        var offsetX = (safeSize.X - bounds.Width) / 2;
        var offsetY = (safeSize.Y - bounds.Height) / 2;
        if (!stretch)
        {
            ImGui.Dummy(safeSize);
            ImGui.SetCursorPosX(oldPos.X + Math.Max(0, offsetX));
            ImGui.SetCursorPosY(oldPos.Y + Math.Max(0, offsetY));
        }
        var fWidth = (float)tex.Width;
        var fHeight = (float)tex.Height;
        var targetSize = stretch ? safeSize : new Vector2(Math.Min(bounds.Width, safeSize.X), Math.Min(bounds.Height, safeSize.Y));
        if (targetSize.X <= 0 || targetSize.Y <= 0)
        {
            return false;
        }

        var uvOffsetX = stretch ? 0 : Math.Min(0, offsetX);
        var uvOffsetY = stretch ? 0 : Math.Min(0, offsetY);

        // Negative offsets mean the caller asked for a source region larger than the display
        // area. Convert that clipping into UV coordinates so ImGui samples only the visible
        // subsection of the texture.
        var uv0 = new Vector2((bounds.X - uvOffsetX) / fWidth, (bounds.Y - uvOffsetY) / fHeight);
        var uv1 = new Vector2((bounds.X + bounds.Width + uvOffsetX) / fWidth, (bounds.Y + bounds.Height + uvOffsetY) / fHeight);
        ImGui.Image(new ImTextureRef(null, texPtr), targetSize, uv0, uv1);
        return true;
    }

    private bool _showCrashPopup;
    private string _crashText = "";

    /// <summary>
    /// Captures exception details for display on the next UI frame and prevents the editor
    /// from continuing normal interaction while in the crash state.
    /// </summary>
    public void ReportCrash(Exception exception)
    {
        _showCrashPopup = true;
        _crashText = exception.ToString();
        CEDGame.Closing = true;
    }

    /// <summary>
    /// Shows a modal crash dialog that allows the user to copy the stack trace or quit and
    /// persist the crash log to disk.
    /// </summary>
    public void ShowCrashInfo()
    {
        if (CEDGame.Closing)
        {
            ImGui.Begin("Crash");
            ImGui.OpenPopup("CrashWindow");
            if (ImGui.BeginPopupModal
                (
                    "CrashWindow",
                    ref _showCrashPopup,
                    ImGuiWindowFlags.AlwaysAutoResize | ImGuiWindowFlags.NoTitleBar
                ))
            {
                ImGui.Text(LangManager.Get(APP_CRASHED));
                ImGui.InputTextMultiline
                    (" ", ref _crashText, 1000, new Vector2(800, 150), ImGuiInputTextFlags.ReadOnly);
                if (ImGui.Button(LangManager.Get(COPY_TO_CLIPBOARD)))
                {
                    ImGui.SetClipboardText(_crashText);
                }
                ImGui.Separator();
                if (ImGui.Button(LangManager.Get(QUIT)))
                {
                    File.WriteAllText("Crash.log", _crashText);
                    CEDGame.Exit();
                }
                ImGui.EndPopup();
            }
            ImGui.End();
        }
    }

    private bool _showServerStatePopup;

    /// <summary>
    /// Displays a blocking modal while the server is busy with an operation that should keep
    /// the user from issuing conflicting requests.
    /// </summary>
    public void ServerStatePopup()
    {
        if (!CEDClient.Running)
        {
            _showServerStatePopup = false;
            return;
        }
        if (CEDClient.ServerState != ServerState.Running)
        {
            ImGui.OpenPopup("ServerState");
            _showServerStatePopup = true;
        }
        if (ImGui.BeginPopupModal
            (
                "ServerState",
                ref _showServerStatePopup,
        ImGuiWindowFlags.AlwaysAutoResize | ImGuiWindowFlags.NoTitleBar
            ))
        {
            ImGui.Text(LangManager.Get(SERVER_IS_PERFORMING_OPERATION));
            ImGui.Text($"{LangManager.Get(STATE)}: {CEDClient.ServerState.ToString()}");
            ImGui.Text($"{LangManager.Get(REASON)}: {CEDClient.ServerStateReason}");
            if (CEDClient.ServerState == ServerState.Running)
            {
                ImGui.CloseCurrentPopup();
            }
            ImGui.EndPopup();
        }
    }
    
    /// <summary>
    /// Returns a registered window by its concrete type.
    /// </summary>
    public T GetWindow<T>() where T : Window
    {
        return (T)AllWindows[typeof(T)];
    }
}
