using Engine.Modules;
using Engine.Tags;
using Game.Effects;
using Game.Effects.Entries;
using Game.Modules;
using Game.Modules.Auras;
using Game.Modules.Inventory;
using Game.Modules.StatModifiers;
using Game.Terrain;
using Microsoft.Xna.Framework;

namespace Tests.Effects;

/// <summary>A build's content checks cover every effect entry, whatever holds it: an action, an item, a terrain contact, an aura, or another entry.</summary>
[TestClass]
public sealed class EffectContentValidationTests
{
    private static readonly GameplayTag UndeclaredTag = GameplayTag.Get("EffectContentValidationTests.Undeclared");
    private static readonly Guid NamedOnlyByAnEffectAuraGuid = new("00000000-0000-0000-0000-0000000000d1");
    private static readonly Guid HolderAuraGuid = new("00000000-0000-0000-0000-0000000000d2");

    private sealed class RegisteringModule(Action<GameModuleContext> configure) : IGameModule
    {
        public void RegisterComponents(ComponentRegistration registration)
        {
        }

        public void Configure(GameModuleContext context) => configure(context);

        public void RegisterBehavior(BehaviorRegistration<GameModuleContext> registration)
        {
        }
    }

    private static Effect ModifierConditionedOn(GameplayTag tag) =>
        new([new StatModifierGrant(StatModifierTarget.IncomingDamage, StatModifierOperation.Multiplicative, StatModifierPolarity.Buff, CanModify: true, -0.1f, DurationFrames: 60, tag)]);

    private static Effect GrantOf(AuraDefinition aura) => new([new AuraSourceGrant(aura, Strength: 4)]);

    /// <summary>An aura nothing registers: only an effect entry names it, and its own effects use an undeclared tag.</summary>
    private static readonly AuraDefinition AuraWithAnUndeclaredTagInside = new(NamedOnlyByAnEffectAuraGuid, "Hidden Blight", Color.Purple, [ModifierConditionedOn(UndeclaredTag)]);

    private static InvalidOperationException BuildWith(Action<GameModuleContext> configure) =>
        Assert.ThrowsExactly<InvalidOperationException>(() => BuiltInTestModules.BuildModules([new RegisteringModule(configure)]));

    [TestMethod]
    public void TerrainContact_WhoseEffectNamesAnUndeclaredTag_ThrowsNamingTheTerrainAndTag()
    {
        var exception = BuildWith(context => context.Terrain.Register(new TerrainDefinition("test:bog", "Bog", "", default, "~", default,
            Contact: new TerrainContact([ModifierConditionedOn(UndeclaredTag)]))));

        Assert.Contains("Terrain 'Bog'", exception.Message);
        Assert.Contains(UndeclaredTag.Name, exception.Message);
    }

    [TestMethod]
    public void Aura_WhoseOwnTagIsUndeclared_ThrowsNamingTheAuraAndTag()
    {
        var exception = BuildWith(context => context.Terrain.Register(new TerrainDefinition("test:bog", "Bog", "", default, "~", default,
            Aura: new TerrainAura(new AuraDefinition(HolderAuraGuid, "Miasma", Color.Green, Tags: [UndeclaredTag]), 4))));

        Assert.Contains("Aura 'Miasma'", exception.Message);
        Assert.Contains(UndeclaredTag.Name, exception.Message);
    }

    [TestMethod]
    public void Aura_WhoseEffectNamesAnUndeclaredTag_ThrowsNamingTheAuraAndTag()
    {
        var exception = BuildWith(context => context.Terrain.Register(new TerrainDefinition("test:bog", "Bog", "", default, "~", default,
            Aura: new TerrainAura(new AuraDefinition(HolderAuraGuid, "Miasma", Color.Green, [ModifierConditionedOn(UndeclaredTag)]), 4))));

        Assert.Contains("Aura 'Miasma'", exception.Message);
        Assert.Contains(UndeclaredTag.Name, exception.Message);
    }

    /// <summary>No module registers an aura: one that only an effect entry names is registered by the build, however deep the entry sits.</summary>
    [TestMethod]
    public void AuraNamedOnlyByAnItemsChainedEffect_IsRegisteredByTheBuild()
    {
        var harmless = new AuraDefinition(NamedOnlyByAnEffectAuraGuid, "Quiet Glow", Color.Yellow);
        var chained = new Effect([new ChainedEffect(1f, [new Effect([new ChainedEffect(1f, [GrantOf(harmless)])])])]);

        var build = BuiltInTestModules.BuildModules([new RegisteringModule(context => context.Items.Register(new ItemDefinition(Guid.NewGuid(), "Charm", null, "?", Color.White, [], [chained])))]);

        Assert.IsTrue(build.Context.Auras.TryGetId(NamedOnlyByAnEffectAuraGuid, out var auraId));
        Assert.AreSame(harmless, build.Context.Auras.Get(auraId));
    }

    /// <summary>An aura found that way is content like any other: what its own effects name is checked too.</summary>
    [TestMethod]
    public void AuraNamedOnlyByATerrainContact_HasItsOwnEffectsChecked()
    {
        var exception = BuildWith(context => context.Terrain.Register(new TerrainDefinition("test:bog", "Bog", "", default, "~", default,
            Contact: new TerrainContact([GrantOf(AuraWithAnUndeclaredTagInside)]))));

        Assert.Contains("Aura 'Hidden Blight'", exception.Message);
        Assert.Contains(UndeclaredTag.Name, exception.Message);
    }

    /// <summary>An aura radiated by another aura's effects is reached the same way.</summary>
    [TestMethod]
    public void AuraNamedOnlyByAnotherAurasEffects_HasItsOwnEffectsChecked()
    {
        var exception = BuildWith(context => context.Terrain.Register(new TerrainDefinition("test:bog", "Bog", "", default, "~", default,
            Aura: new TerrainAura(new AuraDefinition(HolderAuraGuid, "Miasma", Color.Green, [GrantOf(AuraWithAnUndeclaredTagInside)]), 4))));

        Assert.Contains("Aura 'Hidden Blight'", exception.Message);
    }

    [TestMethod]
    public void Item_WhoseChainedEffectNamesAnUndeclaredTag_Throws()
    {
        var chained = new Effect([new ChainedEffect(1f, [ModifierConditionedOn(UndeclaredTag)])]);

        var exception = BuildWith(context => context.Items.Register(new ItemDefinition(Guid.NewGuid(), "Cursed Charm", null, "?", Color.White, [], [chained])));

        Assert.Contains(UndeclaredTag.Name, exception.Message);
    }
}
