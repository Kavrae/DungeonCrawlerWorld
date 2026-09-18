using Engine.Utilities;
using Game.Modules.StatusEffectAura.Components;
using Game.Modules.StatusEffects;
using Microsoft.Xna.Framework;

namespace Game.Terrain;

/// <summary>The terrain every session has: keys and definitions.</summary>
public static class BuiltInTerrain
{
    public const string StoneFloorKey = "core:stone-floor";
    public const string DirtKey = "core:dirt";
    public const string GrassKey = "core:grass";
    public const string LavaKey = "core:lava";
    public const string StoneWallKey = "core:stone-wall";

    /// <summary>Lava's burning aura strength -- it halves with each tile of Manhattan distance, and the glow follows the same falloff.</summary>
    private const byte LavaAuraStrength = 8;

    public static readonly TerrainDefinition StoneFloor = new(
        StoneFloorKey, "Stone floor", "Roughly shaped stone floor.", Color.LightGray, string.Empty, Color.White);

    public static readonly TerrainDefinition Dirt = new(
        DirtKey, "Dirt", "Ordinary dirt. Nothing special.", Color.Tan, string.Empty, Color.White, SpriteName: "Dirt");

    public static readonly TerrainDefinition Grass = new(
        GrassKey, "Grass", "Ordinary grass. Nothing special.", Color.ForestGreen, ",", Color.LawnGreen, SpriteName: "Grass");

    public static readonly TerrainDefinition Lava = new(
        LavaKey, "Lava", "Hot lava. I do not recommend stepping on it.", Color.OrangeRed, "~", Color.Yellow,
        ContactHazard: new ContactHazard(DamagePerTick: 10, TickIntervalFrames: GameTiming.FramesPerSecond),
        Aura: new StatusEffectAuraSourceComponent(StatusEffectType.Burning, LavaAuraStrength, Color.DarkOrange));

    /// <summary>A structure: it stands on a MapLayer over stone floor, so it keeps the floor's background.</summary>
    public static readonly TerrainDefinition StoneWall = new(
        StoneWallKey, "Wall", "Basic wall. Default implementation.", Color.LightGray, "[][]", Color.DarkGray, SpriteName: "Wall",
        BlocksMovement: true);

    public static void RegisterAll(TerrainRegistry registry)
    {
        ArgumentNullException.ThrowIfNull(registry);

        registry.Register(StoneFloor);
        registry.Register(Dirt);
        registry.Register(Grass);
        registry.Register(Lava);
        registry.Register(StoneWall);
    }
}
