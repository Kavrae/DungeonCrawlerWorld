using Engine.Utilities;
using Game.Effects;
using Game.Effects.Entries;
using Game.Modules.Auras;
using Game.Modules.Health;
using Game.Modules.StatModifiers;
using Game.Modules.StatusEffects;
using Game.Tags;
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

    /// <summary>Lava's aura power at the lava itself: 8, then 6, 4 and 2 a tile further each (Linear over LavaAuraSize), and the glow follows the same values.</summary>
    private const ushort LavaAuraPower = 8;

    private const byte LavaAuraSize = 3;

    /// <summary>The Burning stacks standing in lava holds on the part touching it.</summary>
    private const int LavaContactBurningStacks = 8;

    public static readonly TerrainDefinition StoneFloor = new(
        StoneFloorKey, "Stone floor", "Roughly shaped stone floor.", "][", Color.White);

    public static readonly TerrainDefinition Dirt = new(
        DirtKey, "Dirt", "Ordinary dirt. Nothing special.", "..", Color.White, SpriteName: "Dirt");

    public static readonly TerrainDefinition Grass = new(
        GrassKey, "Grass", "Ordinary grass. Nothing special.", ",,", Color.LawnGreen, SpriteName: "Grass");

    public static readonly TerrainDefinition Lava = new(
        LavaKey, "Lava", "Hot lava. I do not recommend stepping on it.", "~~", Color.Yellow,
        Contact: new TerrainContact(
            [new Effect(
            [
                new DirectDamage(MinFlatDamage: 10, MaxFlatDamage: 10, BodyPart: BodyPartTargeting.GroundContact),
                new StatusEffectGrant(StatusEffectType.Burning, LavaContactBurningStacks, StatusEffectGrantMode.TopUpTo, BodyPartTargeting.GroundContact),
            ])],
            Tags: [GameTags.DamageFire],
            RepeatEveryFrames: GameFrameRate.FramesPerSecond),
        Aura: new TerrainAura(BuiltInAuraIds.Lava, LavaAuraPower, LavaAuraSize));

    /// <summary>Holy Ground's healing aura power: 8 standing on it, then 6, 4 and 2 a tile further each (Linear over HolyGroundAuraSize).</summary>
    private const ushort HolyGroundAuraPower = 8;

    private const byte HolyGroundAuraSize = 3;

    /// <summary>The share of incoming damage Holy Ground's blessing takes off.</summary>
    private const float HolyGroundDamageReduction = 0.10f;

    /// <summary>How long the blessing lasts after an entity leaves Holy Ground: five minutes. It is renewed every second while the entity stands there.</summary>
    private static readonly ushort HolyGroundBlessingFrames = GameTiming.FramesForSeconds(5 * 60);

    public static readonly TerrainDefinition HolyGround = new(
        HolyGroundKey, "Holy Ground", "Hallowed earth. Standing on it mends wounds and turns blows aside for a while after.", "*", Color.White,
        Contact: new TerrainContact(
            [new Effect([new StatModifierGrant(StatModifierTarget.IncomingDamage, StatModifierOperation.Multiplicative, StatModifierPolarity.Buff, CanModify: true, -HolyGroundDamageReduction, HolyGroundBlessingFrames,
                Stacking: StatModifierStacking.RefreshFromSameSource)])],
            RepeatEveryFrames: GameFrameRate.FramesPerSecond),
        Aura: new TerrainAura(BuiltInAuraIds.HolyGround, HolyGroundAuraPower, HolyGroundAuraSize));

    /// <summary>A structure: it stands on a MapLayer over stone floor, so it keeps the floor's background.</summary>
    public static readonly TerrainDefinition StoneWall = new(
        StoneWallKey, "Wall", "Basic wall. Default implementation.", "[][]", Color.DarkGray, SpriteName: "wall-stone",
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
