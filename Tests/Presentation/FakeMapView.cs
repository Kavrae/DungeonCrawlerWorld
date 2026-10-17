using Engine.Math;
using Engine.Tiles;
using Game.Modules.Core.Components;
using Game.Terrain;
using Game.Views;
using Game.World;
using Microsoft.Xna.Framework;

namespace Tests.Presentation;

/// <summary>
/// A map view over a handful of occupants and walls, for the map's draw planners. An occupant occupies every tile of its
/// footprint, a Blocking one as that tile's Blocking occupant. A wall is a structure drawn with the named sprite, every cell's seed 0.
/// Everything inside 0..1023 is on the map.
/// </summary>
internal sealed class FakeMapView : IMapViewQuery
{
    private const int MapSize = 1024;

    private readonly Dictionary<int, OccupantView> _occupants = [];
    private readonly Dictionary<(int X, int Y), List<int>> _occupantsByTile = [];
    private readonly Dictionary<(int X, int Y), int> _blockingByTile = [];
    private readonly Dictionary<(int X, int Y), byte> _layerMasks = [];
    private readonly Dictionary<(int X, int Y), ushort> _walls = [];

    public MapBounds Bounds => new(0, 0, MapSize, MapSize, 3);

    public int PlayerEntityId => -1;

    public int AddBlocking(int x, int y, Vector2Byte? size = null) => Add(x, y, size ?? new Vector2Byte(1, 1), NonBlockingKind.None, isDead: false, isBlocking: true);

    public int AddNonBlocking(int x, int y, Vector2Byte size, NonBlockingKind kind, bool isDead = false) => Add(x, y, size, kind, isDead, isBlocking: false);

    public void SetOccupiedLayers(int x, int y, byte mask) => _layerMasks[(x, y)] = mask;

    public TerrainRegistry Terrain { get; } = new();

    /// <summary>A wall at (x, y) drawn with the sprite manifest entry spriteName; walls of one name are one terrain type, and so join each other.</summary>
    public void AddWall(int x, int y, string spriteName)
    {
        var key = $"test:{spriteName}";
        _walls[(x, y)] = Terrain.TryGetId(key, out var typeId) ? typeId : Terrain.Register(new TerrainDefinition(key, spriteName, "", "#", Color.Gray, SpriteName: spriteName, BlocksMovement: true));
    }

    public void CopyCells(int minX, int minY, int width, int height, int mapLayer, MapCellBuffer cells)
    {
        cells.Reset(minX, minY, width, height, mapLayer);
        for (var row = 0; row < height; row++)
        {
            for (var column = 0; column < width; column++)
            {
                var position = new Vector3Int(minX + column, minY + row, mapLayer);
                if (IsOnMap(position))
                {
                    cells.Write(column + row * width, 0, 0, _walls.TryGetValue((position.X, position.Y), out var wall) ? wall : (ushort)0, 0);
                }
            }
        }
    }

    public bool IsOnMap(Vector3Int position) => position.X >= 0 && position.Y >= 0 && position.X < MapSize && position.Y < MapSize;

    public int GetBlockingEntityId(Vector3Int position) => _blockingByTile.TryGetValue((position.X, position.Y), out var entityId) ? entityId : -1;

    public ReadOnlySpan<int> GetOccupants(Vector3Int position) =>
        _occupantsByTile.TryGetValue((position.X, position.Y), out var occupants) ? occupants.ToArray() : [];

    public byte GetOccupiedLayerMask(int x, int y) => _layerMasks.TryGetValue((x, y), out var mask) ? mask : (byte)0;

    public bool TryGetOccupant(int entityId, out OccupantView occupant) => _occupants.TryGetValue(entityId, out occupant);

    public bool TryGetTerrain(int x, int y, int layer, out TerrainView terrain) => throw new NotSupportedException();

    public bool TryGetStructure(int x, int y, int layer, out TerrainView structure) => throw new NotSupportedException();

    public bool TryGetVisual(int entityId, out EntityVisualView visual) => throw new NotSupportedException();

    public EntityStatusView GetStatus(int entityId) => throw new NotSupportedException();

    public bool TryGetChargingAction(int entityId, out ChargingActionView action) => throw new NotSupportedException();

    public float GetChargeFraction(int entityId) => throw new NotSupportedException();

    public EntityInteractionView GetInteraction(int entityId) => throw new NotSupportedException();

    private int Add(int x, int y, Vector2Byte size, NonBlockingKind kind, bool isDead, bool isBlocking)
    {
        var entityId = _occupants.Count + 1;
        _occupants[entityId] = new OccupantView(new Vector3Int(x, y, 0), size, kind, isDead);

        for (var tileX = x; tileX < x + size.X; tileX++)
        {
            for (var tileY = y; tileY < y + size.Y; tileY++)
            {
                if (!_occupantsByTile.TryGetValue((tileX, tileY), out var occupants))
                {
                    _occupantsByTile[(tileX, tileY)] = occupants = [];
                }

                occupants.Add(entityId);
                if (isBlocking)
                {
                    _blockingByTile[(tileX, tileY)] = entityId;
                }
            }
        }

        return entityId;
    }
}
