using Engine.Math;
using Game.Modules.Core.Components;
using Game.Views;
using Microsoft.Xna.Framework;

namespace Tests.Presentation;

/// <summary>
/// A map view over a handful of occupants and walls, for the map's draw planners. An occupant occupies every tile of its
/// footprint, a Blocking one as that tile's Blocking occupant. Everything inside 0..1023 is on the map.
/// </summary>
internal sealed class FakeMapView : IMapViewQuery
{
    private const int MapSize = 1024;

    private readonly Dictionary<int, OccupantView> _occupants = [];
    private readonly Dictionary<(int X, int Y), List<int>> _occupantsByTile = [];
    private readonly Dictionary<(int X, int Y), int> _blockingByTile = [];
    private readonly Dictionary<(int X, int Y), byte> _layerMasks = [];
    private readonly Dictionary<(int X, int Y), (EntityVisualView Face, StructureTopView? Top)> _walls = [];

    public MapBounds Bounds => new(0, 0, MapSize, MapSize, 3);

    public int PlayerEntityId => -1;

    public int AddBlocking(int x, int y, Vector2Byte? size = null) => Add(x, y, size ?? new Vector2Byte(1, 1), NonBlockingKind.None, isDead: false, isBlocking: true);

    public int AddNonBlocking(int x, int y, Vector2Byte size, NonBlockingKind kind, bool isDead = false) => Add(x, y, size, kind, isDead, isBlocking: false);

    public void SetOccupiedLayers(int x, int y, byte mask) => _layerMasks[(x, y)] = mask;

    /// <summary>A wall at (x, y) drawn with face and, when given, top raised raisedRows above it.</summary>
    public void AddWall(int x, int y, EntityVisualView face, EntityVisualView? top = null, int raisedRows = 1) =>
        _walls[(x, y)] = (face, top is { } topVisual ? new StructureTopView(topVisual, raisedRows) : null);

    public bool IsOnMap(Vector3Int position) => position.X >= 0 && position.Y >= 0 && position.X < MapSize && position.Y < MapSize;

    public int GetBlockingEntityId(Vector3Int position) => _blockingByTile.TryGetValue((position.X, position.Y), out var entityId) ? entityId : -1;

    public ReadOnlySpan<int> GetOccupants(Vector3Int position) =>
        _occupantsByTile.TryGetValue((position.X, position.Y), out var occupants) ? occupants.ToArray() : [];

    public byte GetOccupiedLayerMask(int x, int y) => _layerMasks.TryGetValue((x, y), out var mask) ? mask : (byte)0;

    public bool TryGetOccupant(int entityId, out OccupantView occupant) => _occupants.TryGetValue(entityId, out occupant);

    public bool HasStructure(int x, int y, int layer) => _walls.ContainsKey((x, y));

    public bool TryGetStructureVisual(int x, int y, int layer, out EntityVisualView visual)
    {
        var found = _walls.TryGetValue((x, y), out var wall);
        visual = wall.Face;
        return found;
    }

    public bool TryGetStructureTopVisual(int x, int y, int layer, out StructureTopView top)
    {
        if (_walls.TryGetValue((x, y), out var wall) && wall.Top is { } wallTop)
        {
            top = wallTop;
            return true;
        }

        top = default;
        return false;
    }

    public bool TryGetTerrainVisual(int x, int y, int layer, out EntityVisualView visual) => throw new NotSupportedException();

    public bool TryGetTerrain(int x, int y, int layer, out TerrainView terrain) => throw new NotSupportedException();

    public bool TryGetStructure(int x, int y, int layer, out TerrainView structure) => throw new NotSupportedException();

    public Color GetBackgroundColor(int x, int y, int layer) => throw new NotSupportedException();

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
