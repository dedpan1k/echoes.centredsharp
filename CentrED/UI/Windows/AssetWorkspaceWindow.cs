using CentrED.Assets;
using CentrED.IO;
using CentrED.IO.Models;
using CentrED.UI;
using ClassicUO.Assets;
using Hexa.NET.ImGui;
using Microsoft.Xna.Framework.Graphics;
using System.Globalization;
using System.Numerics;
using static CentrED.Application;
using Rectangle = System.Drawing.Rectangle;

namespace CentrED.UI.Windows;

/// <summary>
/// Hosts the native asset-workspace shell that will grow into the centeredsharp replacement
/// for UO Fiddler's browsing, editing, and import/export surface.
/// </summary>
public sealed class AssetWorkspaceWindow : Window
{
    private const float MinimumSplitWidth = 520f;
    private const float MinimumPreviewWidth = 120f;

    private string _pathInput = string.Empty;
    private int _selectedFamilyIndex;
    private string _artFilterText = string.Empty;
    private bool _artObjectMode;
    private ushort _selectedLandId;
    private ushort _selectedStaticId;
    private string _artActionStatus = string.Empty;
    private string _textureFilterText = string.Empty;
    private ushort _selectedTextureId;
    private string _textureActionStatus = string.Empty;
    private bool _gumpMode;
    private string _gumpFilterText = string.Empty;
    private ushort _selectedGumpId;
    private string _gumpActionStatus = string.Empty;
    private bool _tileDataMode;
    private string _hueFilterText = string.Empty;
    private ushort _selectedHueId;
    private string _hueActionStatus = string.Empty;
    private ushort _loadedHueEditId = ushort.MaxValue;
    private string _hueNameEdit = string.Empty;
    private string _hueTableStartEdit = string.Empty;
    private string _hueTableEndEdit = string.Empty;
    private readonly string[] _hueColorEdits = new string[32];
    private bool _tiledataLandMode = true;
    private string _tiledataFilterText = string.Empty;
    private ushort _selectedTiledataId;
    private string _tiledataActionStatus = string.Empty;
    private bool _tiledataFilterInclusive = true;
    private bool _tiledataFilterMatchAll;
    private ulong _tiledataFilterValue;
    private ushort _loadedTiledataEditId = ushort.MaxValue;
    private bool _loadedTiledataEditLandMode = true;
    private string _tiledataNameEdit = string.Empty;
    private string _tiledataTextureIdEdit = string.Empty;
    private string _tiledataAnimationEdit = string.Empty;
    private string _tiledataWeightEdit = string.Empty;
    private string _tiledataQualityEdit = string.Empty;
    private string _tiledataQuantityEdit = string.Empty;
    private string _tiledataHueEdit = string.Empty;
    private string _tiledataStackingOffsetEdit = string.Empty;
    private string _tiledataValueEdit = string.Empty;
    private string _tiledataHeightEdit = string.Empty;
    private string _tiledataMiscDataEdit = string.Empty;
    private string _tiledataUnknown2Edit = string.Empty;
    private string _tiledataUnknown3Edit = string.Empty;
    private ulong _tiledataEditFlags;
    private bool _animationAnimDataMode = true;
    private string _animationFilterText = string.Empty;
    private ushort _selectedAnimationBodyId;
    private byte _selectedAnimationActionId;
    private byte _selectedAnimationDirectionId;
    private int _selectedAnimationFrameIndex;
    private bool _animationAnimatePreview = true;
    private string _animDataFilterText = string.Empty;
    private ushort _selectedAnimDataId;
    private string _animDataActionStatus = string.Empty;
    private string _newAnimDataIdText = string.Empty;
    private string _animDataFrameAddText = string.Empty;
    private bool _animDataFrameAddRelative = true;
    private bool _animDataImportOverwrite = true;
    private bool _animDataImportEraseExisting;
    private bool _animDataAnimatePreview = true;
    private ushort _loadedAnimDataEditId = ushort.MaxValue;
    private string _animDataFrameIntervalEdit = string.Empty;
    private string _animDataFrameStartEdit = string.Empty;
    private byte _animDataUnknownDisplay;
    private readonly List<string> _animDataFrameOffsetEdits = [];
    private int _selectedAnimDataFrameIndex = -1;

    /// <summary>
    /// Stable ImGui title/ID pair for the asset workspace.
    /// </summary>
    public override string Name => "Asset Workspace###AssetWorkspace";

    /// <summary>
    /// The asset workspace starts hidden until explicitly opened.
    /// </summary>
    public override WindowState DefaultState => new()
    {
        IsOpen = false,
    };

    /// <summary>
    /// Refreshes the workspace snapshot whenever the window becomes visible.
    /// </summary>
    public override void OnShow()
    {
        AssetWorkspace.Refresh();
        SyncPathInput();
        EnsureArtBrowserLoaded();
        EnsureTextureBrowserLoaded();
        EnsureGumpBrowserLoaded();
        EnsureHueTileDataBrowserLoaded();
        EnsureAnimationBrowserLoaded();
        EnsureAnimDataBrowserLoaded();
    }

    /// <summary>
    /// Draws the initial asset-workspace shell, including directory selection and family readiness.
    /// </summary>
    protected override void InternalDraw()
    {
        var workspace = AssetWorkspace;
        SyncSelectedIndex(workspace);

        DrawHeader(workspace);
        ImGui.Separator();
        DrawRootPathControls(workspace);
        ImGui.Separator();

        var availableWidth = MathF.Max(0f, ImGui.GetContentRegionAvail().X);
        if (availableWidth < MinimumSplitWidth)
        {
            ImGui.TextDisabled("Window narrowed: the asset workspace stacks its panes until more horizontal space is available.");

            if (ImGui.BeginChild("AssetFamilies", new Vector2(0, 190f), ImGuiChildFlags.Borders))
            {
                DrawFamilyList(workspace);
            }
            ImGui.EndChild();

            if (ImGui.BeginChild("AssetFamilyDetails", new Vector2(0, 0), ImGuiChildFlags.Borders))
            {
                DrawSelectedFamily(workspace);
            }
            ImGui.EndChild();
            return;
        }

        var leftWidth = MathF.Min(300f, MathF.Max(240f, availableWidth * 0.34f));
        if (ImGui.BeginChild("AssetFamilies", new Vector2(leftWidth, 0), ImGuiChildFlags.Borders | ImGuiChildFlags.ResizeX))
        {
            DrawFamilyList(workspace);
        }
        ImGui.EndChild();

        ImGui.SameLine();

        if (ImGui.BeginChild("AssetFamilyDetails", new Vector2(0, 0), ImGuiChildFlags.Borders))
        {
            DrawSelectedFamily(workspace);
        }
        ImGui.EndChild();
    }

    private void DrawHeader(AssetWorkspaceService workspace)
    {
        var dirtyAssetCount = AssetTileBrowser.DirtyCount + AssetTextureBrowser.DirtyCount + AssetGumpBrowser.DirtyCount + AssetHueTileDataBrowser.DirtyCount + AssetAnimDataBrowser.DirtyCount;
        var accent = workspace.HasValidRootPath
            ? new Vector4(0.28f, 0.72f, 0.44f, 1.0f)
            : new Vector4(0.87f, 0.50f, 0.24f, 1.0f);

        ImGui.TextColored(accent, "Native asset workspace");
        ImGui.SameLine();
        ImGui.TextDisabled($"{workspace.ReadyFamilyCount}/{workspace.Families.Count} families ready");
        if (dirtyAssetCount > 0)
        {
            ImGui.SameLine();
            ImGui.TextColored(new Vector4(0.92f, 0.66f, 0.22f, 1.0f), $"{dirtyAssetCount} staged asset change(s)");
        }
        ImGui.TextWrapped("This is the first implementation slice for the centeredsharp-native replacement of the UO Fiddler asset tooling surface. The window now owns path selection, asset-family discovery, and readiness state for the editors that follow.");
        ImGui.Spacing();
        ImGui.TextWrapped(workspace.StatusMessage);
    }

    private void DrawRootPathControls(AssetWorkspaceService workspace)
    {
        ImGui.Text("Ultima data directory");
        ImGui.InputText("##AssetWorkspacePath", ref _pathInput, 1024);
        ImGui.SameLine();
        if (ImGui.Button("Browse..."))
        {
            var defaultPath = _pathInput.Length == 0 ? Environment.CurrentDirectory : _pathInput;
            if (TinyFileDialogs.TrySelectFolder("Select Ultima data directory", defaultPath, out var newPath))
            {
                _pathInput = AssetWorkspaceService.NormalizePath(newPath);
            }
        }

        var canApply = AssetWorkspaceService.NormalizePath(_pathInput) != workspace.ConfiguredRootPath;
        ImGui.BeginDisabled(!canApply);
        if (ImGui.Button("Save Override"))
        {
            workspace.SetConfiguredRootPath(_pathInput);
            SyncPathInput();
        }
        ImGui.EndDisabled();

        ImGui.SameLine();
        ImGui.BeginDisabled(workspace.ConfiguredRootPath.Length == 0);
        if (ImGui.Button("Clear Override"))
        {
            workspace.SetConfiguredRootPath(string.Empty);
            SyncPathInput();
        }
        ImGui.EndDisabled();

        ImGui.SameLine();
        if (ImGui.Button("Refresh"))
        {
            workspace.Refresh();
        }

        var profilePath = AssetWorkspaceService.NormalizePath(ProfileManager.ActiveProfile.ClientPath);
        ImGui.SameLine();
        ImGui.BeginDisabled(profilePath.Length == 0);
        if (ImGui.Button("Use Active Profile"))
        {
            _pathInput = profilePath;
            workspace.SetConfiguredRootPath(profilePath);
            SyncPathInput();
        }
        ImGui.EndDisabled();

        var sourceLabel = workspace.UsingProfileFallback ? "active profile fallback" : "workspace override";
        if (workspace.ConfiguredRootPath.Length == 0 && workspace.ProfileRootPath.Length == 0)
        {
            sourceLabel = "not configured";
        }

        ImGui.Spacing();
        ImGui.TextDisabled($"Source: {sourceLabel}");
        ImGui.SameLine();
        ImGui.TextDisabled($"Files scanned: {workspace.DiscoveredFileCount}");
        ImGui.SameLine();
        ImGui.TextDisabled($"Last refresh: {workspace.LastRefreshUtc.ToLocalTime():HH:mm:ss}");
    }

    private void DrawFamilyList(AssetWorkspaceService workspace)
    {
        ImGui.TextDisabled("Tracked families");
        ImGui.Separator();

        for (var i = 0; i < workspace.Families.Count; i++)
        {
            var family = workspace.Families[i];
            var stateText = family.IsReady ? "Ready" : "Blocked";
            var color = family.IsReady
                ? new Vector4(0.28f, 0.72f, 0.44f, 1.0f)
                : new Vector4(0.84f, 0.44f, 0.24f, 1.0f);

            if (ImGui.Selectable($"{family.DisplayName}##AssetFamily{i}", i == _selectedFamilyIndex))
            {
                _selectedFamilyIndex = i;
            }

            ImGui.SameLine();
            ImGui.TextColored(color, stateText);
            ImGui.TextDisabled(family.Description);
            ImGui.Spacing();
        }
    }

    private void DrawSelectedFamily(AssetWorkspaceService workspace)
    {
        if (workspace.Families.Count == 0)
        {
            ImGui.TextDisabled("No asset families are available yet.");
            return;
        }

        var family = workspace.Families[_selectedFamilyIndex];
        var color = family.IsReady
            ? new Vector4(0.28f, 0.72f, 0.44f, 1.0f)
            : new Vector4(0.84f, 0.44f, 0.24f, 1.0f);

        ImGui.Text(family.DisplayName);
        ImGui.SameLine();
        ImGui.TextColored(color, family.IsReady ? "Ready for editor implementation" : "Missing required files");
        ImGui.Spacing();
        ImGui.TextWrapped(family.Description);
        ImGui.Separator();

        ImGui.TextDisabled("Resolved files");
        if (family.ResolvedFiles.Count == 0)
        {
            ImGui.TextWrapped("No required files for this family are currently resolved from the selected directory.");
        }
        else
        {
            foreach (var file in family.ResolvedFiles)
            {
                ImGui.BulletText(file);
            }
        }

        ImGui.Spacing();
        ImGui.TextDisabled("Missing requirements");
        if (family.MissingRequirements.Count == 0)
        {
            ImGui.TextWrapped("The workspace has the base files needed to start implementing this editor family.");
        }
        else
        {
            foreach (var requirement in family.MissingRequirements)
            {
                ImGui.BulletText(requirement);
            }
        }

        ImGui.Spacing();
        ImGui.TextDisabled("Planned centeredsharp surface");
        ImGui.TextWrapped(GetImplementationNotes(family.Kind));

        if (family.Kind == AssetWorkspaceFamilyKind.ArtAndLandTiles)
        {
            ImGui.Spacing();
            ImGui.Separator();
            DrawArtAndLandBrowser(workspace, family.IsReady);
        }

        if (family.Kind == AssetWorkspaceFamilyKind.GumpsAndTextures)
        {
            ImGui.Spacing();
            ImGui.Separator();
            DrawGumpsAndTexturesBrowser(workspace, family.IsReady);
        }

        if (family.Kind == AssetWorkspaceFamilyKind.AnimationsAndAnimData)
        {
            ImGui.Spacing();
            ImGui.Separator();
            DrawAnimationsAndAnimDataBrowser(workspace, family.IsReady);
        }

        if (family.Kind == AssetWorkspaceFamilyKind.HuesAndTileData)
        {
            ImGui.Spacing();
            ImGui.Separator();
            DrawHuesAndTileDataBrowser(workspace, family.IsReady);
        }
    }

    private void DrawArtAndLandBrowser(AssetWorkspaceService workspace, bool familyReady)
    {
        ImGui.TextDisabled("Implemented slice");
        ImGui.TextWrapped("This first real editor uses local asset loading to browse land and static art directly from the selected Ultima directory, without requiring a server connection.");

        if (!familyReady)
        {
            ImGui.TextWrapped("The Art and Land Tiles browser stays disabled until the required files for this family are available.");
            return;
        }

        EnsureArtBrowserLoaded();
        var browser = AssetTileBrowser;
        if (!browser.IsReady)
        {
            ImGui.TextWrapped(browser.StatusMessage);
            return;
        }

        if (_artActionStatus.Length > 0)
        {
            ImGui.TextWrapped(_artActionStatus);
        }

        var filterChanged = ImGui.InputText("Search##AssetArtFilter", ref _artFilterText, 128);
        ImGui.SameLine();
        if (ImGui.Button("Reload Local Assets"))
        {
            browser.EnsureLoaded(CEDGame.GraphicsDevice, workspace.EffectiveRootPath);
        }

        ImGui.SameLine();
        ImGui.BeginDisabled(browser.DirtyCount == 0);
        if (ImGui.Button("Save art.mul"))
        {
            SaveArtChanges(browser);
        }
        ImGui.EndDisabled();

        ImGui.SameLine();
        ImGuiEx.TwoWaySwitch("Land", "Statics", ref _artObjectMode, new Vector2(92, 20));
        var preferTexmaps = Config.Instance.PreferTexMaps;
        if (!_artObjectMode)
        {
            ImGui.SameLine();
            if (ImGui.Checkbox("Prefer texmaps", ref preferTexmaps))
            {
                Config.Instance.PreferTexMaps = preferTexmaps;
            }
        }

        if (filterChanged)
        {
            KeepSelectedIdVisible(browser);
        }

        var ids = browser.GetFilteredIds(_artObjectMode, _artFilterText);
        if (ids.Count == 0)
        {
            ImGui.TextWrapped("No art entries match the current filter.");
            return;
        }

        var selectedId = GetSelectedId(ids);
        var preview = browser.GetPreview(_artObjectMode, selectedId, Config.Instance.PreferTexMaps);

        var availableWidth = MathF.Max(0f, ImGui.GetContentRegionAvail().X);
        if (availableWidth < MinimumSplitWidth)
        {
            if (ImGui.BeginChild("ArtTileList", new Vector2(0, 220f), ImGuiChildFlags.Borders))
            {
                DrawArtTileList(browser, ids);
            }
            ImGui.EndChild();

            if (ImGui.BeginChild("ArtTilePreview", new Vector2(0, 0), ImGuiChildFlags.Borders))
            {
                DrawArtTilePreview(preview, browser);
            }
            ImGui.EndChild();
            return;
        }

        var listWidth = MathF.Min(340f, MathF.Max(260f, availableWidth * 0.40f));
        if (ImGui.BeginChild("ArtTileList", new Vector2(listWidth, 0), ImGuiChildFlags.Borders | ImGuiChildFlags.ResizeX))
        {
            DrawArtTileList(browser, ids);
        }
        ImGui.EndChild();

        ImGui.SameLine();
        if (ImGui.BeginChild("ArtTilePreview", new Vector2(0, 0), ImGuiChildFlags.Borders))
        {
            DrawArtTilePreview(preview, browser);
        }
        ImGui.EndChild();
    }

    private void DrawGumpsAndTexturesBrowser(AssetWorkspaceService workspace, bool familyReady)
    {
        ImGui.TextDisabled("Implemented slice");
        ImGui.TextWrapped("This family now has native texture-map and gump editors with browse, preview, replace/import/export, staged removal, and explicit save support.");

        if (!familyReady)
        {
            ImGui.TextWrapped("The Gumps and Textures browser stays disabled until the required files for this family are available.");
            return;
        }

        ImGuiEx.TwoWaySwitch("Textures", "Gumps", ref _gumpMode, new Vector2(112, 20));
        ImGui.Spacing();

        if (_gumpMode)
        {
            DrawGumpBrowser(workspace);
            return;
        }

        EnsureTextureBrowserLoaded();
        var browser = AssetTextureBrowser;
        if (!browser.IsReady)
        {
            ImGui.TextWrapped(browser.StatusMessage);
            return;
        }

        if (_textureActionStatus.Length > 0)
        {
            ImGui.TextWrapped(_textureActionStatus);
        }

        var filterChanged = ImGui.InputText("Search##AssetTextureFilter", ref _textureFilterText, 128);
        ImGui.SameLine();
        if (ImGui.Button("Reload Local Textures"))
        {
            browser.EnsureLoaded(CEDGame.GraphicsDevice, workspace.EffectiveRootPath);
        }

        ImGui.SameLine();
        ImGui.BeginDisabled(browser.DirtyCount == 0);
        if (ImGui.Button("Save texmaps.mul"))
        {
            SaveTextureChanges(browser);
        }
        ImGui.EndDisabled();

        if (filterChanged)
        {
            KeepSelectedTextureVisible(browser);
        }

        var ids = browser.GetFilteredIds(_textureFilterText);
        if (ids.Count == 0)
        {
            ImGui.TextWrapped("No texture entries match the current filter.");
            return;
        }

        var selectedId = GetSelectedTextureId(ids);
        var preview = browser.GetPreview(selectedId);

        var availableWidth = MathF.Max(0f, ImGui.GetContentRegionAvail().X);
        if (availableWidth < MinimumSplitWidth)
        {
            if (ImGui.BeginChild("TextureTileList", new Vector2(0, 220f), ImGuiChildFlags.Borders))
            {
                DrawTextureList(browser, ids);
            }
            ImGui.EndChild();

            if (ImGui.BeginChild("TextureTilePreview", new Vector2(0, 0), ImGuiChildFlags.Borders))
            {
                DrawTexturePreview(preview, browser);
            }
            ImGui.EndChild();
            return;
        }

        var listWidth = MathF.Min(340f, MathF.Max(260f, availableWidth * 0.40f));
        if (ImGui.BeginChild("TextureTileList", new Vector2(listWidth, 0), ImGuiChildFlags.Borders | ImGuiChildFlags.ResizeX))
        {
            DrawTextureList(browser, ids);
        }
        ImGui.EndChild();

        ImGui.SameLine();
        if (ImGui.BeginChild("TextureTilePreview", new Vector2(0, 0), ImGuiChildFlags.Borders))
        {
            DrawTexturePreview(preview, browser);
        }
        ImGui.EndChild();
    }

    private void DrawGumpBrowser(AssetWorkspaceService workspace)
    {
        EnsureGumpBrowserLoaded();
        var browser = AssetGumpBrowser;
        if (!browser.IsReady)
        {
            ImGui.TextWrapped(browser.StatusMessage);
            return;
        }

        if (_gumpActionStatus.Length > 0)
        {
            ImGui.TextWrapped(_gumpActionStatus);
        }

        var filterChanged = ImGui.InputText("Search##AssetGumpFilter", ref _gumpFilterText, 128);
        ImGui.SameLine();
        if (ImGui.Button("Reload Local Gumps"))
        {
            browser.EnsureLoaded(CEDGame.GraphicsDevice, workspace.EffectiveRootPath);
        }

        ImGui.SameLine();
        ImGui.BeginDisabled(browser.DirtyCount == 0);
        if (ImGui.Button("Save gumpart.mul"))
        {
            SaveGumpChanges(browser);
        }
        ImGui.EndDisabled();

        if (filterChanged)
        {
            KeepSelectedGumpVisible(browser);
        }

        var ids = browser.GetFilteredIds(_gumpFilterText);
        if (ids.Count == 0)
        {
            ImGui.TextWrapped("No gump entries match the current filter.");
            return;
        }

        var selectedId = GetSelectedGumpId(ids);
        var preview = browser.GetPreview(selectedId);

        var availableWidth = MathF.Max(0f, ImGui.GetContentRegionAvail().X);
        if (availableWidth < MinimumSplitWidth)
        {
            if (ImGui.BeginChild("GumpTileList", new Vector2(0, 220f), ImGuiChildFlags.Borders))
            {
                DrawGumpList(browser, ids);
            }
            ImGui.EndChild();

            if (ImGui.BeginChild("GumpTilePreview", new Vector2(0, 0), ImGuiChildFlags.Borders))
            {
                DrawGumpPreview(preview, browser);
            }
            ImGui.EndChild();
            return;
        }

        var listWidth = MathF.Min(340f, MathF.Max(260f, availableWidth * 0.40f));
        if (ImGui.BeginChild("GumpTileList", new Vector2(listWidth, 0), ImGuiChildFlags.Borders | ImGuiChildFlags.ResizeX))
        {
            DrawGumpList(browser, ids);
        }
        ImGui.EndChild();

        ImGui.SameLine();
        if (ImGui.BeginChild("GumpTilePreview", new Vector2(0, 0), ImGuiChildFlags.Borders))
        {
            DrawGumpPreview(preview, browser);
        }
        ImGui.EndChild();
    }

    private void DrawArtTileList(AssetTileBrowserService browser, List<ushort> ids)
    {
        if (ImGui.BeginTable("AssetArtTable", 3))
        {
            var clipper = ImGui.ImGuiListClipper();
            ImGui.TableSetupColumn("Id", ImGuiTableColumnFlags.WidthFixed, ImGui.CalcTextSize(0xFFFF.FormatId()).X);
            ImGui.TableSetupColumn("Graphic", ImGuiTableColumnFlags.WidthFixed, 44f);
            ImGui.TableSetupColumn("Name", ImGuiTableColumnFlags.WidthStretch);
            clipper.Begin(ids.Count);

            while (clipper.Step())
            {
                for (var i = clipper.DisplayStart; i < clipper.DisplayEnd; i++)
                {
                    var id = ids[i];
                    var tile = browser.GetPreview(_artObjectMode, id, Config.Instance.PreferTexMaps);
                    var selected = id == (_artObjectMode ? _selectedStaticId : _selectedLandId);

                    ImGui.PushID(i);
                    ImGui.TableNextRow(ImGuiTableRowFlags.None, 44f);
                    ImGui.TableNextColumn();
                    ImGui.Text(id.FormatId());

                    ImGui.TableNextColumn();
                    DrawPreviewImage(tile, new Vector2(44, 44), !_artObjectMode);

                    ImGui.TableNextColumn();
                    ImGui.PushStyleVar(ImGuiStyleVar.SelectableTextAlign, new Vector2(0.0f, 0.5f));
                    if (ImGui.Selectable(tile.Name.Length == 0 ? "(unnamed)" : tile.Name, selected, ImGuiSelectableFlags.SpanAllColumns, new Vector2(0, 44)))
                    {
                        SetSelectedId(id);
                    }
                    ImGui.PopStyleVar();
                    ImGui.PopID();
                }
            }

            ImGui.EndTable();
        }
    }

    private void DrawArtTilePreview(AssetTilePreview preview, AssetTileBrowserService browser)
    {
        if (!preview.IsValid)
        {
            ImGui.TextWrapped("Preview unavailable for the current selection.");
            return;
        }

        ImGui.Text(_artObjectMode ? "Static art preview" : "Land art preview");
        if (!_artObjectMode && preview.UsesTexmap)
        {
            ImGui.SameLine();
            ImGui.TextDisabled("texmap source");
        }

        var maxWidth = MathF.Max(0f, ImGui.GetContentRegionAvail().X);
        if (maxWidth < MinimumPreviewWidth)
        {
            ImGui.TextWrapped("Expand the window to restore the asset preview.");
            return;
        }

        var targetSize = new Vector2(MathF.Max(MinimumPreviewWidth, MathF.Min(maxWidth, 240f)), 240f);
        DrawPreviewImage(preview, targetSize, !_artObjectMode);

        ImGui.Spacing();
        var selectedId = _artObjectMode ? _selectedStaticId : _selectedLandId;
        var isDirty = browser.IsDirty(_artObjectMode, selectedId);
        var isMarkedForRemoval = browser.IsMarkedForRemoval(_artObjectMode, selectedId);
        if (isDirty)
        {
            ImGui.TextColored(new Vector4(0.92f, 0.66f, 0.22f, 1.0f), isMarkedForRemoval ? "Staged removal" : "Staged replacement");
            if (!isMarkedForRemoval)
            {
                var sourcePath = browser.GetReplacementSourcePath(_artObjectMode, selectedId);
                if (sourcePath.Length > 0)
                {
                    ImGui.TextWrapped(sourcePath);
                }
            }
        }

        if (ImGui.Button("Export Preview..."))
        {
            ExportSelectedPreview(browser, selectedId);
        }
        ImGui.SameLine();
        if (ImGui.Button(isDirty ? "Replace Staged Image..." : "Import Replacement..."))
        {
            ImportReplacement(browser, selectedId);
        }
        ImGui.SameLine();
        if (ImGui.Button(isMarkedForRemoval ? "Keep Asset" : "Stage Remove"))
        {
            ToggleRemoval(browser, selectedId, isMarkedForRemoval);
        }
        ImGui.SameLine();
        ImGui.BeginDisabled(!isDirty);
        if (ImGui.Button("Clear Staged"))
        {
            browser.ClearStagedChange(_artObjectMode, selectedId);
            _artActionStatus = $"Cleared staged art change for {selectedId.FormatId()}.";
        }
        ImGui.EndDisabled();

        ImGui.TextWrapped("Staged replacements update the Asset Workspace preview immediately and can now be written back into art.mul and artidx.mul with the save action above.");
        ImGui.Spacing();
        ImGui.Text($"Asset Id: {preview.AssetId.FormatId()}");
        ImGui.Text($"Real Index: {preview.RealIndex.FormatId()}");
        if (_artObjectMode)
        {
            ImGui.Text($"Height: {preview.Height}");
        }
        ImGui.TextWrapped(preview.Name.Length == 0 ? "(unnamed)" : preview.Name);
        ImGui.Separator();
        ImGui.TextDisabled("Flags");
        ImGui.TextWrapped(preview.Flags.Length == 0 ? "None" : preview.Flags);
    }

    private void DrawTextureList(AssetTextureBrowserService browser, List<ushort> ids)
    {
        if (ImGui.BeginTable("AssetTextureTable", 3))
        {
            var clipper = ImGui.ImGuiListClipper();
            ImGui.TableSetupColumn("Id", ImGuiTableColumnFlags.WidthFixed, ImGui.CalcTextSize(0xFFFF.FormatId()).X);
            ImGui.TableSetupColumn("Preview", ImGuiTableColumnFlags.WidthFixed, 44f);
            ImGui.TableSetupColumn("Size", ImGuiTableColumnFlags.WidthStretch);
            clipper.Begin(ids.Count);

            while (clipper.Step())
            {
                for (var i = clipper.DisplayStart; i < clipper.DisplayEnd; i++)
                {
                    var id = ids[i];
                    var preview = browser.GetPreview(id);

                    ImGui.PushID($"Texture{id}");
                    ImGui.TableNextRow(ImGuiTableRowFlags.None, 44f);
                    ImGui.TableNextColumn();
                    ImGui.Text(id.FormatId());

                    ImGui.TableNextColumn();
                    DrawTexturePreviewImage(preview, new Vector2(44, 44));

                    ImGui.TableNextColumn();
                    ImGui.PushStyleVar(ImGuiStyleVar.SelectableTextAlign, new Vector2(0.0f, 0.5f));
                    if (ImGui.Selectable($"{preview.PixelSize} x {preview.PixelSize}", id == _selectedTextureId, ImGuiSelectableFlags.SpanAllColumns, new Vector2(0, 44)))
                    {
                        _selectedTextureId = id;
                    }
                    ImGui.PopStyleVar();
                    ImGui.PopID();
                }
            }

            ImGui.EndTable();
        }
    }

    private void DrawTexturePreview(AssetTexturePreview preview, AssetTextureBrowserService browser)
    {
        if (!preview.IsValid)
        {
            ImGui.TextWrapped("Preview unavailable for the current selection.");
            return;
        }

        ImGui.Text("Texture preview");
        var maxWidth = MathF.Max(0f, ImGui.GetContentRegionAvail().X);
        if (maxWidth < MinimumPreviewWidth)
        {
            ImGui.TextWrapped("Expand the window to restore the texture preview.");
            return;
        }

        var targetSize = new Vector2(MathF.Max(MinimumPreviewWidth, MathF.Min(maxWidth, 240f)), 240f);
        DrawTexturePreviewImage(preview, targetSize);

        ImGui.Spacing();
        var isDirty = browser.IsDirty(_selectedTextureId);
        var isMarkedForRemoval = browser.IsMarkedForRemoval(_selectedTextureId);
        if (isDirty)
        {
            ImGui.TextColored(new Vector4(0.92f, 0.66f, 0.22f, 1.0f), isMarkedForRemoval ? "Staged removal" : "Staged replacement");
            if (!isMarkedForRemoval)
            {
                var sourcePath = browser.GetReplacementSourcePath(_selectedTextureId);
                if (sourcePath.Length > 0)
                {
                    ImGui.TextWrapped(sourcePath);
                }
            }
        }

        if (ImGui.Button("Export Preview...##Texture"))
        {
            ExportSelectedTexture(browser, _selectedTextureId);
        }
        ImGui.SameLine();
        if (ImGui.Button(isDirty ? "Replace Staged Image...##Texture" : "Import Replacement...##Texture"))
        {
            ImportTextureReplacement(browser, _selectedTextureId);
        }
        ImGui.SameLine();
        if (ImGui.Button(isMarkedForRemoval ? "Keep Texture" : "Stage Remove##Texture"))
        {
            ToggleTextureRemoval(browser, _selectedTextureId, isMarkedForRemoval);
        }
        ImGui.SameLine();
        ImGui.BeginDisabled(!isDirty);
        if (ImGui.Button("Clear Staged##Texture"))
        {
            browser.ClearStagedChange(_selectedTextureId);
            _textureActionStatus = $"Cleared staged texture change for {_selectedTextureId.FormatId()}.";
        }
        ImGui.EndDisabled();

        ImGui.TextWrapped("Texture replacements update the Asset Workspace preview immediately and can now be written back into texmaps.mul and texidx.mul with the save action above.");
        ImGui.Spacing();
        ImGui.Text($"Texture Id: {preview.TextureId.FormatId()}");
        ImGui.Text($"Resolution: {preview.PixelSize} x {preview.PixelSize}");
    }

    private void DrawGumpList(AssetGumpBrowserService browser, List<ushort> ids)
    {
        if (ImGui.BeginTable("AssetGumpTable", 3))
        {
            var clipper = ImGui.ImGuiListClipper();
            ImGui.TableSetupColumn("Id", ImGuiTableColumnFlags.WidthFixed, ImGui.CalcTextSize(0xFFFF.FormatId()).X);
            ImGui.TableSetupColumn("Preview", ImGuiTableColumnFlags.WidthFixed, 44f);
            ImGui.TableSetupColumn("Size", ImGuiTableColumnFlags.WidthStretch);
            clipper.Begin(ids.Count);

            while (clipper.Step())
            {
                for (var i = clipper.DisplayStart; i < clipper.DisplayEnd; i++)
                {
                    var id = ids[i];
                    var preview = browser.GetPreview(id);

                    ImGui.PushID($"Gump{id}");
                    ImGui.TableNextRow(ImGuiTableRowFlags.None, 44f);
                    ImGui.TableNextColumn();
                    ImGui.Text(id.FormatId());

                    ImGui.TableNextColumn();
                    DrawGumpPreviewImage(preview, new Vector2(44, 44));

                    ImGui.TableNextColumn();
                    ImGui.PushStyleVar(ImGuiStyleVar.SelectableTextAlign, new Vector2(0.0f, 0.5f));
                    if (ImGui.Selectable($"{preview.Width} x {preview.Height}", id == _selectedGumpId, ImGuiSelectableFlags.SpanAllColumns, new Vector2(0, 44)))
                    {
                        _selectedGumpId = id;
                    }
                    ImGui.PopStyleVar();
                    ImGui.PopID();
                }
            }

            ImGui.EndTable();
        }
    }

    private void DrawGumpPreview(AssetGumpPreview preview, AssetGumpBrowserService browser)
    {
        if (!preview.IsValid)
        {
            ImGui.TextWrapped("Preview unavailable for the current selection.");
            return;
        }

        ImGui.Text("Gump preview");
        var maxWidth = MathF.Max(0f, ImGui.GetContentRegionAvail().X);
        if (maxWidth < MinimumPreviewWidth)
        {
            ImGui.TextWrapped("Expand the window to restore the gump preview.");
            return;
        }

        var maxHeight = 280f;
        var scale = MathF.Min(MathF.Min(MathF.Min(maxWidth, 320f) / preview.Width, maxHeight / preview.Height), 1f);
        var targetSize = new Vector2(MathF.Max(MinimumPreviewWidth, preview.Width * scale), preview.Height * scale);
        DrawGumpPreviewImage(preview, targetSize);

        ImGui.Spacing();
        var isDirty = browser.IsDirty(_selectedGumpId);
        var isMarkedForRemoval = browser.IsMarkedForRemoval(_selectedGumpId);
        if (isDirty)
        {
            ImGui.TextColored(new Vector4(0.92f, 0.66f, 0.22f, 1.0f), isMarkedForRemoval ? "Staged removal" : "Staged replacement");
            if (!isMarkedForRemoval)
            {
                var sourcePath = browser.GetReplacementSourcePath(_selectedGumpId);
                if (sourcePath.Length > 0)
                {
                    ImGui.TextWrapped(sourcePath);
                }
            }
        }

        if (ImGui.Button("Export Preview...##Gump"))
        {
            ExportSelectedGump(browser, _selectedGumpId);
        }
        ImGui.SameLine();
        if (ImGui.Button(isDirty ? "Replace Staged Image...##Gump" : "Import Replacement...##Gump"))
        {
            ImportGumpReplacement(browser, _selectedGumpId);
        }
        ImGui.SameLine();
        if (ImGui.Button(isMarkedForRemoval ? "Keep Gump" : "Stage Remove##Gump"))
        {
            ToggleGumpRemoval(browser, _selectedGumpId, isMarkedForRemoval);
        }
        ImGui.SameLine();
        ImGui.BeginDisabled(!isDirty);
        if (ImGui.Button("Clear Staged##Gump"))
        {
            browser.ClearStagedChange(_selectedGumpId);
            _gumpActionStatus = $"Cleared staged gump change for {_selectedGumpId.FormatId()}.";
        }
        ImGui.EndDisabled();

        ImGui.TextWrapped("Gump replacements update the Asset Workspace preview immediately and can now be written back into gumpart.mul and gumpidx.mul with the save action above.");
        ImGui.Spacing();
        ImGui.Text($"Gump Id: {preview.GumpId.FormatId()}");
        ImGui.Text($"Resolution: {preview.Width} x {preview.Height}");
    }

    private void DrawAnimationsAndAnimDataBrowser(AssetWorkspaceService workspace, bool familyReady)
    {
        ImGui.TextDisabled("Implemented slices");
        ImGui.TextWrapped("This family now includes a native animation browser for body/action/direction playback preview plus the existing native animdata editor with frame-list editing, JSON import/export, and explicit save support.");

        if (!familyReady)
        {
            ImGui.TextWrapped("The Animations and AnimData browser stays disabled until the required files for this family are available.");
            return;
        }

        ImGuiEx.TwoWaySwitch("Animation Preview", "AnimData", ref _animationAnimDataMode, new Vector2(168, 20));
        ImGui.Spacing();

        if (!_animationAnimDataMode)
        {
            DrawAnimationBrowser(workspace);
            return;
        }

        EnsureAnimDataBrowserLoaded();
        EnsureArtBrowserLoaded();
        EnsureHueTileDataBrowserLoaded();

        var browser = AssetAnimDataBrowser;
        if (!browser.IsReady)
        {
            ImGui.TextWrapped(browser.StatusMessage);
            return;
        }

        if (_animDataActionStatus.Length > 0)
        {
            ImGui.TextWrapped(_animDataActionStatus);
        }

        var filterChanged = ImGui.InputText("Search##AssetAnimDataFilter", ref _animDataFilterText, 128);
        ImGui.SameLine();
        if (ImGui.Button("Reload Local AnimData"))
        {
            browser.EnsureLoaded(workspace.EffectiveRootPath);
            _loadedAnimDataEditId = ushort.MaxValue;
        }

        ImGui.SameLine();
        ImGui.BeginDisabled(browser.DirtyCount == 0);
        if (ImGui.Button("Save animdata.mul"))
        {
            SaveAnimDataChanges(browser);
        }
        ImGui.EndDisabled();

        ImGui.SameLine();
        if (ImGui.Button("Export Selected JSON..."))
        {
            ExportSelectedAnimData(browser);
        }

        ImGui.SameLine();
        if (ImGui.Button("Export All JSON..."))
        {
            ExportAllAnimData(browser);
        }

        ImGui.SameLine();
        if (ImGui.Button("Import JSON..."))
        {
            ImportAnimData(browser);
        }

        ImGui.Checkbox("Overwrite existing on import", ref _animDataImportOverwrite);
        ImGui.SameLine();
        ImGui.Checkbox("Erase before import", ref _animDataImportEraseExisting);

        ImGui.InputText("New Base Id##AssetAnimDataNewId", ref _newAnimDataIdText, 16);
        ImGui.SameLine();
        if (ImGui.Button("Add Entry"))
        {
            AddAnimDataEntry(browser);
        }

        if (filterChanged)
        {
            KeepSelectedAnimDataVisible(browser);
        }

        var ids = browser.GetFilteredIds(_animDataFilterText);
        if (ids.Count == 0)
        {
            ImGui.TextWrapped("No animdata entries match the current filter.");
            return;
        }

        var selectedId = GetSelectedAnimDataId(ids);
        LoadAnimDataEditBuffer(browser, selectedId);

        var availableWidth = MathF.Max(0f, ImGui.GetContentRegionAvail().X);
        if (availableWidth < MinimumSplitWidth)
        {
            if (ImGui.BeginChild("AnimDataList", new Vector2(0, 240f), ImGuiChildFlags.Borders))
            {
                DrawAnimDataList(browser, ids);
            }
            ImGui.EndChild();

            if (ImGui.BeginChild("AnimDataDetail", new Vector2(0, 0), ImGuiChildFlags.Borders))
            {
                DrawAnimDataDetail(browser, selectedId);
            }
            ImGui.EndChild();
            return;
        }

        var listWidth = MathF.Min(380f, MathF.Max(280f, availableWidth * 0.42f));
        if (ImGui.BeginChild("AnimDataList", new Vector2(listWidth, 0), ImGuiChildFlags.Borders | ImGuiChildFlags.ResizeX))
        {
            DrawAnimDataList(browser, ids);
        }
        ImGui.EndChild();

        ImGui.SameLine();
        if (ImGui.BeginChild("AnimDataDetail", new Vector2(0, 0), ImGuiChildFlags.Borders))
        {
            DrawAnimDataDetail(browser, selectedId);
        }
        ImGui.EndChild();
    }

        private void DrawAnimationBrowser(AssetWorkspaceService workspace)
        {
            EnsureAnimationBrowserLoaded();

            var browser = AssetAnimationBrowser;
            ImGui.TextWrapped(browser.StatusMessage);
            if (!browser.IsReady)
            {
                return;
            }

            var filterChanged = ImGui.InputText("Search##AssetAnimationFilter", ref _animationFilterText, 128);
            ImGui.SameLine();
            if (ImGui.Button("Reload Local Animations"))
            {
                browser.EnsureLoaded(CEDGame.GraphicsDevice, workspace.EffectiveRootPath);
            }

            ImGui.SameLine();
            ImGui.Checkbox("Animate preview##AssetAnimation", ref _animationAnimatePreview);

            if (filterChanged)
            {
                KeepSelectedAnimationVisible(browser);
            }

            var ids = browser.GetFilteredBodyIds(_animationFilterText);
            if (ids.Count == 0)
            {
                ImGui.TextWrapped("No animation bodies match the current filter.");
                return;
            }

            var selectedBodyId = GetSelectedAnimationBodyId(ids);
            var availableWidth = MathF.Max(0f, ImGui.GetContentRegionAvail().X);
            if (availableWidth < MinimumSplitWidth)
            {
                if (ImGui.BeginChild("AnimationBodyList", new Vector2(0, 240f), ImGuiChildFlags.Borders))
                {
                    DrawAnimationBodyList(browser, ids);
                }
                ImGui.EndChild();

                if (ImGui.BeginChild("AnimationDetail", new Vector2(0, 0), ImGuiChildFlags.Borders))
                {
                    DrawAnimationDetail(browser, selectedBodyId);
                }
                ImGui.EndChild();
                return;
            }

            var listWidth = MathF.Min(380f, MathF.Max(280f, availableWidth * 0.42f));
            if (ImGui.BeginChild("AnimationBodyList", new Vector2(listWidth, 0), ImGuiChildFlags.Borders | ImGuiChildFlags.ResizeX))
            {
                DrawAnimationBodyList(browser, ids);
            }
            ImGui.EndChild();

            ImGui.SameLine();
            if (ImGui.BeginChild("AnimationDetail", new Vector2(0, 0), ImGuiChildFlags.Borders))
            {
                DrawAnimationDetail(browser, selectedBodyId);
            }
            ImGui.EndChild();
        }

        private void DrawAnimationBodyList(AssetAnimationBrowserService browser, List<ushort> ids)
        {
            if (ImGui.BeginTable("AssetAnimationTable", 3))
            {
                var clipper = ImGui.ImGuiListClipper();
                ImGui.TableSetupColumn("Body", ImGuiTableColumnFlags.WidthFixed, ImGui.CalcTextSize(0xFFFF.FormatId()).X);
                ImGui.TableSetupColumn("Type", ImGuiTableColumnFlags.WidthFixed, 84f);
                ImGui.TableSetupColumn("Actions", ImGuiTableColumnFlags.WidthStretch);
                clipper.Begin(ids.Count);

                while (clipper.Step())
                {
                    for (var i = clipper.DisplayStart; i < clipper.DisplayEnd; i++)
                    {
                        var id = ids[i];
                        var entry = browser.GetBodyEntry(id);

                        ImGui.PushID($"AnimBody{id}");
                        ImGui.TableNextRow(ImGuiTableRowFlags.None, 24f);
                        ImGui.TableNextColumn();
                        ImGui.Text(id.FormatId());

                        ImGui.TableNextColumn();
                        ImGui.Text(entry.Type.ToString());

                        ImGui.TableNextColumn();
                        ImGui.PushStyleVar(ImGuiStyleVar.SelectableTextAlign, new Vector2(0.0f, 0.5f));
                        if (ImGui.Selectable($"{entry.ActionCount} actions", id == _selectedAnimationBodyId, ImGuiSelectableFlags.SpanAllColumns, new Vector2(0, 24f)))
                        {
                            _selectedAnimationBodyId = id;
                            _selectedAnimationActionId = 0;
                            _selectedAnimationFrameIndex = 0;
                        }
                        ImGui.PopStyleVar();
                        ImGui.PopID();
                    }
                }

                ImGui.EndTable();
            }
        }

        private void DrawAnimationDetail(AssetAnimationBrowserService browser, ushort bodyId)
        {
            var entry = browser.GetBodyEntry(bodyId);
            var actions = browser.GetActions(bodyId);
            if (actions.Count == 0)
            {
                ImGui.TextWrapped("No animation actions are available for the selected body.");
                return;
            }

            if (!actions.Any(action => action.ActionId == _selectedAnimationActionId))
            {
                _selectedAnimationActionId = actions[0].ActionId;
                _selectedAnimationFrameIndex = 0;
            }

            var currentActionLabel = GetAnimationActionDisplayName(actions, _selectedAnimationActionId);
            if (ImGui.BeginCombo("Action##AssetAnimation", currentActionLabel))
            {
                foreach (var action in actions)
                {
                    if (ImGui.Selectable(action.DisplayName, action.ActionId == _selectedAnimationActionId))
                    {
                        _selectedAnimationActionId = action.ActionId;
                        _selectedAnimationFrameIndex = 0;
                    }
                }

                ImGui.EndCombo();
            }

            var direction = (int)_selectedAnimationDirectionId;
            if (ImGui.SliderInt("Direction##AssetAnimation", ref direction, 0, 4))
            {
                _selectedAnimationDirectionId = (byte)direction;
                _selectedAnimationFrameIndex = 0;
            }

            ImGui.TextDisabled("ClassicUO stores five physical directions per action; mirrored facings are derived at runtime.");

            var preview = browser.GetPreview(bodyId, _selectedAnimationActionId, _selectedAnimationDirectionId);
            DrawAnimationPreview(preview);

            ImGui.Spacing();
            ImGui.Text($"Body: {entry.BodyId.FormatId()}");
            if (entry.ResolvedBodyId != entry.BodyId)
            {
                ImGui.Text($"Resolved Body: {entry.ResolvedBodyId.FormatId()}");
            }

            ImGui.Text($"Type: {entry.Type}");
            ImGui.Text($"Archive Group: {entry.StorageGroup}");
            ImGui.Text($"Archive File: anim{(entry.FileIndex == 0 ? string.Empty : (entry.FileIndex + 1).ToString(CultureInfo.InvariantCulture))}.mul");
            ImGui.Text($"Encoding: {(entry.UsesUop ? "UOP-backed" : "MUL-backed")}");
            ImGui.TextWrapped($"Flags: {entry.Flags}");
        }

        private void DrawAnimationPreview(AssetAnimationPreview preview)
        {
            if (!preview.IsValid)
            {
                ImGui.TextWrapped("Preview unavailable for the current body, action, and direction.");
                return;
            }

            var frameIndex = GetAnimationPreviewFrameIndex(preview);
            var frame = preview.Frames[frameIndex];
            var maxWidth = MathF.Max(MinimumPreviewWidth, MathF.Min(ImGui.GetContentRegionAvail().X, 320f));
            DrawAnimationPreviewFrame(preview, frame, new Vector2(maxWidth, 260f));

            ImGui.Spacing();
            if (!_animationAnimatePreview)
            {
                ImGui.SliderInt("Frame##AssetAnimation", ref _selectedAnimationFrameIndex, 0, Math.Max(0, preview.Frames.Count - 1));
            }

            ImGui.Text($"Preview Frame: {frameIndex + 1}/{preview.Frames.Count}");
            ImGui.Text($"Canvas: {preview.CanvasWidth} x {preview.CanvasHeight}");
        }

    private void DrawAnimDataList(AssetAnimDataService browser, List<ushort> ids)
    {
        if (ImGui.BeginTable("AssetAnimDataTable", 3))
        {
            var clipper = ImGui.ImGuiListClipper();
            ImGui.TableSetupColumn("Id", ImGuiTableColumnFlags.WidthFixed, ImGui.CalcTextSize(0xFFFF.FormatId()).X);
            ImGui.TableSetupColumn("Frames", ImGuiTableColumnFlags.WidthFixed, 52f);
            ImGui.TableSetupColumn("Name", ImGuiTableColumnFlags.WidthStretch);
            clipper.Begin(ids.Count);

            while (clipper.Step())
            {
                for (var i = clipper.DisplayStart; i < clipper.DisplayEnd; i++)
                {
                    var id = ids[i];
                    var entry = browser.GetEntry(id);
                    var name = GetAnimDataDisplayName(id);
                    var selected = id == _selectedAnimDataId;

                    ImGui.PushID($"AnimData{id}");
                    ImGui.TableNextRow(ImGuiTableRowFlags.None, 24f);
                    ImGui.TableNextColumn();
                    ImGui.Text(id.FormatId());

                    ImGui.TableNextColumn();
                    ImGui.Text(entry.FrameOffsets.Count.ToString(CultureInfo.InvariantCulture));

                    ImGui.TableNextColumn();
                    ImGui.PushStyleVar(ImGuiStyleVar.SelectableTextAlign, new Vector2(0.0f, 0.5f));
                    if (ImGui.Selectable(name, selected, ImGuiSelectableFlags.SpanAllColumns, new Vector2(0, 24f)))
                    {
                        _selectedAnimDataId = id;
                        _loadedAnimDataEditId = ushort.MaxValue;
                    }
                    ImGui.PopStyleVar();
                    ImGui.PopID();
                }
            }

            ImGui.EndTable();
        }
    }

    private void DrawAnimDataDetail(AssetAnimDataService browser, ushort baseId)
    {
        var entry = browser.GetEntry(baseId);
        DrawAnimDataPreview(entry);

        ImGui.Spacing();
        if (browser.IsDirty(baseId))
        {
            ImGui.TextColored(new Vector4(0.92f, 0.66f, 0.22f, 1.0f), "Staged animdata edits");
        }

        if (ImGui.Button("Apply AnimData Changes"))
        {
            ApplyAnimDataEdits(browser, baseId);
        }
        ImGui.SameLine();
        ImGui.BeginDisabled(!browser.IsDirty(baseId));
        if (ImGui.Button("Revert Entry##AnimData"))
        {
            browser.RevertEntry(baseId);
            LoadAnimDataEditBuffer(browser, baseId, true);
            _animDataActionStatus = $"Reverted animdata entry {baseId.FormatId()} to the loaded file state.";
        }
        ImGui.EndDisabled();
        ImGui.SameLine();
        if (ImGui.Button("Remove Entry##AnimData"))
        {
            RemoveAnimDataEntry(browser, baseId);
        }

        ImGui.Separator();
        ImGui.Text($"Base Id: {baseId.FormatId()}");
        ImGui.TextWrapped(GetAnimDataDisplayName(baseId));
        ImGui.Text($"Unknown: {_animDataUnknownDisplay}");
        ImGui.InputText("Frame Interval##AnimDataFrameInterval", ref _animDataFrameIntervalEdit, 16);
        ImGui.InputText("Frame Start##AnimDataFrameStart", ref _animDataFrameStartEdit, 16);
        ImGui.Checkbox("Animate preview", ref _animDataAnimatePreview);

        ImGui.Spacing();
        ImGui.TextDisabled("Frames");
        ImGui.InputText("Add Frame##AnimDataAddFrame", ref _animDataFrameAddText, 16);
        ImGui.SameLine();
        ImGui.Checkbox("Relative", ref _animDataFrameAddRelative);
        ImGui.SameLine();
        if (ImGui.Button("Add Frame##AnimData"))
        {
            AddAnimDataFrame(baseId);
        }
        ImGui.SameLine();
        ImGui.BeginDisabled(_selectedAnimDataFrameIndex < 0 || _selectedAnimDataFrameIndex >= _animDataFrameOffsetEdits.Count);
        if (ImGui.Button("Move Up##AnimDataFrame"))
        {
            MoveAnimDataFrame(-1);
        }
        ImGui.SameLine();
        if (ImGui.Button("Move Down##AnimDataFrame"))
        {
            MoveAnimDataFrame(1);
        }
        ImGui.SameLine();
        if (ImGui.Button("Remove Frame##AnimData"))
        {
            RemoveAnimDataFrame();
        }
        ImGui.EndDisabled();

        if (ImGui.BeginTable("AnimDataFramesTable", 3))
        {
            ImGui.TableSetupColumn("Index", ImGuiTableColumnFlags.WidthFixed, 44f);
            ImGui.TableSetupColumn("Offset", ImGuiTableColumnFlags.WidthFixed, 80f);
            ImGui.TableSetupColumn("Graphic", ImGuiTableColumnFlags.WidthStretch);
            for (var i = 0; i < _animDataFrameOffsetEdits.Count; i++)
            {
                var parsedOffset = TryParseSByteText(_animDataFrameOffsetEdits[i], out var offset);
                var absoluteId = parsedOffset ? baseId + offset : -1;

                ImGui.PushID($"AnimFrame{i}");
                ImGui.TableNextRow();
                ImGui.TableNextColumn();
                if (ImGui.Selectable(i.ToString(CultureInfo.InvariantCulture), _selectedAnimDataFrameIndex == i, ImGuiSelectableFlags.SpanAllColumns))
                {
                    _selectedAnimDataFrameIndex = i;
                }

                ImGui.TableNextColumn();
                ImGui.SetNextItemWidth(-1f);
                var offsetText = _animDataFrameOffsetEdits[i];
                if (ImGui.InputText("##Offset", ref offsetText, 8))
                {
                    _animDataFrameOffsetEdits[i] = offsetText;
                }

                ImGui.TableNextColumn();
                if (parsedOffset && absoluteId >= 0 && absoluteId <= ushort.MaxValue)
                {
                    ImGui.Text($"0x{absoluteId:X4} {GetAnimDataDisplayName((ushort)absoluteId)}");
                }
                else
                {
                    ImGui.TextDisabled("Invalid frame");
                }

                ImGui.PopID();
            }

            ImGui.EndTable();
        }
    }

    private void DrawAnimDataPreview(AssetAnimDataEntry entry)
    {
        if (entry.FrameOffsets.Count == 0)
        {
            ImGui.TextWrapped("No preview is available because the entry does not contain any frames.");
            return;
        }

        var frameIndex = GetPreviewFrameIndex(entry);
        var preview = TryGetAnimDataPreview(entry.BaseId, entry.FrameOffsets[frameIndex]);
        if (!preview.IsValid)
        {
            ImGui.TextWrapped("Preview unavailable for the current frame sequence. Static art preview requires art.mul and tiledata.mul to also be readable from this client path.");
            return;
        }

        var maxWidth = MathF.Max(0f, ImGui.GetContentRegionAvail().X);
        var targetSize = new Vector2(MathF.Max(MinimumPreviewWidth, MathF.Min(maxWidth, 240f)), 220f);
        DrawPreviewImage(preview, targetSize, false);
        ImGui.Text($"Preview Frame: {frameIndex + 1}/{entry.FrameOffsets.Count}");
        ImGui.Text($"Graphic: {(entry.BaseId + entry.FrameOffsets[frameIndex]).FormatId()}");
    }

    private void DrawHuesAndTileDataBrowser(AssetWorkspaceService workspace, bool familyReady)
    {
        ImGui.TextDisabled("Implemented slice");
        ImGui.TextWrapped("This family now includes a native hue editor and a native tiledata editor with explicit apply/save behavior, UO Fiddler-compatible text and CSV import/export, and optional asset previews for tiledata entries.");

        if (!familyReady)
        {
            ImGui.TextWrapped("The Hues and TileData browser stays disabled until the required files for this family are available.");
            return;
        }

        ImGuiEx.TwoWaySwitch("Hues", "TileData", ref _tileDataMode, new Vector2(112, 20));
        ImGui.Spacing();

        if (_tileDataMode)
        {
            DrawTileDataBrowser(workspace);
            return;
        }

        DrawHueBrowser(workspace);
    }

    private void DrawHueBrowser(AssetWorkspaceService workspace)
    {
        EnsureHueTileDataBrowserLoaded();
        var browser = AssetHueTileDataBrowser;
        if (!browser.IsReady)
        {
            ImGui.TextWrapped(browser.StatusMessage);
            return;
        }

        if (_hueActionStatus.Length > 0)
        {
            ImGui.TextWrapped(_hueActionStatus);
        }

        var filterChanged = ImGui.InputText("Search##AssetHueFilter", ref _hueFilterText, 128);
        ImGui.SameLine();
        if (ImGui.Button("Reload Local Hues"))
        {
            browser.EnsureLoaded(workspace.EffectiveRootPath);
            _loadedHueEditId = ushort.MaxValue;
        }

        ImGui.SameLine();
        ImGui.BeginDisabled(browser.DirtyCount == 0);
        if (ImGui.Button("Save hues.mul"))
        {
            SaveHueChanges(browser);
        }
        ImGui.EndDisabled();

        ImGui.SameLine();
        if (ImGui.Button("Export Hue Names..."))
        {
            ExportHueNames(browser);
        }

        if (filterChanged)
        {
            KeepSelectedHueVisible(browser);
        }

        var ids = browser.GetFilteredHueIds(_hueFilterText);
        if (ids.Count == 0)
        {
            ImGui.TextWrapped("No hue entries match the current filter.");
            return;
        }

        var selectedId = GetSelectedHueId(ids);
        LoadHueEditBuffer(browser, selectedId);

        var availableWidth = MathF.Max(0f, ImGui.GetContentRegionAvail().X);
        if (availableWidth < MinimumSplitWidth)
        {
            if (ImGui.BeginChild("HueList", new Vector2(0, 240f), ImGuiChildFlags.Borders))
            {
                DrawHueList(browser, ids);
            }
            ImGui.EndChild();

            if (ImGui.BeginChild("HueDetail", new Vector2(0, 0), ImGuiChildFlags.Borders))
            {
                DrawHueDetail(browser, selectedId);
            }
            ImGui.EndChild();
            return;
        }

        var listWidth = MathF.Min(360f, MathF.Max(280f, availableWidth * 0.42f));
        if (ImGui.BeginChild("HueList", new Vector2(listWidth, 0), ImGuiChildFlags.Borders | ImGuiChildFlags.ResizeX))
        {
            DrawHueList(browser, ids);
        }
        ImGui.EndChild();

        ImGui.SameLine();
        if (ImGui.BeginChild("HueDetail", new Vector2(0, 0), ImGuiChildFlags.Borders))
        {
            DrawHueDetail(browser, selectedId);
        }
        ImGui.EndChild();
    }

    private void DrawTileDataBrowser(AssetWorkspaceService workspace)
    {
        EnsureHueTileDataBrowserLoaded();
        EnsureArtBrowserLoaded();

        var browser = AssetHueTileDataBrowser;
        if (!browser.IsReady)
        {
            ImGui.TextWrapped(browser.StatusMessage);
            return;
        }

        if (_tiledataActionStatus.Length > 0)
        {
            ImGui.TextWrapped(_tiledataActionStatus);
        }

        ImGuiEx.TwoWaySwitch("Land", "Items", ref _tiledataLandMode, new Vector2(92, 20));
        ImGui.SameLine();
        var filterChanged = ImGui.InputText("Search##AssetTileDataFilter", ref _tiledataFilterText, 128);
        ImGui.SameLine();
        if (ImGui.Button("Reload Local TileData"))
        {
            browser.EnsureLoaded(workspace.EffectiveRootPath);
            _loadedTiledataEditId = ushort.MaxValue;
        }

        ImGui.SameLine();
        ImGui.BeginDisabled(browser.DirtyCount == 0);
        if (ImGui.Button("Save tiledata.mul"))
        {
            SaveTileDataChanges(browser);
        }
        ImGui.EndDisabled();

        ImGui.SameLine();
        if (ImGui.Button(_tiledataLandMode ? "Export Land CSV..." : "Export Item CSV..."))
        {
            ExportTileDataCsv(browser, _tiledataLandMode);
        }

        ImGui.SameLine();
        if (ImGui.Button(_tiledataLandMode ? "Import Land CSV..." : "Import Item CSV..."))
        {
            ImportTileDataCsv(browser, _tiledataLandMode);
        }

        DrawTileDataFilterEditor(browser);

        if (filterChanged)
        {
            KeepSelectedTileDataVisible(browser);
        }

        var ids = browser.GetFilteredTileIds(_tiledataLandMode, _tiledataFilterText, _tiledataFilterValue, _tiledataFilterInclusive, _tiledataFilterMatchAll);
        if (ids.Count == 0)
        {
            ImGui.TextWrapped("No tiledata entries match the current filter.");
            return;
        }

        var selectedId = GetSelectedTileDataId(ids);
        LoadTileDataEditBuffer(browser, _tiledataLandMode, selectedId);

        var availableWidth = MathF.Max(0f, ImGui.GetContentRegionAvail().X);
        if (availableWidth < MinimumSplitWidth)
        {
            if (ImGui.BeginChild("TileDataList", new Vector2(0, 240f), ImGuiChildFlags.Borders))
            {
                DrawTileDataList(browser, ids);
            }
            ImGui.EndChild();

            if (ImGui.BeginChild("TileDataDetail", new Vector2(0, 0), ImGuiChildFlags.Borders))
            {
                DrawTileDataDetail(browser, selectedId);
            }
            ImGui.EndChild();
            return;
        }

        var listWidth = MathF.Min(420f, MathF.Max(300f, availableWidth * 0.44f));
        if (ImGui.BeginChild("TileDataList", new Vector2(listWidth, 0), ImGuiChildFlags.Borders | ImGuiChildFlags.ResizeX))
        {
            DrawTileDataList(browser, ids);
        }
        ImGui.EndChild();

        ImGui.SameLine();
        if (ImGui.BeginChild("TileDataDetail", new Vector2(0, 0), ImGuiChildFlags.Borders))
        {
            DrawTileDataDetail(browser, selectedId);
        }
        ImGui.EndChild();
    }

    private void DrawHueList(AssetHueTileDataService browser, List<ushort> ids)
    {
        if (ImGui.BeginTable("AssetHueTable", 3))
        {
            var clipper = ImGui.ImGuiListClipper();
            ImGui.TableSetupColumn("Id", ImGuiTableColumnFlags.WidthFixed, ImGui.CalcTextSize(0xFFFF.FormatId()).X);
            ImGui.TableSetupColumn("Preview", ImGuiTableColumnFlags.WidthFixed, 124f);
            ImGui.TableSetupColumn("Name", ImGuiTableColumnFlags.WidthStretch);
            clipper.Begin(ids.Count);

            while (clipper.Step())
            {
                for (var i = clipper.DisplayStart; i < clipper.DisplayEnd; i++)
                {
                    var id = ids[i];
                    var entry = browser.GetHueEntry(id);

                    ImGui.PushID($"Hue{id}");
                    ImGui.TableNextRow(ImGuiTableRowFlags.None, 28f);
                    ImGui.TableNextColumn();
                    ImGui.Text(id.FormatId());

                    ImGui.TableNextColumn();
                    DrawHueGradient(entry.Colors, new Vector2(120f, 18f));

                    ImGui.TableNextColumn();
                    ImGui.PushStyleVar(ImGuiStyleVar.SelectableTextAlign, new Vector2(0.0f, 0.5f));
                    if (ImGui.Selectable(entry.Name.Length == 0 ? "(unnamed)" : entry.Name, id == _selectedHueId, ImGuiSelectableFlags.SpanAllColumns, new Vector2(0, 28f)))
                    {
                        _selectedHueId = id;
                        _loadedHueEditId = ushort.MaxValue;
                    }
                    ImGui.PopStyleVar();
                    ImGui.PopID();
                }
            }

            ImGui.EndTable();
        }
    }

    private void DrawHueDetail(AssetHueTileDataService browser, ushort hueId)
    {
        var entry = browser.GetHueEntry(hueId);
        DrawHueGradient(entry.Colors, new Vector2(MathF.Max(MinimumPreviewWidth, MathF.Min(ImGui.GetContentRegionAvail().X, 320f)), 36f));

        ImGui.Spacing();
        if (browser.IsHueDirty(hueId))
        {
            ImGui.TextColored(new Vector4(0.92f, 0.66f, 0.22f, 1.0f), "Staged hue edits");
        }

        if (ImGui.Button("Apply Hue Changes"))
        {
            ApplyHueEdits(browser, hueId);
        }
        ImGui.SameLine();
        ImGui.BeginDisabled(!browser.IsHueDirty(hueId));
        if (ImGui.Button("Revert Hue"))
        {
            browser.RevertHueEntry(hueId);
            LoadHueEditBuffer(browser, hueId, true);
            _hueActionStatus = $"Reverted hue {hueId.FormatId()} to the loaded file state.";
        }
        ImGui.EndDisabled();
        ImGui.SameLine();
        if (ImGui.Button("Export Hue..."))
        {
            ExportSelectedHue(browser, hueId);
        }
        ImGui.SameLine();
        if (ImGui.Button("Import Hue..."))
        {
            ImportSelectedHue(browser, hueId);
        }

        ImGui.Separator();
        ImGui.Text($"Hue Id: {hueId.FormatId()}");
        ImGui.InputText("Name##HueName", ref _hueNameEdit, 64);
        ImGui.InputText("Table Start##HueTableStart", ref _hueTableStartEdit, 16);
        ImGui.InputText("Table End##HueTableEnd", ref _hueTableEndEdit, 16);

        ImGui.Spacing();
        ImGui.TextDisabled("Colors (ushort, decimal or 0x-prefixed)");
        if (ImGui.BeginTable("HueColorTable", 4))
        {
            for (var i = 0; i < 4; i++)
            {
                ImGui.TableSetupColumn($"HueColorCol{i}", ImGuiTableColumnFlags.WidthStretch);
            }

            for (var i = 0; i < _hueColorEdits.Length; i++)
            {
                ImGui.TableNextColumn();
                ImGui.PushID($"HueColor{i}");
                ImGui.SetNextItemWidth(-1f);
                ImGui.InputText($"C{i:00}", ref _hueColorEdits[i], 16);
                ImGui.PopID();
            }

            ImGui.EndTable();
        }
    }

    private void DrawTileDataFilterEditor(AssetHueTileDataService browser)
    {
        ImGui.TextDisabled("Tiledata filter");
        if (ImGui.Button("Check All Flags"))
        {
            _tiledataFilterValue = browser.SupportedFlags.Aggregate(0ul, (current, flag) => current | (ulong)flag);
        }

        ImGui.SameLine();
        if (ImGui.Button("Clear Flags"))
        {
            _tiledataFilterValue = 0ul;
        }

        ImGui.SameLine();
        ImGui.Checkbox("Inclusive##TileDataFilterInclusive", ref _tiledataFilterInclusive);
        ImGui.SameLine();
        ImGui.Checkbox("Match All##TileDataFilterMatchAll", ref _tiledataFilterMatchAll);

        if (ImGui.BeginChild("TileDataFilterFlags", new Vector2(0, 126f), ImGuiChildFlags.Borders))
        {
            var flags = browser.SupportedFlags;
            var columns = Math.Max(1, (int)(ImGui.GetContentRegionAvail().X / 150f));
            if (ImGui.BeginTable("TileDataFilterTable", columns))
            {
                for (var i = 0; i < columns; i++)
                {
                    ImGui.TableSetupColumn($"TileDataFilterColumn{i}", ImGuiTableColumnFlags.WidthStretch);
                }

                for (var i = 0; i < flags.Count; i++)
                {
                    ImGui.TableNextColumn();
                    var flag = flags[i];
                    var enabled = (_tiledataFilterValue & (ulong)flag) != 0;
                    if (ImGui.Checkbox(flag.ToString(), ref enabled))
                    {
                        if (enabled)
                        {
                            _tiledataFilterValue |= (ulong)flag;
                        }
                        else
                        {
                            _tiledataFilterValue &= ~(ulong)flag;
                        }
                    }
                }

                ImGui.EndTable();
            }
        }
        ImGui.EndChild();
    }

    private void DrawTileDataList(AssetHueTileDataService browser, List<ushort> ids)
    {
        if (ImGui.BeginTable("AssetTileDataTable", 3))
        {
            var clipper = ImGui.ImGuiListClipper();
            ImGui.TableSetupColumn("Id", ImGuiTableColumnFlags.WidthFixed, ImGui.CalcTextSize(0xFFFF.FormatId()).X);
            ImGui.TableSetupColumn("Preview", ImGuiTableColumnFlags.WidthFixed, 44f);
            ImGui.TableSetupColumn("Name", ImGuiTableColumnFlags.WidthStretch);
            clipper.Begin(ids.Count);

            while (clipper.Step())
            {
                for (var i = clipper.DisplayStart; i < clipper.DisplayEnd; i++)
                {
                    var id = ids[i];
                    var preview = TryGetTileDataPreview(id);
                    var name = _tiledataLandMode ? browser.GetLandEntry(id).Name : browser.GetItemEntry(id).Name;

                    ImGui.PushID($"TileData{id}");
                    ImGui.TableNextRow(ImGuiTableRowFlags.None, 44f);
                    ImGui.TableNextColumn();
                    ImGui.Text(id.FormatId());

                    ImGui.TableNextColumn();
                    if (preview.IsValid)
                    {
                        DrawPreviewImage(preview, new Vector2(44, 44), _tiledataLandMode);
                    }
                    else
                    {
                        ImGui.TextDisabled("N/A");
                    }

                    ImGui.TableNextColumn();
                    ImGui.PushStyleVar(ImGuiStyleVar.SelectableTextAlign, new Vector2(0.0f, 0.5f));
                    if (ImGui.Selectable(name.Length == 0 ? "(unnamed)" : name, id == _selectedTiledataId, ImGuiSelectableFlags.SpanAllColumns, new Vector2(0, 44f)))
                    {
                        _selectedTiledataId = id;
                        _loadedTiledataEditId = ushort.MaxValue;
                    }
                    ImGui.PopStyleVar();
                    ImGui.PopID();
                }
            }

            ImGui.EndTable();
        }
    }

    private void DrawTileDataDetail(AssetHueTileDataService browser, ushort tileId)
    {
        var preview = TryGetTileDataPreview(tileId);
        if (preview.IsValid)
        {
            DrawPreviewImage(preview, new Vector2(MathF.Max(MinimumPreviewWidth, MathF.Min(ImGui.GetContentRegionAvail().X, 240f)), 220f), _tiledataLandMode);
            ImGui.Spacing();
        }

        var isDirty = _tiledataLandMode ? browser.IsLandDirty(tileId) : browser.IsItemDirty(tileId);
        if (isDirty)
        {
            ImGui.TextColored(new Vector4(0.92f, 0.66f, 0.22f, 1.0f), "Staged tiledata edits");
        }

        if (ImGui.Button("Apply TileData Changes"))
        {
            ApplyTileDataEdits(browser, tileId);
        }
        ImGui.SameLine();
        ImGui.BeginDisabled(!isDirty);
        if (ImGui.Button("Revert Entry"))
        {
            if (_tiledataLandMode)
            {
                browser.RevertLandEntry(tileId);
            }
            else
            {
                browser.RevertItemEntry(tileId);
            }

            LoadTileDataEditBuffer(browser, _tiledataLandMode, tileId, true);
            _tiledataActionStatus = $"Reverted tiledata entry {tileId.FormatId()} to the loaded file state.";
        }
        ImGui.EndDisabled();

        ImGui.Separator();
        ImGui.Text($"Tile Id: {tileId.FormatId()}");
        ImGui.InputText("Name##TileDataName", ref _tiledataNameEdit, 64);
        if (_tiledataLandMode)
        {
            ImGui.InputText("Texture Id##TileDataTextureId", ref _tiledataTextureIdEdit, 16);
        }
        else
        {
            ImGui.InputText("Animation##TileDataAnimation", ref _tiledataAnimationEdit, 16);
            ImGui.InputText("Weight##TileDataWeight", ref _tiledataWeightEdit, 16);
            ImGui.InputText("Quality/Layer##TileDataQuality", ref _tiledataQualityEdit, 16);
            ImGui.InputText("Quantity##TileDataQuantity", ref _tiledataQuantityEdit, 16);
            ImGui.InputText("Hue##TileDataHue", ref _tiledataHueEdit, 16);
            ImGui.InputText("Stack Offset##TileDataStackingOffset", ref _tiledataStackingOffsetEdit, 16);
            ImGui.InputText("Value##TileDataValue", ref _tiledataValueEdit, 16);
            ImGui.InputText("Height##TileDataHeight", ref _tiledataHeightEdit, 16);
            ImGui.InputText("Misc Data##TileDataMiscData", ref _tiledataMiscDataEdit, 16);
            ImGui.InputText("Unknown2##TileDataUnknown2", ref _tiledataUnknown2Edit, 16);
            ImGui.InputText("Unknown3##TileDataUnknown3", ref _tiledataUnknown3Edit, 16);
        }

        ImGui.Spacing();
        ImGui.TextDisabled("Flags");
        DrawTileDataFlagCheckboxes(browser);
    }

    private void DrawTileDataFlagCheckboxes(AssetHueTileDataService browser)
    {
        var flags = browser.SupportedFlags;
        var columns = Math.Max(1, (int)(ImGui.GetContentRegionAvail().X / 150f));
        if (ImGui.BeginTable("TileDataEditFlags", columns))
        {
            for (var i = 0; i < columns; i++)
            {
                ImGui.TableSetupColumn($"TileDataEditFlagColumn{i}", ImGuiTableColumnFlags.WidthStretch);
            }

            foreach (var flag in flags)
            {
                ImGui.TableNextColumn();
                var enabled = (_tiledataEditFlags & (ulong)flag) != 0;
                if (ImGui.Checkbox(flag.ToString(), ref enabled))
                {
                    if (enabled)
                    {
                        _tiledataEditFlags |= (ulong)flag;
                    }
                    else
                    {
                        _tiledataEditFlags &= ~(ulong)flag;
                    }
                }
            }

            ImGui.EndTable();
        }
    }

    private void DrawHueGradient(IReadOnlyList<ushort> colors, Vector2 size)
    {
        var drawList = ImGui.GetWindowDrawList();
        var start = ImGui.GetCursorScreenPos();
        var width = MathF.Max(1f, size.X / colors.Count);
        for (var i = 0; i < colors.Count; i++)
        {
            var left = new Vector2(start.X + (i * width), start.Y);
            var right = new Vector2(start.X + ((i + 1) * width), start.Y + size.Y);
            drawList.AddRectFilled(left, right, ToImGuiColor(colors[i]));
        }

        drawList.AddRect(start, new Vector2(start.X + size.X, start.Y + size.Y), ImGui.GetColorU32(new Vector4(0.18f, 0.18f, 0.18f, 1.0f)));
        ImGui.Dummy(size);
    }

    private void DrawPreviewImage(AssetTilePreview preview, Vector2 size, bool stretch)
    {
        if (!CEDGame.UIManager.DrawImage(preview.Texture, preview.Bounds, size, stretch))
        {
            ImGui.TextDisabled("Texture not found");
        }
    }

    private void DrawTexturePreviewImage(AssetTexturePreview preview, Vector2 size)
    {
        if (!CEDGame.UIManager.DrawImage(preview.Texture, preview.Bounds, size, true))
        {
            ImGui.TextDisabled("Texture not found");
        }
    }

    private void DrawGumpPreviewImage(AssetGumpPreview preview, Vector2 size)
    {
        if (!CEDGame.UIManager.DrawImage(preview.Texture, preview.Bounds, size, true))
        {
            ImGui.TextDisabled("Texture not found");
        }
    }

    private void ExportSelectedPreview(AssetTileBrowserService browser, ushort assetId)
    {
        var defaultName = $"{(_artObjectMode ? "static" : "land")}_{assetId:X4}.png";
        if (!TinyFileDialogs.TrySaveFile("Export asset preview", defaultName, ["*.png", "*.bmp", "*.jpg"], "Image files", out var outputPath))
        {
            return;
        }

        try
        {
            browser.ExportPreview(_artObjectMode, assetId, Config.Instance.PreferTexMaps, outputPath);
            _artActionStatus = $"Exported preview for {assetId.FormatId()} to {outputPath}.";
        }
        catch (Exception ex)
        {
            _artActionStatus = $"Export failed: {ex.Message}";
        }
    }

    private void ImportReplacement(AssetTileBrowserService browser, ushort assetId)
    {
        if (!TinyFileDialogs.TryOpenFile("Import replacement image", Environment.CurrentDirectory, ["*.png", "*.bmp", "*.jpg", "*.jpeg"], "Image files", false, out var imagePath))
        {
            return;
        }

        try
        {
            browser.StageReplacement(_artObjectMode, assetId, imagePath);
            _artActionStatus = $"Imported staged replacement for {assetId.FormatId()} from {imagePath}.";
        }
        catch (Exception ex)
        {
            _artActionStatus = $"Import failed: {ex.Message}";
        }
    }

    private void SaveArtChanges(AssetTileBrowserService browser)
    {
        try
        {
            browser.SaveStagedReplacements();
            _artActionStatus = $"Saved staged replacements into {Path.Combine(browser.LoadedRootPath, "art.mul")} and {Path.Combine(browser.LoadedRootPath, "artidx.mul")}. Existing files were backed up to .bak first.";
        }
        catch (Exception ex)
        {
            _artActionStatus = $"Save failed: {ex.Message}";
        }
    }

    private void ToggleRemoval(AssetTileBrowserService browser, ushort assetId, bool isMarkedForRemoval)
    {
        if (isMarkedForRemoval)
        {
            browser.ClearStagedChange(_artObjectMode, assetId);
            _artActionStatus = $"Restored {assetId.FormatId()} to keep its current art entry.";
            return;
        }

        browser.StageRemoval(_artObjectMode, assetId);
        _artActionStatus = $"Staged removal for {assetId.FormatId()}. Save art.mul to persist the deletion.";
    }

    private void ExportSelectedTexture(AssetTextureBrowserService browser, ushort textureId)
    {
        var defaultName = $"texture_{textureId:X4}.png";
        if (!TinyFileDialogs.TrySaveFile("Export texture preview", defaultName, ["*.png", "*.bmp", "*.jpg"], "Image files", out var outputPath))
        {
            return;
        }

        try
        {
            browser.ExportPreview(textureId, outputPath);
            _textureActionStatus = $"Exported texture preview for {textureId.FormatId()} to {outputPath}.";
        }
        catch (Exception ex)
        {
            _textureActionStatus = $"Texture export failed: {ex.Message}";
        }
    }

    private void ImportTextureReplacement(AssetTextureBrowserService browser, ushort textureId)
    {
        if (!TinyFileDialogs.TryOpenFile("Import texture replacement", Environment.CurrentDirectory, ["*.png", "*.bmp", "*.jpg", "*.jpeg"], "Image files", false, out var imagePath))
        {
            return;
        }

        try
        {
            browser.StageReplacement(textureId, imagePath);
            _textureActionStatus = $"Imported staged texture replacement for {textureId.FormatId()} from {imagePath}.";
        }
        catch (Exception ex)
        {
            _textureActionStatus = $"Texture import failed: {ex.Message}";
        }
    }

    private void SaveTextureChanges(AssetTextureBrowserService browser)
    {
        try
        {
            browser.SaveStagedChanges();
            _textureActionStatus = $"Saved staged texture changes into {Path.Combine(browser.LoadedRootPath, "texmaps.mul")} and {Path.Combine(browser.LoadedRootPath, "texidx.mul")}. Existing files were backed up to .bak first.";
        }
        catch (Exception ex)
        {
            _textureActionStatus = $"Texture save failed: {ex.Message}";
        }
    }

    private void ToggleTextureRemoval(AssetTextureBrowserService browser, ushort textureId, bool isMarkedForRemoval)
    {
        if (isMarkedForRemoval)
        {
            browser.ClearStagedChange(textureId);
            _textureActionStatus = $"Restored {textureId.FormatId()} to keep its current texture entry.";
            return;
        }

        browser.StageRemoval(textureId);
        _textureActionStatus = $"Staged removal for {textureId.FormatId()}. Save texmaps.mul to persist the deletion.";
    }

    private void ExportSelectedGump(AssetGumpBrowserService browser, ushort gumpId)
    {
        var defaultName = $"gump_{gumpId:X4}.png";
        if (!TinyFileDialogs.TrySaveFile("Export gump preview", defaultName, ["*.png", "*.bmp", "*.jpg"], "Image files", out var outputPath))
        {
            return;
        }

        try
        {
            browser.ExportPreview(gumpId, outputPath);
            _gumpActionStatus = $"Exported gump preview for {gumpId.FormatId()} to {outputPath}.";
        }
        catch (Exception ex)
        {
            _gumpActionStatus = $"Gump export failed: {ex.Message}";
        }
    }

    private void ImportGumpReplacement(AssetGumpBrowserService browser, ushort gumpId)
    {
        if (!TinyFileDialogs.TryOpenFile("Import gump replacement", Environment.CurrentDirectory, ["*.png", "*.bmp", "*.jpg", "*.jpeg"], "Image files", false, out var imagePath))
        {
            return;
        }

        try
        {
            browser.StageReplacement(gumpId, imagePath);
            _gumpActionStatus = $"Imported staged gump replacement for {gumpId.FormatId()} from {imagePath}.";
        }
        catch (Exception ex)
        {
            _gumpActionStatus = $"Gump import failed: {ex.Message}";
        }
    }

    private void SaveGumpChanges(AssetGumpBrowserService browser)
    {
        try
        {
            browser.SaveStagedChanges();
            _gumpActionStatus = $"Saved staged gump changes into {Path.Combine(browser.LoadedRootPath, "gumpart.mul")} and {Path.Combine(browser.LoadedRootPath, "gumpidx.mul")}. Existing files were backed up to .bak first.";
        }
        catch (Exception ex)
        {
            _gumpActionStatus = $"Gump save failed: {ex.Message}";
        }
    }

    private void ToggleGumpRemoval(AssetGumpBrowserService browser, ushort gumpId, bool isMarkedForRemoval)
    {
        if (isMarkedForRemoval)
        {
            browser.ClearStagedChange(gumpId);
            _gumpActionStatus = $"Restored {gumpId.FormatId()} to keep its current gump entry.";
            return;
        }

        browser.StageRemoval(gumpId);
        _gumpActionStatus = $"Staged removal for {gumpId.FormatId()}. Save gumpart.mul to persist the deletion.";
    }

    private void SaveHueChanges(AssetHueTileDataService browser)
    {
        try
        {
            browser.SaveHues();
            _hueActionStatus = $"Saved staged hue edits into {Path.Combine(browser.LoadedRootPath, "hues.mul")}. Existing files were backed up to .bak first.";
        }
        catch (Exception ex)
        {
            _hueActionStatus = $"Hue save failed: {ex.Message}";
        }
    }

    private void ExportHueNames(AssetHueTileDataService browser)
    {
        if (!TinyFileDialogs.TrySaveFile("Export hue names list", "Hue names list.txt", ["*.txt"], "Text files", out var outputPath))
        {
            return;
        }

        try
        {
            browser.ExportHueList(outputPath);
            _hueActionStatus = $"Exported the hue names list to {outputPath}.";
        }
        catch (Exception ex)
        {
            _hueActionStatus = $"Hue list export failed: {ex.Message}";
        }
    }

    private void ExportSelectedHue(AssetHueTileDataService browser, ushort hueId)
    {
        var defaultName = $"Hue {hueId}.txt";
        if (!TinyFileDialogs.TrySaveFile("Export hue", defaultName, ["*.txt"], "Text files", out var outputPath))
        {
            return;
        }

        try
        {
            browser.ExportHue(hueId, outputPath);
            _hueActionStatus = $"Exported hue {hueId.FormatId()} to {outputPath}.";
        }
        catch (Exception ex)
        {
            _hueActionStatus = $"Hue export failed: {ex.Message}";
        }
    }

    private void ImportSelectedHue(AssetHueTileDataService browser, ushort hueId)
    {
        if (!TinyFileDialogs.TryOpenFile("Import hue", Environment.CurrentDirectory, ["*.txt"], "Text files", false, out var inputPath))
        {
            return;
        }

        try
        {
            browser.ImportHue(hueId, inputPath);
            LoadHueEditBuffer(browser, hueId, true);
            _hueActionStatus = $"Imported hue data for {hueId.FormatId()} from {inputPath}.";
        }
        catch (Exception ex)
        {
            _hueActionStatus = $"Hue import failed: {ex.Message}";
        }
    }

    private void ApplyHueEdits(AssetHueTileDataService browser, ushort hueId)
    {
        if (!TryParseUShortText(_hueTableStartEdit, out var tableStart) || !TryParseUShortText(_hueTableEndEdit, out var tableEnd))
        {
            _hueActionStatus = "Hue apply failed: table bounds must be valid ushort values.";
            return;
        }

        var colors = new ushort[32];
        for (var i = 0; i < colors.Length; i++)
        {
            if (!TryParseUShortText(_hueColorEdits[i], out colors[i]))
            {
                _hueActionStatus = $"Hue apply failed: color {i} must be a valid ushort value.";
                return;
            }
        }

        try
        {
            browser.UpdateHueEntry(hueId, _hueNameEdit, tableStart, tableEnd, colors);
            LoadHueEditBuffer(browser, hueId, true);
            _hueActionStatus = $"Applied staged hue edits for {hueId.FormatId()}. Save hues.mul to persist them.";
        }
        catch (Exception ex)
        {
            _hueActionStatus = $"Hue apply failed: {ex.Message}";
        }
    }

    private void SaveTileDataChanges(AssetHueTileDataService browser)
    {
        try
        {
            browser.SaveTileData();
            _tiledataActionStatus = $"Saved staged tiledata edits into {Path.Combine(browser.LoadedRootPath, "tiledata.mul")}. Existing files were backed up to .bak first.";
        }
        catch (Exception ex)
        {
            _tiledataActionStatus = $"TileData save failed: {ex.Message}";
        }
    }

    private void ExportTileDataCsv(AssetHueTileDataService browser, bool landMode)
    {
        var defaultName = landMode ? "land_tiledata.csv" : "item_tiledata.csv";
        if (!TinyFileDialogs.TrySaveFile("Export tiledata CSV", defaultName, ["*.csv"], "CSV files", out var outputPath))
        {
            return;
        }

        try
        {
            if (landMode)
            {
                browser.ExportLandDataToCsv(outputPath);
            }
            else
            {
                browser.ExportItemDataToCsv(outputPath);
            }

            _tiledataActionStatus = $"Exported {(_tiledataLandMode ? "land" : "item")} tiledata CSV to {outputPath}.";
        }
        catch (Exception ex)
        {
            _tiledataActionStatus = $"TileData CSV export failed: {ex.Message}";
        }
    }

    private void ImportTileDataCsv(AssetHueTileDataService browser, bool landMode)
    {
        if (!TinyFileDialogs.TryOpenFile("Import tiledata CSV", Environment.CurrentDirectory, ["*.csv"], "CSV files", false, out var inputPath))
        {
            return;
        }

        try
        {
            if (landMode)
            {
                browser.ImportLandDataFromCsv(inputPath);
            }
            else
            {
                browser.ImportItemDataFromCsv(inputPath);
            }

            LoadTileDataEditBuffer(browser, landMode, _selectedTiledataId, true);
            _tiledataActionStatus = $"Imported {(_tiledataLandMode ? "land" : "item")} tiledata CSV from {inputPath}. Save tiledata.mul to persist the changes.";
        }
        catch (Exception ex)
        {
            _tiledataActionStatus = $"TileData CSV import failed: {ex.Message}";
        }
    }

    private void ApplyTileDataEdits(AssetHueTileDataService browser, ushort tileId)
    {
        try
        {
            if (_tiledataLandMode)
            {
                if (!TryParseUShortText(_tiledataTextureIdEdit, out var textureId))
                {
                    _tiledataActionStatus = "TileData apply failed: texture id must be a valid ushort value.";
                    return;
                }

                browser.UpdateLandEntry(tileId, _tiledataNameEdit, textureId, (TileFlag)_tiledataEditFlags);
            }
            else
            {
                if (!TryParseShortText(_tiledataAnimationEdit, out var animation) ||
                    !TryParseByteText(_tiledataWeightEdit, out var weight) ||
                    !TryParseByteText(_tiledataQualityEdit, out var quality) ||
                    !TryParseByteText(_tiledataQuantityEdit, out var quantity) ||
                    !TryParseByteText(_tiledataHueEdit, out var hue) ||
                    !TryParseByteText(_tiledataStackingOffsetEdit, out var stackingOffset) ||
                    !TryParseByteText(_tiledataValueEdit, out var value) ||
                    !TryParseByteText(_tiledataHeightEdit, out var height) ||
                    !TryParseShortText(_tiledataMiscDataEdit, out var miscData) ||
                    !TryParseByteText(_tiledataUnknown2Edit, out var unknown2) ||
                    !TryParseByteText(_tiledataUnknown3Edit, out var unknown3))
                {
                    _tiledataActionStatus = "TileData apply failed: one or more numeric fields are invalid.";
                    return;
                }

                browser.UpdateItemEntry(tileId, new AssetItemTileDataEntry(tileId, _tiledataNameEdit, animation, weight, quality, quantity, hue, stackingOffset, value, height, miscData, unknown2, unknown3, (TileFlag)_tiledataEditFlags));
            }

            LoadTileDataEditBuffer(browser, _tiledataLandMode, tileId, true);
            _tiledataActionStatus = $"Applied staged tiledata edits for {tileId.FormatId()}. Save tiledata.mul to persist them.";
        }
        catch (Exception ex)
        {
            _tiledataActionStatus = $"TileData apply failed: {ex.Message}";
        }
    }

    private void SaveAnimDataChanges(AssetAnimDataService browser)
    {
        try
        {
            browser.Save();
            _animDataActionStatus = $"Saved staged animdata edits into {Path.Combine(browser.LoadedRootPath, "animdata.mul")}. Existing files were backed up to .bak first.";
        }
        catch (Exception ex)
        {
            _animDataActionStatus = $"AnimData save failed: {ex.Message}";
        }
    }

    private void ExportSelectedAnimData(AssetAnimDataService browser)
    {
        var defaultName = $"animdata-{_selectedAnimDataId:X4}.json";
        if (!TinyFileDialogs.TrySaveFile("Export animdata JSON", defaultName, ["*.json"], "JSON files", out var outputPath))
        {
            return;
        }

        try
        {
            var count = browser.ExportJson(outputPath, [_selectedAnimDataId]);
            _animDataActionStatus = $"Exported {count} animdata entr{(count == 1 ? "y" : "ies")} to {outputPath}.";
        }
        catch (Exception ex)
        {
            _animDataActionStatus = $"AnimData export failed: {ex.Message}";
        }
    }

    private void ExportAllAnimData(AssetAnimDataService browser)
    {
        var defaultName = $"animdata-{DateTime.Now:yyyyMMddHHmm}.json";
        if (!TinyFileDialogs.TrySaveFile("Export all animdata JSON", defaultName, ["*.json"], "JSON files", out var outputPath))
        {
            return;
        }

        try
        {
            var count = browser.ExportJson(outputPath, browser.EntryIds);
            _animDataActionStatus = $"Exported {count} animdata entr{(count == 1 ? "y" : "ies")} to {outputPath}.";
        }
        catch (Exception ex)
        {
            _animDataActionStatus = $"AnimData export failed: {ex.Message}";
        }
    }

    private void ImportAnimData(AssetAnimDataService browser)
    {
        if (!TinyFileDialogs.TryOpenFile("Import animdata JSON", Environment.CurrentDirectory, ["*.json"], "JSON files", false, out var inputPath))
        {
            return;
        }

        try
        {
            var count = browser.ImportJson(inputPath, _animDataImportOverwrite, _animDataImportEraseExisting);
            KeepSelectedAnimDataVisible(browser);
            LoadAnimDataEditBuffer(browser, GetSelectedAnimDataId(browser.EntryIds.ToList()), true);
            _animDataActionStatus = $"Imported {count} animdata entr{(count == 1 ? "y" : "ies")} from {inputPath}. Save animdata.mul to persist the changes.";
        }
        catch (Exception ex)
        {
            _animDataActionStatus = $"AnimData import failed: {ex.Message}";
        }
    }

    private void AddAnimDataEntry(AssetAnimDataService browser)
    {
        if (!TryParseUShortText(_newAnimDataIdText, out var baseId))
        {
            _animDataActionStatus = "AnimData add failed: the new base id must be a valid ushort value.";
            return;
        }

        try
        {
            browser.AddEntry(baseId);
            _selectedAnimDataId = baseId;
            _loadedAnimDataEditId = ushort.MaxValue;
            _newAnimDataIdText = string.Empty;
            LoadAnimDataEditBuffer(browser, baseId, true);
            _animDataActionStatus = $"Added a new staged animdata entry at {baseId.FormatId()}. Save animdata.mul to persist it.";
        }
        catch (Exception ex)
        {
            _animDataActionStatus = $"AnimData add failed: {ex.Message}";
        }
    }

    private void RemoveAnimDataEntry(AssetAnimDataService browser, ushort baseId)
    {
        try
        {
            browser.RemoveEntry(baseId);
            _loadedAnimDataEditId = ushort.MaxValue;
            KeepSelectedAnimDataVisible(browser);
            _animDataActionStatus = $"Removed animdata entry {baseId.FormatId()} from the staged working set. Save animdata.mul to persist the deletion.";
        }
        catch (Exception ex)
        {
            _animDataActionStatus = $"AnimData remove failed: {ex.Message}";
        }
    }

    private void ApplyAnimDataEdits(AssetAnimDataService browser, ushort baseId)
    {
        if (!TryParseByteText(_animDataFrameIntervalEdit, out var frameInterval) || !TryParseByteText(_animDataFrameStartEdit, out var frameStart))
        {
            _animDataActionStatus = "AnimData apply failed: frame interval and frame start must be valid byte values.";
            return;
        }

        var offsets = new List<sbyte>(_animDataFrameOffsetEdits.Count);
        for (var i = 0; i < _animDataFrameOffsetEdits.Count; i++)
        {
            if (!TryParseSByteText(_animDataFrameOffsetEdits[i], out var offset))
            {
                _animDataActionStatus = $"AnimData apply failed: frame offset {i + 1} is invalid.";
                return;
            }

            offsets.Add(offset);
        }

        try
        {
            browser.UpdateEntry(baseId, frameInterval, frameStart, offsets);
            LoadAnimDataEditBuffer(browser, baseId, true);
            _animDataActionStatus = $"Applied staged animdata edits for {baseId.FormatId()}. Save animdata.mul to persist them.";
        }
        catch (Exception ex)
        {
            _animDataActionStatus = $"AnimData apply failed: {ex.Message}";
        }
    }

    private void AddAnimDataFrame(ushort baseId)
    {
        if (!TryParseIntegerText(_animDataFrameAddText, out var value))
        {
            _animDataActionStatus = "AnimData add frame failed: the frame value must be a valid number.";
            return;
        }

        var offset = _animDataFrameAddRelative ? value : value - baseId;
        if (offset < sbyte.MinValue || offset > sbyte.MaxValue)
        {
            _animDataActionStatus = "AnimData add frame failed: the frame offset must fit in the signed animdata range (-128 to 127).";
            return;
        }

        if (_animDataFrameOffsetEdits.Count >= 64)
        {
            _animDataActionStatus = "AnimData add frame failed: animdata entries cannot contain more than 64 frames.";
            return;
        }

        _animDataFrameOffsetEdits.Add(offset.ToString(CultureInfo.InvariantCulture));
        _selectedAnimDataFrameIndex = _animDataFrameOffsetEdits.Count - 1;
        _animDataFrameAddText = string.Empty;
    }

    private void MoveAnimDataFrame(int delta)
    {
        var sourceIndex = _selectedAnimDataFrameIndex;
        var targetIndex = sourceIndex + delta;
        if (sourceIndex < 0 || sourceIndex >= _animDataFrameOffsetEdits.Count || targetIndex < 0 || targetIndex >= _animDataFrameOffsetEdits.Count)
        {
            return;
        }

        (_animDataFrameOffsetEdits[sourceIndex], _animDataFrameOffsetEdits[targetIndex]) = (_animDataFrameOffsetEdits[targetIndex], _animDataFrameOffsetEdits[sourceIndex]);
        _selectedAnimDataFrameIndex = targetIndex;
    }

    private void RemoveAnimDataFrame()
    {
        if (_selectedAnimDataFrameIndex < 0 || _selectedAnimDataFrameIndex >= _animDataFrameOffsetEdits.Count)
        {
            return;
        }

        _animDataFrameOffsetEdits.RemoveAt(_selectedAnimDataFrameIndex);
        if (_animDataFrameOffsetEdits.Count == 0)
        {
            _selectedAnimDataFrameIndex = -1;
        }
        else
        {
            _selectedAnimDataFrameIndex = Math.Clamp(_selectedAnimDataFrameIndex, 0, _animDataFrameOffsetEdits.Count - 1);
        }
    }

    private void EnsureAnimDataBrowserLoaded()
    {
        var rootPath = AssetWorkspace.EffectiveRootPath;
        if (rootPath.Length == 0)
        {
            return;
        }

        AssetAnimDataBrowser.EnsureLoaded(rootPath);
    }

    private void EnsureAnimationBrowserLoaded()
    {
        if (CEDGame == null)
        {
            return;
        }

        var rootPath = AssetWorkspace.EffectiveRootPath;
        if (rootPath.Length == 0)
        {
            return;
        }

        AssetAnimationBrowser.EnsureLoaded(CEDGame.GraphicsDevice, rootPath);
    }

    private void EnsureHueTileDataBrowserLoaded()
    {
        var rootPath = AssetWorkspace.EffectiveRootPath;
        if (rootPath.Length == 0)
        {
            return;
        }

        AssetHueTileDataBrowser.EnsureLoaded(rootPath);
    }

    private void EnsureArtBrowserLoaded()
    {
        if (CEDGame == null)
        {
            return;
        }

        var rootPath = AssetWorkspace.EffectiveRootPath;
        if (rootPath.Length == 0)
        {
            return;
        }

        AssetTileBrowser.EnsureLoaded(CEDGame.GraphicsDevice, rootPath);
    }

    private void EnsureTextureBrowserLoaded()
    {
        if (CEDGame == null)
        {
            return;
        }

        var rootPath = AssetWorkspace.EffectiveRootPath;
        if (rootPath.Length == 0)
        {
            return;
        }

        AssetTextureBrowser.EnsureLoaded(CEDGame.GraphicsDevice, rootPath);
    }

    private void EnsureGumpBrowserLoaded()
    {
        if (CEDGame == null)
        {
            return;
        }

        var rootPath = AssetWorkspace.EffectiveRootPath;
        if (rootPath.Length == 0)
        {
            return;
        }

        AssetGumpBrowser.EnsureLoaded(CEDGame.GraphicsDevice, rootPath);
    }

    private void LoadAnimDataEditBuffer(AssetAnimDataService browser, ushort baseId, bool force = false)
    {
        if (!force && _loadedAnimDataEditId == baseId)
        {
            return;
        }

        var entry = browser.GetEntry(baseId);
        _selectedAnimDataId = baseId;
        _loadedAnimDataEditId = baseId;
        _animDataFrameIntervalEdit = entry.FrameInterval.ToString(CultureInfo.InvariantCulture);
        _animDataFrameStartEdit = entry.FrameStart.ToString(CultureInfo.InvariantCulture);
        _animDataUnknownDisplay = entry.Unknown;
        _animDataFrameOffsetEdits.Clear();
        foreach (var offset in entry.FrameOffsets)
        {
            _animDataFrameOffsetEdits.Add(offset.ToString(CultureInfo.InvariantCulture));
        }

        _selectedAnimDataFrameIndex = _animDataFrameOffsetEdits.Count > 0 ? 0 : -1;
    }

    private void LoadHueEditBuffer(AssetHueTileDataService browser, ushort hueId, bool force = false)
    {
        if (!force && _loadedHueEditId == hueId)
        {
            return;
        }

        var entry = browser.GetHueEntry(hueId);
        _selectedHueId = hueId;
        _loadedHueEditId = hueId;
        _hueNameEdit = entry.Name;
        _hueTableStartEdit = entry.TableStart.ToString(CultureInfo.InvariantCulture);
        _hueTableEndEdit = entry.TableEnd.ToString(CultureInfo.InvariantCulture);
        for (var i = 0; i < _hueColorEdits.Length; i++)
        {
            _hueColorEdits[i] = entry.Colors[i].ToString(CultureInfo.InvariantCulture);
        }
    }

    private void LoadTileDataEditBuffer(AssetHueTileDataService browser, bool landMode, ushort tileId, bool force = false)
    {
        if (!force && _loadedTiledataEditId == tileId && _loadedTiledataEditLandMode == landMode)
        {
            return;
        }

        _selectedTiledataId = tileId;
        _loadedTiledataEditId = tileId;
        _loadedTiledataEditLandMode = landMode;
        _tiledataLandMode = landMode;

        if (landMode)
        {
            var entry = browser.GetLandEntry(tileId);
            _tiledataNameEdit = entry.Name;
            _tiledataTextureIdEdit = entry.TextureId.ToString(CultureInfo.InvariantCulture);
            _tiledataEditFlags = (ulong)entry.Flags;
            return;
        }

        var item = browser.GetItemEntry(tileId);
        _tiledataNameEdit = item.Name;
        _tiledataAnimationEdit = item.Animation.ToString(CultureInfo.InvariantCulture);
        _tiledataWeightEdit = item.Weight.ToString(CultureInfo.InvariantCulture);
        _tiledataQualityEdit = item.Quality.ToString(CultureInfo.InvariantCulture);
        _tiledataQuantityEdit = item.Quantity.ToString(CultureInfo.InvariantCulture);
        _tiledataHueEdit = item.Hue.ToString(CultureInfo.InvariantCulture);
        _tiledataStackingOffsetEdit = item.StackingOffset.ToString(CultureInfo.InvariantCulture);
        _tiledataValueEdit = item.Value.ToString(CultureInfo.InvariantCulture);
        _tiledataHeightEdit = item.Height.ToString(CultureInfo.InvariantCulture);
        _tiledataMiscDataEdit = item.MiscData.ToString(CultureInfo.InvariantCulture);
        _tiledataUnknown2Edit = item.Unknown2.ToString(CultureInfo.InvariantCulture);
        _tiledataUnknown3Edit = item.Unknown3.ToString(CultureInfo.InvariantCulture);
        _tiledataEditFlags = (ulong)item.Flags;
    }

    private ushort GetSelectedId(List<ushort> ids)
    {
        var selectedId = _artObjectMode ? _selectedStaticId : _selectedLandId;
        if (ids.Contains(selectedId))
        {
            return selectedId;
        }

        selectedId = ids[0];
        SetSelectedId(selectedId);
        return selectedId;
    }

    private void SetSelectedId(ushort id)
    {
        if (_artObjectMode)
        {
            _selectedStaticId = id;
        }
        else
        {
            _selectedLandId = id;
        }
    }

    private void KeepSelectedIdVisible(AssetTileBrowserService browser)
    {
        var ids = browser.GetFilteredIds(_artObjectMode, _artFilterText);
        if (ids.Count == 0)
        {
            return;
        }

        var selectedId = _artObjectMode ? _selectedStaticId : _selectedLandId;
        if (!ids.Contains(selectedId))
        {
            SetSelectedId(ids[0]);
        }
    }

    private ushort GetSelectedTextureId(List<ushort> ids)
    {
        if (ids.Contains(_selectedTextureId))
        {
            return _selectedTextureId;
        }

        _selectedTextureId = ids[0];
        return _selectedTextureId;
    }

    private void KeepSelectedTextureVisible(AssetTextureBrowserService browser)
    {
        var ids = browser.GetFilteredIds(_textureFilterText);
        if (ids.Count == 0)
        {
            return;
        }

        if (!ids.Contains(_selectedTextureId))
        {
            _selectedTextureId = ids[0];
        }
    }

    private ushort GetSelectedGumpId(List<ushort> ids)
    {
        if (ids.Contains(_selectedGumpId))
        {
            return _selectedGumpId;
        }

        _selectedGumpId = ids[0];
        return _selectedGumpId;
    }

    private void KeepSelectedGumpVisible(AssetGumpBrowserService browser)
    {
        var ids = browser.GetFilteredIds(_gumpFilterText);
        if (ids.Count == 0)
        {
            return;
        }

        if (!ids.Contains(_selectedGumpId))
        {
            _selectedGumpId = ids[0];
        }
    }

    private ushort GetSelectedAnimDataId(List<ushort> ids)
    {
        if (ids.Contains(_selectedAnimDataId))
        {
            return _selectedAnimDataId;
        }

        _selectedAnimDataId = ids[0];
        return _selectedAnimDataId;
    }

    private ushort GetSelectedAnimationBodyId(List<ushort> ids)
    {
        if (ids.Contains(_selectedAnimationBodyId))
        {
            return _selectedAnimationBodyId;
        }

        _selectedAnimationBodyId = ids[0];
        return _selectedAnimationBodyId;
    }

    private void KeepSelectedAnimDataVisible(AssetAnimDataService browser)
    {
        var ids = browser.GetFilteredIds(_animDataFilterText);
        if (ids.Count == 0)
        {
            return;
        }

        if (!ids.Contains(_selectedAnimDataId))
        {
            _selectedAnimDataId = ids[0];
            _loadedAnimDataEditId = ushort.MaxValue;
        }
    }

    private void KeepSelectedAnimationVisible(AssetAnimationBrowserService browser)
    {
        var ids = browser.GetFilteredBodyIds(_animationFilterText);
        if (ids.Count == 0)
        {
            return;
        }

        if (!ids.Contains(_selectedAnimationBodyId))
        {
            _selectedAnimationBodyId = ids[0];
            _selectedAnimationActionId = 0;
            _selectedAnimationFrameIndex = 0;
        }
    }

    private ushort GetSelectedHueId(List<ushort> ids)
    {
        if (ids.Contains(_selectedHueId))
        {
            return _selectedHueId;
        }

        _selectedHueId = ids[0];
        return _selectedHueId;
    }

    private void KeepSelectedHueVisible(AssetHueTileDataService browser)
    {
        var ids = browser.GetFilteredHueIds(_hueFilterText);
        if (ids.Count == 0)
        {
            return;
        }

        if (!ids.Contains(_selectedHueId))
        {
            _selectedHueId = ids[0];
            _loadedHueEditId = ushort.MaxValue;
        }
    }

    private ushort GetSelectedTileDataId(List<ushort> ids)
    {
        if (ids.Contains(_selectedTiledataId))
        {
            return _selectedTiledataId;
        }

        _selectedTiledataId = ids[0];
        return _selectedTiledataId;
    }

    private void KeepSelectedTileDataVisible(AssetHueTileDataService browser)
    {
        var ids = browser.GetFilteredTileIds(_tiledataLandMode, _tiledataFilterText, _tiledataFilterValue, _tiledataFilterInclusive, _tiledataFilterMatchAll);
        if (ids.Count == 0)
        {
            return;
        }

        if (!ids.Contains(_selectedTiledataId))
        {
            _selectedTiledataId = ids[0];
            _loadedTiledataEditId = ushort.MaxValue;
        }
    }

    private AssetTilePreview TryGetTileDataPreview(ushort tileId)
    {
        if (!AssetTileBrowser.IsReady)
        {
            return AssetTilePreview.Invalid;
        }

        return AssetTileBrowser.GetPreview(!_tiledataLandMode, tileId, Config.Instance.PreferTexMaps);
    }

    private static bool TryParseByteText(string text, out byte value)
    {
        value = 0;
        return TryParseIntegerText(text, out var parsed) && byte.TryParse(parsed.ToString(CultureInfo.InvariantCulture), out value);
    }

    private static bool TryParseUShortText(string text, out ushort value)
    {
        value = 0;
        return TryParseIntegerText(text, out var parsed) && ushort.TryParse(parsed.ToString(CultureInfo.InvariantCulture), out value);
    }

    private static bool TryParseShortText(string text, out short value)
    {
        value = 0;
        return TryParseIntegerText(text, out var parsed) && short.TryParse(parsed.ToString(CultureInfo.InvariantCulture), out value);
    }

    private static bool TryParseSByteText(string text, out sbyte value)
    {
        value = 0;
        return TryParseIntegerText(text, out var parsed) && sbyte.TryParse(parsed.ToString(CultureInfo.InvariantCulture), out value);
    }

    private static bool TryParseIntegerText(string text, out int value)
    {
        text = text.Trim();
        if (text.StartsWith("0x", StringComparison.OrdinalIgnoreCase))
        {
            return int.TryParse(text[2..], NumberStyles.HexNumber, CultureInfo.InvariantCulture, out value);
        }

        return int.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out value);
    }

    private static uint ToImGuiColor(ushort hueColor)
    {
        var red = ((hueColor >> 10) & 0x1F) / 31f;
        var green = ((hueColor >> 5) & 0x1F) / 31f;
        var blue = (hueColor & 0x1F) / 31f;
        return ImGui.GetColorU32(new Vector4(red, green, blue, 1.0f));
    }

    private int GetPreviewFrameIndex(AssetAnimDataEntry entry)
    {
        if (!_animDataAnimatePreview || entry.FrameOffsets.Count <= 1)
        {
            return Math.Clamp(_selectedAnimDataFrameIndex, 0, Math.Max(0, entry.FrameOffsets.Count - 1));
        }

        var delayMs = entry.FrameInterval > 0 ? (entry.FrameInterval * 100) + 1 : 100;
        var tick = (int)(ImGui.GetTime() * 1000d / delayMs);
        return tick % entry.FrameOffsets.Count;
    }

    private int GetAnimationPreviewFrameIndex(AssetAnimationPreview preview)
    {
        if (!_animationAnimatePreview || preview.Frames.Count <= 1)
        {
            return Math.Clamp(_selectedAnimationFrameIndex, 0, Math.Max(0, preview.Frames.Count - 1));
        }

        var tick = (int)(ImGui.GetTime() * 10d);
        return tick % preview.Frames.Count;
    }

    private unsafe void DrawAnimationPreviewFrame(AssetAnimationPreview preview, AssetAnimationFramePreview frame, Vector2 size)
    {
        var safeSize = new Vector2(MathF.Max(0f, size.X), MathF.Max(0f, size.Y));
        if (safeSize.X <= 0 || safeSize.Y <= 0)
        {
            ImGui.Dummy(safeSize);
            return;
        }

        var drawList = ImGui.GetWindowDrawList();
        var topLeft = ImGui.GetCursorScreenPos();
        var bottomRight = new Vector2(topLeft.X + safeSize.X, topLeft.Y + safeSize.Y);
        drawList.AddRectFilled(topLeft, bottomRight, ImGui.GetColorU32(new Vector4(0.06f, 0.06f, 0.07f, 1.0f)));
        drawList.AddRect(topLeft, bottomRight, ImGui.GetColorU32(new Vector4(0.18f, 0.18f, 0.18f, 1.0f)));

        if (frame.IsValid)
        {
            var scale = MathF.Min(safeSize.X / preview.CanvasWidth, safeSize.Y / preview.CanvasHeight);
            var canvasSize = new Vector2(preview.CanvasWidth * scale, preview.CanvasHeight * scale);
            var canvasOrigin = new Vector2(
                topLeft.X + ((safeSize.X - canvasSize.X) * 0.5f),
                topLeft.Y + ((safeSize.Y - canvasSize.Y) * 0.5f));

            var frameTopLeft = new Vector2(
                canvasOrigin.X + (frame.OffsetX * scale),
                canvasOrigin.Y + (frame.OffsetY * scale));
            var frameBottomRight = new Vector2(
                frameTopLeft.X + (frame.Bounds.Width * scale),
                frameTopLeft.Y + (frame.Bounds.Height * scale));

            var texture = frame.Texture!;
            var texPtr = CEDGame.UIManager._uiRenderer.BindTexture(texture);
            var uv0 = new Vector2(0f, 0f);
            var uv1 = new Vector2(1f, 1f);
            drawList.AddImage(new ImTextureRef(null, texPtr), frameTopLeft, frameBottomRight, uv0, uv1, ImGui.GetColorU32(new Vector4(1f, 1f, 1f, 1f)));
        }

        ImGui.Dummy(safeSize);
    }

    private AssetTilePreview TryGetAnimDataPreview(ushort baseId, sbyte offset)
    {
        if (!AssetTileBrowser.IsReady)
        {
            return AssetTilePreview.Invalid;
        }

        var absoluteId = baseId + offset;
        if (absoluteId < 0 || absoluteId > ushort.MaxValue)
        {
            return AssetTilePreview.Invalid;
        }

        return AssetTileBrowser.GetPreview(true, (ushort)absoluteId, false);
    }

    private string GetAnimDataDisplayName(ushort id)
    {
        if (AssetHueTileDataBrowser.IsReady && id < AssetHueTileDataBrowser.ItemIds.Count)
        {
            var entry = AssetHueTileDataBrowser.GetItemEntry(id);
            if (!string.IsNullOrWhiteSpace(entry.Name))
            {
                return entry.Name;
            }
        }

        return $"0x{id:X4}";
    }

    private static string GetAnimationActionDisplayName(IReadOnlyList<AssetAnimationActionEntry> actions, byte actionId)
    {
        foreach (var action in actions)
        {
            if (action.ActionId == actionId)
            {
                return action.DisplayName;
            }
        }

        return $"Action {actionId}";
    }

    private void SyncPathInput()
    {
        var workspace = AssetWorkspace;
        _pathInput = workspace.ConfiguredRootPath.Length > 0
            ? workspace.ConfiguredRootPath
            : workspace.EffectiveRootPath;
    }

    private void SyncSelectedIndex(AssetWorkspaceService workspace)
    {
        if (_selectedFamilyIndex < 0 || _selectedFamilyIndex >= workspace.Families.Count)
        {
            _selectedFamilyIndex = 0;
        }
    }

    private static string GetImplementationNotes(AssetWorkspaceFamilyKind kind)
    {
        return kind switch
        {
            AssetWorkspaceFamilyKind.ArtAndLandTiles => "This family will anchor the first real editors: searchable asset browsing, image preview, replace/import/export flows, and staged save support.",
            AssetWorkspaceFamilyKind.GumpsAndTextures => "This family will reuse the image-asset pipeline while adding gump-specific scaling and texture-map preview behavior.",
            AssetWorkspaceFamilyKind.AnimationsAndAnimData => "This family will add playback, direction and action selection, frame metadata editing, and JSON-compatible animdata workflows.",
            AssetWorkspaceFamilyKind.HuesAndTileData => "This family will introduce native palette editing, tile flag inspection, metadata filters, and round-trip save validation.",
            AssetWorkspaceFamilyKind.MapsStaticsAndReplacement => "This family will bridge the asset workspace into centeredsharp tools and large-scale operations for apply-oriented world mutations.",
            AssetWorkspaceFamilyKind.Multis => "This family will add a centeredsharp-native multi browser and editor with import/export coverage instead of reusing the WinForms plugin UI.",
            _ => "This family is tracked for the centeredsharp-native asset workspace.",
        };
    }
}