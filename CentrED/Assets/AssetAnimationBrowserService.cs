using ClassicUO.Assets;
using ClassicUO.IO;
using ClassicUO.Utility;
using Microsoft.Xna.Framework.Graphics;
using Rectangle = System.Drawing.Rectangle;

namespace CentrED.Assets;

/// <summary>
/// One selectable animation action for a resolved body entry.
/// </summary>
public readonly record struct AssetAnimationActionEntry(byte ActionId, string DisplayName);

/// <summary>
/// Summary metadata for one browsable animation body.
/// </summary>
public readonly record struct AssetAnimationBodyEntry(
    ushort BodyId,
    ushort ResolvedBodyId,
    AnimationGroupsType Type,
    AnimationGroups StorageGroup,
    AnimationFlags Flags,
    int FileIndex,
    bool UsesUop,
    int ActionCount)
{
    /// <summary>
    /// Sentinel returned when a body could not be resolved.
    /// </summary>
    public static AssetAnimationBodyEntry Invalid => new(0, 0, AnimationGroupsType.Unknown, AnimationGroups.None, AnimationFlags.None, -1, false, 0);

    /// <summary>
    /// Gets whether the body metadata is usable for preview requests.
    /// </summary>
    public bool IsValid => ActionCount > 0;
}

/// <summary>
/// One decoded animation frame texture aligned within a shared preview canvas.
/// </summary>
public readonly record struct AssetAnimationFramePreview(Texture2D? Texture, Rectangle Bounds, int OffsetX, int OffsetY)
{
    /// <summary>
    /// Sentinel returned for empty or missing frames.
    /// </summary>
    public static AssetAnimationFramePreview Invalid => new(null, default, 0, 0);

    /// <summary>
    /// Gets whether the frame contains a texture and non-empty bounds.
    /// </summary>
    public bool IsValid => Texture != null && Bounds.Width > 0 && Bounds.Height > 0;
}

/// <summary>
/// Fully decoded preview payload for one body/action/direction animation query.
/// </summary>
public readonly record struct AssetAnimationPreview(
    ushort BodyId,
    ushort ResolvedBodyId,
    AnimationGroupsType Type,
    AnimationGroups StorageGroup,
    AnimationFlags Flags,
    byte ActionId,
    byte DirectionId,
    bool UsesUop,
    IReadOnlyList<AssetAnimationFramePreview> Frames,
    int CanvasWidth,
    int CanvasHeight)
{
    /// <summary>
    /// Sentinel returned when no preview could be built.
    /// </summary>
    public static AssetAnimationPreview Invalid => new(0, 0, AnimationGroupsType.Unknown, AnimationGroups.None, AnimationFlags.None, 0, 0, false, [], 0, 0);

    /// <summary>
    /// Gets whether the preview contains drawable frame data.
    /// </summary>
    public bool IsValid => Frames.Count > 0 && CanvasWidth > 0 && CanvasHeight > 0;
}

/// <summary>
/// Loads and decodes local Ultima animation assets for browse-and-preview workflows.
/// </summary>
public sealed class AssetAnimationBrowserService
{
    private const int MaxBrowsedBodyId = 4096;

    private sealed class BodyState
    {
        public required AssetAnimationBodyEntry Entry { get; init; }
        public required AnimationsLoader.AnimationDirection[] Indices { get; init; }
        public required AssetAnimationActionEntry[] Actions { get; init; }
    }

    private UOFileManager? _uoFileManager;
    private AnimationsLoader? _animations;
    private GraphicsDevice? _graphicsDevice;
    private readonly SortedDictionary<ushort, BodyState> _bodyStates = [];
    private readonly List<ushort> _bodyIds = [];
    private readonly Dictionary<(ushort BodyId, byte ActionId, byte DirectionId), AssetAnimationPreview> _previewCache = new();

    /// <summary>
    /// Gets the root path currently loaded into the browser service.
    /// </summary>
    public string LoadedRootPath { get; private set; } = string.Empty;

    /// <summary>
    /// Gets whether animation data is currently loaded and ready.
    /// </summary>
    public bool IsReady { get; private set; }

    /// <summary>
    /// Gets the last service status message.
    /// </summary>
    public string StatusMessage { get; private set; } = "Choose a Ultima data directory to start browsing animations.";

    /// <summary>
    /// Gets the browsable body ids discovered in the loaded client directory.
    /// </summary>
    public IReadOnlyList<ushort> BodyIds => _bodyIds;

    /// <summary>
    /// Ensures the browser service is loaded for the provided client directory.
    /// </summary>
    public void EnsureLoaded(GraphicsDevice graphicsDevice, string rootPath)
    {
        if (IsReady && string.Equals(LoadedRootPath, rootPath, StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        ResetState();

        if (string.IsNullOrWhiteSpace(rootPath) || !Directory.Exists(rootPath))
        {
            StatusMessage = "The selected Ultima data directory does not exist.";
            return;
        }

        var hasAnimData = File.Exists(Path.Combine(rootPath, "animdata.mul"));
        var hasAnimationArchive = Enumerable.Range(0, 5)
            .Select(index => index == 0 ? "anim.mul" : $"anim{index + 1}.mul")
            .Any(fileName => File.Exists(Path.Combine(rootPath, fileName)));

        if (!hasAnimData || !hasAnimationArchive)
        {
            StatusMessage = "animdata.mul plus at least one anim*.mul archive are required before animations can be browsed.";
            return;
        }

        try
        {
            var clientVersion = DetermineClientVersion(rootPath);
            var fileManager = new UOFileManager(clientVersion, rootPath);
            fileManager.Animations.Load();

            _uoFileManager = fileManager;
            _animations = fileManager.Animations;
            _graphicsDevice = graphicsDevice;

            PopulateBodyStates();

            LoadedRootPath = AssetWorkspaceService.NormalizePath(rootPath);
            IsReady = true;
            StatusMessage = $"Loaded {_bodyIds.Count} browsable animation bod{(_bodyIds.Count == 1 ? "y" : "ies")} from {LoadedRootPath}.";
        }
        catch (Exception ex)
        {
            ResetState();
            StatusMessage = $"Failed to load local animations: {ex.Message}";
        }
    }

    /// <summary>
    /// Filters browsable body ids by numeric text matching.
    /// </summary>
    public List<ushort> GetFilteredBodyIds(string filterText)
    {
        var results = new List<ushort>();
        if (!IsReady)
        {
            return results;
        }

        if (string.IsNullOrWhiteSpace(filterText))
        {
            results.AddRange(_bodyIds);
            return results;
        }

        foreach (var id in _bodyIds)
        {
            if (id.ToString().Contains(filterText, StringComparison.InvariantCultureIgnoreCase) ||
                $"0x{id:X4}".Contains(filterText, StringComparison.InvariantCultureIgnoreCase))
            {
                results.Add(id);
            }
        }

        return results;
    }

    /// <summary>
    /// Gets summary metadata for one browsable body id.
    /// </summary>
    public AssetAnimationBodyEntry GetBodyEntry(ushort bodyId)
    {
        return TryGetBodyState(bodyId, out var state) ? state.Entry : AssetAnimationBodyEntry.Invalid;
    }

    /// <summary>
    /// Gets the valid actions currently available for one browsable body.
    /// </summary>
    public IReadOnlyList<AssetAnimationActionEntry> GetActions(ushort bodyId)
    {
        return TryGetBodyState(bodyId, out var state) ? state.Actions : [];
    }

    /// <summary>
    /// Decodes one body/action/direction animation into a preview payload.
    /// </summary>
    public AssetAnimationPreview GetPreview(ushort bodyId, byte actionId, byte directionId)
    {
        if (!TryGetBodyState(bodyId, out var state) || _animations == null || _graphicsDevice == null)
        {
            return AssetAnimationPreview.Invalid;
        }

        if (directionId >= AnimationsLoader.MAX_DIRECTIONS || !state.Actions.Any(action => action.ActionId == actionId))
        {
            return AssetAnimationPreview.Invalid;
        }

        var cacheKey = (bodyId, actionId, directionId);
        if (_previewCache.TryGetValue(cacheKey, out var cachedPreview))
        {
            return cachedPreview;
        }

        var frames = state.Entry.UsesUop
            ? _animations.ReadUOPAnimationFrames(bodyId, actionId, directionId, state.Entry.Type, state.Entry.FileIndex, state.Indices[actionId])
            : _animations.ReadMULAnimationFrames(state.Entry.FileIndex, state.Indices[(actionId * AnimationsLoader.MAX_DIRECTIONS) + directionId]);

        if (frames.IsEmpty)
        {
            return AssetAnimationPreview.Invalid;
        }

        var preview = BuildPreview(state.Entry, actionId, directionId, frames);
        if (preview.IsValid)
        {
            _previewCache[cacheKey] = preview;
        }

        return preview;
    }

    private void PopulateBodyStates()
    {
        _bodyStates.Clear();
        _bodyIds.Clear();

        for (var bodyId = 0; bodyId < MaxBrowsedBodyId; bodyId++)
        {
            if (TryBuildBodyState((ushort)bodyId, out var state))
            {
                _bodyStates[(ushort)bodyId] = state;
                _bodyIds.Add((ushort)bodyId);
            }
        }
    }

    private bool TryBuildBodyState(ushort bodyId, out BodyState state)
    {
        state = null!;
        if (_uoFileManager == null || _animations == null)
        {
            return false;
        }

        ushort resolvedHue = 0;
        var resolvedBodyId = bodyId;
        _animations.ReplaceBody(ref resolvedBodyId, ref resolvedHue);

        var hue = (ushort)0;
        var flags = AnimationFlags.None;
        var indices = _animations.GetIndices(_uoFileManager.Version, bodyId, ref hue, ref flags, out var fileIndex, out var animType);
        if (indices.IsEmpty)
        {
            return false;
        }

        var clonedIndices = indices.ToArray();
        var usesUop = (flags & AnimationFlags.UseUopAnimation) != 0;
        var actionCount = usesUop ? clonedIndices.Length : clonedIndices.Length / AnimationsLoader.MAX_DIRECTIONS;
        if (actionCount <= 0)
        {
            return false;
        }

        var storageGroup = ResolveStorageGroup(animType, flags, actionCount);
        var actions = BuildActions(storageGroup, clonedIndices, usesUop);
        if (actions.Length == 0)
        {
            return false;
        }

        state = new BodyState
        {
            Entry = new AssetAnimationBodyEntry(
                bodyId,
                resolvedBodyId,
                animType,
                storageGroup,
                flags,
                fileIndex,
                usesUop,
                actions.Length),
            Indices = clonedIndices,
            Actions = actions,
        };

        return true;
    }

    private AssetAnimationPreview BuildPreview(
        AssetAnimationBodyEntry entry,
        byte actionId,
        byte directionId,
        ReadOnlySpan<AnimationsLoader.FrameInfo> frames)
    {
        var left = 0;
        var top = 0;
        var right = 0;
        var bottom = 0;

        for (var i = 0; i < frames.Length; i++)
        {
            ref readonly var frame = ref frames[i];
            if (!IsFrameDrawable(frame))
            {
                continue;
            }

            left = Math.Max(left, frame.CenterX);
            top = Math.Max(top, frame.CenterY);
            right = Math.Max(right, frame.Width - frame.CenterX);
            bottom = Math.Max(bottom, frame.Height - frame.CenterY);
        }

        var canvasWidth = left + right;
        var canvasHeight = top + bottom;
        if (canvasWidth <= 0 || canvasHeight <= 0)
        {
            return AssetAnimationPreview.Invalid;
        }

        var framePreviews = new AssetAnimationFramePreview[frames.Length];
        for (var i = 0; i < frames.Length; i++)
        {
            ref readonly var frame = ref frames[i];
            if (!IsFrameDrawable(frame))
            {
                framePreviews[i] = AssetAnimationFramePreview.Invalid;
                continue;
            }

            var texture = new Texture2D(_graphicsDevice!, frame.Width, frame.Height, false, SurfaceFormat.Color);
            texture.SetData(frame.Pixels);
            framePreviews[i] = new AssetAnimationFramePreview(
                texture,
                new Rectangle(0, 0, frame.Width, frame.Height),
                left - frame.CenterX,
                top - frame.CenterY);
        }

        return new AssetAnimationPreview(
            entry.BodyId,
            entry.ResolvedBodyId,
            entry.Type,
            entry.StorageGroup,
            entry.Flags,
            actionId,
            directionId,
            entry.UsesUop,
            framePreviews,
            canvasWidth,
            canvasHeight);
    }

    private bool TryGetBodyState(ushort bodyId, out BodyState state)
    {
        if (_bodyStates.TryGetValue(bodyId, out state!))
        {
            return true;
        }

        if (!IsReady || !TryBuildBodyState(bodyId, out state))
        {
            state = null!;
            return false;
        }

        _bodyStates[bodyId] = state;
        if (!_bodyIds.Contains(bodyId))
        {
            _bodyIds.Add(bodyId);
            _bodyIds.Sort();
        }

        return true;
    }

    private static bool IsFrameDrawable(in AnimationsLoader.FrameInfo frame)
    {
        return frame.Width > 0 &&
            frame.Height > 0 &&
            frame.Pixels != null &&
            frame.Pixels.Length >= frame.Width * frame.Height;
    }

    private static ClassicUO.Utility.ClientVersion DetermineClientVersion(string rootPath)
    {
        var tiledataPath = Path.Combine(rootPath, "tiledata.mul");
        if (!File.Exists(tiledataPath))
        {
            return ClassicUO.Utility.ClientVersion.CV_7090;
        }

        return new FileInfo(tiledataPath).Length switch
        {
            >= 3188736 => ClassicUO.Utility.ClientVersion.CV_7090,
            >= 1644544 => ClassicUO.Utility.ClientVersion.CV_7000,
            _ => ClassicUO.Utility.ClientVersion.CV_6000,
        };
    }

    private void ResetState()
    {
        DisposePreviewTextures();
        _previewCache.Clear();
        _bodyStates.Clear();
        _bodyIds.Clear();
        _uoFileManager = null;
        _animations = null;
        _graphicsDevice = null;
        LoadedRootPath = string.Empty;
        IsReady = false;
    }

    private void DisposePreviewTextures()
    {
        var textures = new HashSet<Texture2D>();
        foreach (var preview in _previewCache.Values)
        {
            foreach (var frame in preview.Frames)
            {
                if (frame.Texture != null)
                {
                    textures.Add(frame.Texture);
                }
            }
        }

        foreach (var texture in textures)
        {
            texture.Dispose();
        }
    }

    private static AnimationGroups ResolveStorageGroup(AnimationGroupsType type, AnimationFlags flags, int actionCount)
    {
        return actionCount switch
        {
            (int)LowAnimationGroup.AnimationCount => AnimationGroups.Low,
            (int)HighAnimationGroup.AnimationCount => AnimationGroups.High,
            (int)PeopleAnimationGroup.AnimationCount => AnimationGroups.People,
            _ when type is AnimationGroupsType.Human or AnimationGroupsType.Equipment => AnimationGroups.People,
            _ when type == AnimationGroupsType.Animal => AnimationGroups.Low,
            _ when type == AnimationGroupsType.Monster && (flags & AnimationFlags.CalculateOffsetByLowGroup) != 0 => AnimationGroups.Low,
            _ when type == AnimationGroupsType.Monster && (flags & AnimationFlags.CalculateOffsetByPeopleGroup) != 0 => AnimationGroups.People,
            _ => AnimationGroups.High,
        };
    }

    private static AssetAnimationActionEntry[] BuildActions(AnimationGroups group, AnimationsLoader.AnimationDirection[] indices, bool usesUop)
    {
        var actions = new List<AssetAnimationActionEntry>();
        var actionCount = usesUop ? indices.Length : indices.Length / AnimationsLoader.MAX_DIRECTIONS;

        for (var actionId = 0; actionId < actionCount; actionId++)
        {
            var hasData = usesUop
                ? IsDirectionPopulated(indices[actionId])
                : Enumerable.Range(0, AnimationsLoader.MAX_DIRECTIONS).Any(direction => IsDirectionPopulated(indices[(actionId * AnimationsLoader.MAX_DIRECTIONS) + direction]));

            if (!hasData)
            {
                continue;
            }

            actions.Add(new AssetAnimationActionEntry((byte)actionId, GetActionDisplayName(group, actionId)));
        }

        return [.. actions];
    }

    private static bool IsDirectionPopulated(AnimationsLoader.AnimationDirection direction)
    {
        if (direction.Position == 0xFFFF_FFFF || direction.Size == 0xFFFF_FFFF)
        {
            return false;
        }

        return direction.Position != 0 || direction.Size != 0 || direction.UncompressedSize != 0;
    }

    private static string GetActionDisplayName(AnimationGroups group, int actionId)
    {
        var actionName = group switch
        {
            AnimationGroups.Low when Enum.IsDefined(typeof(LowAnimationGroup), actionId) => ((LowAnimationGroup)actionId).ToString(),
            AnimationGroups.High when Enum.IsDefined(typeof(HighAnimationGroup), actionId) => ((HighAnimationGroup)actionId).ToString(),
            AnimationGroups.People when Enum.IsDefined(typeof(PeopleAnimationGroup), actionId) => ((PeopleAnimationGroup)actionId).ToString(),
            _ => $"Action {actionId}"
        };

        return $"{actionId:00} {actionName}";
    }
}