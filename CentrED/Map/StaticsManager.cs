using System.Collections.ObjectModel;
using System.Diagnostics.CodeAnalysis;

namespace CentrED.Map;

/// <summary>
/// Tracks all loaded static tiles, including animated entries, light emitters, and ghost previews.
/// </summary>
public class StaticsManager
{
    private static readonly ReadOnlyCollection<StaticObject> EMPTY = [];
    
    private ushort _Width;
    private ushort _Height;

    /// <summary>
    /// Gets the total number of tracked static objects.
    /// </summary>
    public int Count { get; private set; }

    private List<StaticObject>?[] _tiles;
    private Dictionary<int, StaticObject> _idDictionary = new();
    
    private List<StaticObject> _animatedTiles = [];

    /// <summary>
    /// Gets the statics that require animated-art updates.
    /// </summary>
    public IReadOnlyList<StaticObject> AnimatedTiles => _animatedTiles.AsReadOnly();
    
    private Dictionary<StaticObject, LightObject> _lightTiles = new();

    /// <summary>
    /// Gets the static-to-light overlay mapping for visible light sources.
    /// </summary>
    public IReadOnlyDictionary<StaticObject, LightObject>  LightTiles => _lightTiles.AsReadOnly();
    
    private Dictionary<TileObject, List<StaticObject>>  _ghostTiles = new();

    /// <summary>
    /// Gets all currently active ghost statics used for tool previews.
    /// </summary>
    public IEnumerable<StaticObject> GhostTiles => _ghostTiles.Values.SelectMany(x => x);

    /// <summary>
    /// Initializes internal storage for the supplied map dimensions.
    /// </summary>
    /// <param name="width">The map width in tiles.</param>
    /// <param name="height">The map height in tiles.</param>
    public void Initialize(ushort width, ushort height)
    {
        _Width = width;
        _Height = height;
        Clear();
    }

    /// <summary>
    /// Removes all tracked statics and previews.
    /// </summary>
    public void Clear()
    {
        Count = 0;
        _tiles = new List<StaticObject>[_Width * _Height];
        _idDictionary.Clear();
        _animatedTiles.Clear();
        _lightTiles.Clear();
        _ghostTiles.Clear();
    }

    /// <summary>
    /// Refreshes every tracked static after a render-affecting setting changes.
    /// </summary>
    public void UpdateAll()
    {
        foreach (var so in _idDictionary.Values)
        {
            so.Update();
        }
    }

    /// <summary>
    /// Looks up a static by object id.
    /// </summary>
    /// <param name="id">The picking object id.</param>
    /// <returns>The matching static object, or <see langword="null"/>.</returns>
    public StaticObject? Get(int id)
    {
        _idDictionary.TryGetValue(id, out var result);
        return result;
    }
    
    /// <summary>
    /// Gets the statics at the supplied tile coordinates.
    /// </summary>
    /// <param name="x">The tile X coordinate.</param>
    /// <param name="y">The tile Y coordinate.</param>
    /// <returns>A read-only list of statics at the requested tile.</returns>
    public ReadOnlyCollection<StaticObject> Get(int x, int y)
    {
        return Get((ushort)x, (ushort)y);
    }

    /// <summary>
    /// Gets the statics at the supplied tile coordinates.
    /// </summary>
    /// <param name="x">The tile X coordinate.</param>
    /// <param name="y">The tile Y coordinate.</param>
    /// <returns>A read-only list of statics at the requested tile.</returns>
    public ReadOnlyCollection<StaticObject> Get(ushort x, ushort y)
    {
        if (x > _Width || y > _Height)
            return EMPTY;
        var list = _tiles[Index(x, y)];
        return list?.AsReadOnly() ?? EMPTY;
    }
    
    /// <summary>
    /// Gets the tracked static object that matches the supplied tile payload.
    /// </summary>
    /// <param name="staticTile">The static tile to find.</param>
    /// <returns>The matching static object, or <see langword="null"/>.</returns>
    public StaticObject? Get(StaticTile staticTile)
    {
        var list = _tiles[Index(staticTile)];
        if (list == null || list.Count == 0)
            return null;
        return list.FirstOrDefault(so => so.StaticTile.Equals(staticTile));
    }
    
    /// <summary>
    /// Adds a static tile to the manager and updates auxiliary indexes.
    /// </summary>
    /// <param name="staticTile">The static tile to add.</param>
    public void Add(StaticTile staticTile)
    {
        var so = new StaticObject(staticTile);
        var index = Index(staticTile);
        var list = _tiles[index];
        if (list == null)
        {
            list = [];
            _tiles[index] = list;
        }
        list.Add(so);
        list.Sort();
        _idDictionary.Add(so.ObjectId, so);
        Count++;
        if (so.IsAnimated)
        {
            _animatedTiles.Add(so);
        }
        if (so.IsLight)
        {
            _lightTiles.Add(so, new LightObject(so));
        }
    }
    
    /// <summary>
    /// Removes a matching static tile from the manager.
    /// </summary>
    /// <param name="staticTile">The static tile to remove.</param>
    public void Remove(StaticTile staticTile)
    {
        var list = _tiles[Index(staticTile)];
        if (list == null || list.Count == 0)
            return;
        var found = list.Find(so => so.StaticTile.Equals(staticTile));
        if (found != null)
        {
            list.Remove(found);
            list.Sort();
            _idDictionary.Remove(found.ObjectId);
            if (found.IsAnimated)
            {
                _animatedTiles.Remove(found);
            }
            if (found.IsLight)
            {
                _lightTiles.Remove(found);
            }
        }
        Count--;
    }

    /// <summary>
    /// Removes all statics at the supplied tile coordinates.
    /// </summary>
    /// <param name="x">The tile X coordinate.</param>
    /// <param name="y">The tile Y coordinate.</param>
    public void Remove(ushort x, ushort y)
    {
        var index = Index(x, y);
        var so = _tiles[index];
        if (so != null)
        {
            _tiles[index] = null;
            Count -= so.Count;
            foreach (var staticObject in so)
            {
                _idDictionary.Remove(staticObject.ObjectId);
                if (staticObject.IsAnimated)
                {
                    _animatedTiles.Remove(staticObject);
                }
                if (staticObject.IsLight)
                {
                    _lightTiles.Remove(staticObject);
                }
            }
        }
    }
    
    /// <summary>
    /// Moves a tracked static to a new tile coordinate.
    /// </summary>
    /// <param name="staticTile">The source static tile.</param>
    /// <param name="newX">The destination X coordinate.</param>
    /// <param name="newY">The destination Y coordinate.</param>
    public void Move(StaticTile staticTile, ushort newX, ushort newY)
    {
        var list = _tiles[Index(staticTile)];
        if (list == null || list.Count == 0)
            return;
        var found = list.Find(so => so.StaticTile.Equals(staticTile));
        if (found != null)
        {
            list.Remove(found);
            found.UpdatePos(newX, newY, staticTile.Z);
            var newIndex = Index(newX, newY);
            var newList = _tiles[newIndex];
            if (newList == null)
            {
                newList = new();
                _tiles[newIndex] = newList;
            }
            newList.Add(found);
            newList.Sort();
        }
    }

    /// <summary>
    /// Updates a tracked static altitude and re-sorts the destination stack.
    /// </summary>
    /// <param name="tile">The static tile to elevate.</param>
    /// <param name="newZ">The destination altitude.</param>
    public void Elevate(StaticTile tile, sbyte newZ)
    {
        Get(tile)?.UpdatePos(tile.X, tile.Y, newZ);
        _tiles[Index(tile)]?.Sort();
    }

    /// <summary>
    /// Gets all ghost statics associated with a preview parent tile.
    /// </summary>
    /// <param name="parent">The preview parent.</param>
    /// <returns>The ghost statics for the parent.</returns>
    public List<StaticObject> GetGhosts(TileObject parent)
    {
        return _ghostTiles.TryGetValue(parent, out var ghosts) ? ghosts : [];
    }
    
    /// <summary>
    /// Gets the first ghost static associated with a preview parent tile.
    /// </summary>
    /// <param name="parent">The preview parent.</param>
    /// <param name="result">The matching ghost static when present.</param>
    /// <returns><see langword="true"/> when a ghost exists.</returns>
    public bool TryGetGhost(TileObject parent, [MaybeNullWhen(false)] out StaticObject result)
    {
        if (_ghostTiles.TryGetValue(parent, out var ghosts))
        {
            result = ghosts.FirstOrDefault();
            if(result == null)
                Console.WriteLine("[WARN] Encountered empty list of ghost tiles");
            return result != null;
        }
        result = null;
        return false;
    }

    /// <summary>
    /// Replaces the preview state for a tile with a single ghost static.
    /// </summary>
    /// <param name="parent">The preview parent.</param>
    /// <param name="ghost">The ghost static to track.</param>
    public void AddGhost(TileObject parent, StaticObject ghost)
    {
        _ghostTiles[parent] = [ghost];
    }

    /// <summary>
    /// Replaces the preview state for a tile with multiple ghost statics.
    /// </summary>
    /// <param name="parent">The preview parent.</param>
    /// <param name="ghosts">The ghost statics to track.</param>
    public void AddGhosts(TileObject parent, IEnumerable<StaticObject> ghosts)
    {
        _ghostTiles[parent] = ghosts.ToList();
    }

    /// <summary>
    /// Clears any ghost preview associated with a tile.
    /// </summary>
    /// <param name="parent">The preview parent.</param>
    public void ClearGhost(TileObject parent)
    {
        _ghostTiles.Remove(parent);
    }
    
    private int Index(StaticTile tile) => tile.X * _Height + tile.Y;
    private int Index(ushort x, ushort y) => x * _Height + y;
}