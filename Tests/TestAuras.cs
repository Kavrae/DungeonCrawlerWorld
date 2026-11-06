using Engine.ECS.Components;
using Engine.ECS.Components.Stores;
using Engine.Events;
using Game.Effects;
using Game.Effects.Entries;
using Game.Modules.Auras;
using Game.Modules.Auras.Components;
using Game.Modules.StatusEffects;
using Microsoft.Xna.Framework;

namespace Tests;

/// <summary>Aura definitions, catalogs and aura-source services for a test that doesn't build content of its own.</summary>
internal static class TestAuras
{
    private readonly struct AuraMatcher(byte auraId) : IComponentMatcher<AuraSourceComponent>
    {
        public bool Matches(in AuraSourceComponent source) => source.AuraId == auraId;
    }

    /// <summary>The session-local id each catalog built here gives Light.</summary>
    public const byte LightId = 0;

    /// <inheritdoc cref="LightId"/>
    public const byte BurningId = 1;

    /// <inheritdoc cref="LightId"/>
    public const byte PoisonId = 2;

    /// <summary>An aura that only glows.</summary>
    public static readonly AuraDefinition Light = new(new Guid("00000000-0000-0000-0000-00000000a001"), "Light", Color.White);

    /// <summary>An aura that tops Burning stacks up to its strength, held on the entity as a whole.</summary>
    public static readonly AuraDefinition Burning = new(new Guid("00000000-0000-0000-0000-00000000a002"), "Burning", Color.DarkOrange,
        [new Effect([new StatusEffectGrant(StatusEffectType.Burning, StackCount: 1, StatusEffectGrantMode.TopUpTo)])]);

    /// <summary>An aura that tops Poison stacks up to its strength.</summary>
    public static readonly AuraDefinition Poison = new(new Guid("00000000-0000-0000-0000-00000000a003"), "Poison", Color.DarkGreen,
        [new Effect([new StatusEffectGrant(StatusEffectType.Poison, StackCount: 1, StatusEffectGrantMode.TopUpTo)])]);

    /// <summary>Burning's colour with no effects: an aura of its own, since one Guid is one definition.</summary>
    public static readonly AuraDefinition BurningGlowOnly = new(new Guid("00000000-0000-0000-0000-00000000a012"), "Burning glow", Burning.GlowColor);

    /// <summary>Poison's colour with no effects: an aura of its own, since one Guid is one definition.</summary>
    public static readonly AuraDefinition PoisonGlowOnly = new(new Guid("00000000-0000-0000-0000-00000000a013"), "Poison glow", Poison.GlowColor);

    /// <summary>Light and the glow-only Burning and Poison, none with an effect, at the same ids Catalog gives Light, Burning and Poison: for a test about sources, the grid or the glow.</summary>
    public static AuraCatalog GlowOnlyCatalog()
    {
        var auras = new AuraCatalog();
        auras.Register(Light);
        auras.Register(BurningGlowOnly);
        auras.Register(PoisonGlowOnly);
        return auras;
    }

    /// <summary>Light, Burning and Poison, the last two topping up their status effect's stacks.</summary>
    public static AuraCatalog Catalog()
    {
        var auras = new AuraCatalog();
        auras.Register(Light);
        auras.Register(Burning);
        auras.Register(Poison);
        return auras;
    }

    /// <summary>Adds an unkeyed source of the aura to entityId, or removes the one it has: a source switched on and off by hand.</summary>
    public static void ToggleSource(MultiComponentPool<AuraSourceComponent> sources, EventBus eventBus, int entityId, byte auraId, ushort power, byte size)
    {
        if (sources.CountMatching(entityId, new AuraMatcher(auraId)) > 0)
        {
            AuraSourceEffects.Revoke(sources, eventBus, entityId, auraId);
            return;
        }

        AuraSourceEffects.Apply(sources, eventBus, entityId, auraId, power, size);
    }

    public static AuraSources Sources(MultiComponentPool<AuraSourceComponent>? sources, EventBus eventBus, AuraCatalog? auras = null) =>
        new(sources ?? EmptyPools.Multi<AuraSourceComponent>(), auras ?? GlowOnlyCatalog(), eventBus);
}
