using Engine.Math;
using Engine.Tiles;
using Game.Areas;
using Game.Bootstrap;
using Game.Modules.Core.Components;
using Game.Terrain;
using Game.World;

namespace Builders.MapAreas;

/// <summary>One area as the game builds it: its own game session over a bounded map the area's size, the area stamped in by the game's own AreaStamp and its spawns spawned by the session's own factory -- never simulated.</summary>
/// <remarks>
/// Under the area is a plain fill (grass on Ground, dirt UnderGround), so a transparent cell shows what generation
/// would leave there. Uniques are spawned as in a session where none has lived yet. Nothing is simulated, so an
/// entity's own aura (a shrine's glow) isn't in the aura field; terrain's is.
/// </remarks>
public sealed class AreaPreviewBuild : IMapPreviewScene
{
    public const string GroundFillKey = BuiltInTerrain.GrassKey;
    public const string UnderGroundFillKey = BuiltInTerrain.DirtKey;

    private const int LayerCount = 3;

    private AreaPreviewBuild(GameSession session, AreaInstance instance, AreaStampResult stamp)
    {
        Session = session;
        Instance = instance;
        Stamp = stamp;
    }

    public GameSession Session { get; }

    public AreaInstance Instance { get; }

    AreaInstance? IMapPreviewScene.Area => Instance;

    /// <summary>What stamping the instance decided: the cells it covers, its spawns and its markers.</summary>
    public AreaStampResult Stamp { get; }

    /// <param name="buildSession">Builds a session over a map, with the content the IDE's own session has.</param>
    /// <param name="seed">The instance's seed: what its Chance and OneOf roll from.</param>
    public static AreaPreviewBuild Build(AreaDefinition area, ulong seed, Func<Map, GameSession> buildSession)
    {
        var map = new Map(new MapBounds(0, 0, System.Math.Max(area.Width, 1), System.Math.Max(area.Height, 1), LayerCount));
        var session = buildSession(map);
        var catalogs = session.Catalogs;

        var layout = map.CreateLayout(0, 0, layoutSeed: 0);
        Fill(layout, catalogs.Terrain);

        var instance = new AreaInstance(new AreaInstanceKey(area.Id), area, seed, OriginX: 0, OriginY: 0);
        var stamp = new AreaStampResult();
        new AreaStamp(catalogs.Areas, catalogs.Terrain, catalogs.Definitions).Apply(layout, instance, stamp);
        map.LoadNeighborhood(layout);
        session.Internals.AuraField.EnsureBuilt();

        foreach (var planned in stamp.Spawns)
        {
            AreaSpawner.Spawn(session.Internals.Factory, session.Internals.UniqueEntities, planned);
        }

        return new AreaPreviewBuild(session, instance, stamp);
    }

    /// <summary>The plain fill the area is stamped over.</summary>
    public static void Fill(NeighborhoodLayout layout, TerrainRegistry terrain)
    {
        var ground = new TerrainCell(terrain.GetId(GroundFillKey));
        var underGround = new TerrainCell(terrain.GetId(UnderGroundFillKey));
        for (var y = layout.MinY; y < layout.MaxY; y++)
        {
            for (var x = layout.MinX; x < layout.MaxX; x++)
            {
                layout.SetTerrain(x, y, TerrainLayer.Ground, ground);
                layout.SetTerrain(x, y, TerrainLayer.UnderGround, underGround);
            }
        }
    }
}
