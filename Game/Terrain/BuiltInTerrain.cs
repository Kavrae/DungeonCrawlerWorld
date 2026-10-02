using Engine.Utilities;
using Game.Modules.Auras;
using Game.Modules.Health;
using Game.Modules.StatModifiers;
using Game.Modules.StatusEffects;
using Game.Tags;
using Game.Effects;
using Game.Effects.Entries;
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
    public const string HolyGroundKey = "core:holy-ground";

    /// <summary>Lava's aura strength -- it halves with each tile of Manhattan distance, and the glow follows the same falloff.</summary>
    private const byte LavaAuraStrength = 8;

    /// <summary>Lava's own aura: each tick tops one body part, picked at random, up to the aura's strength at the entity in Burning stacks.</summary>
    /// <remarks>A different part each tick, each burning on its own, so the longer an entity stays the more of it is alight. An entity without body parts burns as a whole.</remarks>
    public static readonly AuraDefinition LavaAura = new(new Guid("d9f6a1c4-8b2e-4f3a-9c1d-000000000301"), "Lava", Color.DarkOrange,
        [new Effect([new StatusEffectGrant(StatusEffectType.Burning, StackCount: 1, StatusEffectGrantMode.TopUpTo, BodyPartTargeting.Random)])]);

    /// <summary>The Burning stacks standing in lava holds on the part touching it.</summary>
    private const int LavaContactBurningStacks = 8;

    public static readonly TerrainDefinition StoneFloor = new(
        StoneFloorKey, "Stone floor", "Roughly shaped stone floor.", Color.LightGray, string.Empty, Color.White);

    public static readonly TerrainDefinition Dirt = new(
        DirtKey, "Dirt", "Ordinary dirt. Nothing special.", Color.Tan, string.Empty, Color.White, SpriteName: "Dirt");

    public static readonly TerrainDefinition Grass = new(
        GrassKey, "Grass", "Ordinary grass. Nothing special.", Color.ForestGreen, ",", Color.LawnGreen, SpriteName: "Grass");

    public static readonly TerrainDefinition Lava = new(
        LavaKey, "Lava", "Hot lava. I do not recommend stepping on it.", Color.OrangeRed, "~", Color.Yellow,
        Contact: new TerrainContact(
            [new Effect(
            [
                new DirectDamage(MinFlatDamage: 10, MaxFlatDamage: 10, BodyPart: BodyPartTargeting.GroundContact),
                new StatusEffectGrant(StatusEffectType.Burning, LavaContactBurningStacks, StatusEffectGrantMode.TopUpTo, BodyPartTargeting.GroundContact),
            ])],
            Tags: [GameTags.DamageFire],
            RepeatEveryFrames: GameTiming.FramesPerSecond),
        Aura: new TerrainAura(LavaAura, LavaAuraStrength));

    /// <summary>Holy Ground's healing aura strength: 8 standing on it, then 4, 2 and 1 a tile further each.</summary>
    private const byte HolyGroundAuraStrength = 8;

    /// <summary>The share of incoming damage Holy Ground's blessing takes off.</summary>
    private const float HolyGroundDamageReduction = 0.10f;

    /// <summary>How long the blessing lasts after an entity leaves Holy Ground: five minutes. It is renewed every second while the entity stands there.</summary>
    private static readonly ushort HolyGroundBlessingFrames = GameTiming.FramesForSeconds(5 * 60);

    /// <summary>Holy Ground's own aura: each tick heals one point of health per point of strength at the entity, to the body part its healing priority picks.</summary>
    public static readonly AuraDefinition HolyGroundAura = new(new Guid("d9f6a1c4-8b2e-4f3a-9c1d-000000000305"), "Holy Ground", Color.White,
        [new Effect([new DirectHeal(PercentOfMaxHealth: 0f, FlatAmount: 1f, BodyPartTargetMode: BodyPartTargetMode.LowestPercentage)])]);

    public static readonly TerrainDefinition HolyGround = new(
        HolyGroundKey, "Holy Ground", "Hallowed earth. Standing on it mends wounds and turns blows aside for a while after.", Color.PaleGoldenrod, "*", Color.White,
        Contact: new TerrainContact(
            [new Effect([new StatModifierGrant(StatModifierTarget.IncomingDamage, StatModifierOperation.Multiplicative, StatModifierPolarity.Buff, CanModify: true, -HolyGroundDamageReduction, HolyGroundBlessingFrames,
                Stacking: StatModifierStacking.RefreshFromSameSource)])],
            RepeatEveryFrames: GameTiming.FramesPerSecond),
        Aura: new TerrainAura(HolyGroundAura, HolyGroundAuraStrength));

    /// <summary>A structure: it stands on a MapLayer over stone floor, so it keeps the floor's background.</summary>
    public static readonly TerrainDefinition StoneWall = new(
        StoneWallKey, "Wall", "Basic wall. Default implementation.", Color.LightGray, "[][]", Color.DarkGray, SpriteName: "Wall",
        BlocksMovement: true);

    public static void RegisterAll(TerrainRegistry registry)
    {
        registry.Register(StoneFloor);
        registry.Register(Dirt);
        registry.Register(Grass);
        registry.Register(Lava);
        registry.Register(StoneWall);
        registry.Register(HolyGround);
    }
}
