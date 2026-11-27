using Engine.ECS.Components;
using Engine.Math;
using Engine.Settings;
using Engine.Tags;
using Engine.Tiles;
using Game.Effects;
using Game.Effects.Entries;
using Game.Modules.Auras.Components;
using Game.Modules.Health;
using Game.Modules.Race.Components;
using Game.Modules.StatModifiers;
using Game.Modules.StatusEffects;
using Game.Spawning;
using Game.Tags;
using Microsoft.Xna.Framework;

namespace Game.Modules.Auras;

/// <summary>The aura stress benchmark: every built creature, or a share of them, radiates a random small, medium and large aura, one of each of the game's kinds of effect.</summary>
/// <remarks>
/// <para>
/// Off unless StressAuraCreaturePercent is above 0 (<c>--setting=AurasModule.StressAuraCreaturePercent=100</c>), so a
/// normal game never sees it; the phase-performance-testing script passes it through <c>-ExtraArguments</c>.
/// StressAuraSizePercent scales every reach.
/// </para>
/// <para>
/// The auras are built here, not read from content: they are a benchmark's, never a player's, so no layer holds them and
/// the Workshop never lists them. Their Guids are in a range no content uses. A creature's three picks come from its
/// spawn seed, so a seed still replays exactly, and the spawn-record rebuild draws the same three.
/// </para>
/// </remarks>
public static class AuraStressTest
{
    public static readonly SettingKey<int> CreaturePercent = new(AurasModule.ModuleId, "StressAuraCreaturePercent");
    public static readonly SettingKey<int> SizePercent = new(AurasModule.ModuleId, "StressAuraSizePercent");

    private static readonly byte[] BaseSizes = [3, 8, 15];
    private static readonly ushort[] Powers = [1, 2, 3];
    private static readonly string[] SizeNames = ["Small", "Medium", "Large"];
    private static readonly ulong StressSalt = SeedMix.Of(new Guid("5e0c3f1a-0000-4000-8000-a0a5a5a5a5a5"));

    /// <summary>The Guid of one stress aura: three sizes per effect, in a range no content uses.</summary>
    public static Guid GuidOf(int effectIndex, int sizeIndex) =>
        new($"d9f6a1c4-8b2e-4f3a-9c1d-{0x900 + (effectIndex * BaseSizes.Length) + sizeIndex:x12}");

    /// <summary>How many stress auras a benchmark registers: three sizes of each effect.</summary>
    public static int AuraCount => StressEffects().Length * BaseSizes.Length;

    public static void Declare(SettingsDeclarations settings)
    {
        settings.Declare(CreaturePercent, 0, static value => value is >= 0 and <= 100 ? null : "must be from 0 to 100");
        settings.Declare(SizePercent, 100, static value => value is >= 1 and <= 1000 ? null : "must be from 1 to 1000");
    }

    /// <summary>Registers the stress auras and adds the build step that hands them out, when the benchmark is on.</summary>
    public static void Wire(GameModuleContext context, SettingValues settings)
    {
        var creaturePercent = settings.Get(CreaturePercent);
        if (creaturePercent == 0)
        {
            return;
        }

        var effects = StressEffects();
        var auraIdsBySize = new byte[BaseSizes.Length][];
        for (var sizeIndex = 0; sizeIndex < BaseSizes.Length; sizeIndex++)
        {
            auraIdsBySize[sizeIndex] = new byte[effects.Length];
            for (var effectIndex = 0; effectIndex < effects.Length; effectIndex++)
            {
                var (name, color, entries, magnitude, falloff) = effects[effectIndex];
                var definition = new AuraDefinition(
                    GuidOf(effectIndex, sizeIndex),
                    $"Stress {SizeNames[sizeIndex]} {name}",
                    color,
                    entries is null ? null : [new Effect(entries)],
                    Magnitude: magnitude,
                    Falloff: falloff,
                    Description: "An aura of the stress benchmark.");
                auraIdsBySize[sizeIndex][effectIndex] = context.Auras.Register(definition);
            }
        }

        var sizePercent = settings.Get(SizePercent);
        var sizes = BaseSizes.Select(size => (byte)Math.Clamp(size * sizePercent / 100, 1, byte.MaxValue)).ToArray();
        context.EntityFactory.AddBuildStep(new StressAuraBuildStep(auraIdsBySize, sizes, creaturePercent));
    }

    /// <summary>One aura per kind of effect an aura tick can apply, plus one that only glows.</summary>
    private static (string Name, Color Color, IEffectEntry[]? Entries, AuraMagnitude Magnitude, AuraFalloff Falloff)[] StressEffects() =>
    [
        ("Burning", Color.OrangeRed, [new StatusEffectGrant(StatusEffectType.Burning, Mode: StatusEffectGrantMode.TopUpTo, BodyPart: BodyPartTargeting.Random)], AuraMagnitude.Power, AuraFalloff.Linear),
        ("Poison", Color.LimeGreen, [new StatusEffectGrant(StatusEffectType.Poison, Mode: StatusEffectGrantMode.TopUpTo)], AuraMagnitude.Power, AuraFalloff.Linear),
        ("Heal", Color.LightGreen, [new DirectHeal(0f, FlatAmount: 1f, BodyPartTargetMode: BodyPartTargetMode.LowestPercentage)], AuraMagnitude.Power, AuraFalloff.Linear),
        ("Damage", Color.Red, [new DirectDamage(1, 1)], AuraMagnitude.Power, AuraFalloff.Linear),
        ("Mana Restore", Color.Blue, [new DirectManaRestore(0.01f)], AuraMagnitude.Power, AuraFalloff.Linear),
        ("Mana Drain", Color.Purple, [new ManaDrain(1)], AuraMagnitude.Power, AuraFalloff.Linear),
        ("Health Drain", Color.DarkRed, [new HealthDrain(1)], AuraMagnitude.Power, AuraFalloff.Linear),
        ("Might", Color.Gold, [new StatModifierGrant(StatModifierTarget.OutgoingDamage, StatModifierOperation.Multiplicative, StatModifierPolarity.Buff, false, 0.1f, 120, Stacking: StatModifierStacking.RefreshFromAnySource, FixedDuration: true)], AuraMagnitude.Flat, AuraFalloff.None),
        ("Frailty", Color.Gray, [new StatModifierGrant(StatModifierTarget.IncomingDamage, StatModifierOperation.Multiplicative, StatModifierPolarity.Debuff, false, 0.1f, 120, Stacking: StatModifierStacking.RefreshFromAnySource, FixedDuration: true)], AuraMagnitude.Flat, AuraFalloff.None),
        ("Fireproof", Color.SteelBlue, [new StatusEffectImmunityGrant(StatusEffectType.Burning, 90)], AuraMagnitude.Flat, AuraFalloff.None),
        ("Cleanse", Color.White, [new StatusEffectRemoval(StatusEffectType.Poison)], AuraMagnitude.Flat, AuraFalloff.None),
        ("Silence", Color.Magenta, [new ActivationDisableGrant(GameplayTagQuery.Any([GameTags.Spell]), 90, FixedDuration: true)], AuraMagnitude.Flat, AuraFalloff.None),
        ("Dread", Color.DarkSlateGray, [new StatModifierGrant(StatModifierTarget.BehaviorCaution, StatModifierOperation.Multiplicative, StatModifierPolarity.Debuff, false, 3f, 120, Stacking: StatModifierStacking.RefreshFromAnySource, FixedDuration: true)], AuraMagnitude.Flat, AuraFalloff.None),
        ("Glow", Color.LightYellow, null, AuraMagnitude.Power, AuraFalloff.Linear),
    ];

    /// <summary>Gives a built creature its three stress auras: one small, one medium, one large, each of an effect its spawn seed picks.</summary>
    private sealed class StressAuraBuildStep(byte[][] auraIdsBySize, byte[] sizes, int creaturePercent) : IEntityBuildStep
    {
        public void Built(ComponentManager componentManager, int entityId)
        {
            if (!componentManager.GetPackedPool<RaceSlotsComponent>().Has(entityId)
                || !componentManager.GetDirectPool<SpawnRecordComponent>().TryGetReadonly(entityId, out var record))
            {
                return;
            }

            var roll = SeedMix.Mix(record.Seed, StressSalt);
            if ((int)(roll % 100) >= creaturePercent)
            {
                return;
            }

            var sources = componentManager.GetMultiPool<AuraSourceComponent>();
            for (var sizeIndex = 0; sizeIndex < sizes.Length; sizeIndex++)
            {
                roll = SeedMix.Mix(roll, (ulong)sizeIndex + 1);
                var auraIds = auraIdsBySize[sizeIndex];
                sources.Add(entityId, new AuraSourceComponent(auraIds[(int)((roll >> 8) % (ulong)auraIds.Length)], Powers[sizeIndex], sizes[sizeIndex]));
            }
        }

        public void Applied(ComponentManager componentManager, int entityId)
        {
        }
    }
}
