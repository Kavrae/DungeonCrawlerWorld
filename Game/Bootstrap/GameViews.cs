using Game.Modules;
using Game.Modules.Actions;
using Game.Modules.Core.Components;
using Game.Modules.Health;
using Game.Modules.ProcessingTier;
using Game.Spawning;
using Game.Views;

namespace Game.Bootstrap;

/// <summary>The read-only views over a game session's state that everything outside Game reads it through.</summary>
public sealed class GameViews
{
    public GameViews(World.World world, GameModuleContext context, LocalTierRoster localTierRoster)
    {
        var componentManager = context.ComponentManager;

        MapView = new MapViewQuery(world, componentManager, context.Actions, context.Terrain, context.Definitions, context.SimulationClock, context.Items);
        EntityBodyParts = EntityBodyParts.For(componentManager, context.Definitions);
        EntityActions = EntityActions.For(componentManager, context.Actions, context.Definitions);
        EntityNaming = EntityNaming.For(componentManager, context.Definitions);
        PlayerActionGate = new PlayerActionGate(componentManager.GetPackedPool<ActionLockComponent>(), context.EffectServices.AbilityScores, context.EffectServices.StatModifiers, world, context.SimulationClock);
        InventoryView = new InventoryView(componentManager, context.Items);
        ShopView = new ShopView(componentManager);
        CurrencyView = new CurrencyView(componentManager);
        HotkeyBindingView = new HotkeyBindingView(componentManager);
        HealthView = new HealthView(componentManager, EntityBodyParts);
        StatModifierView = new StatModifierView(componentManager);
        AbilityScoreView = new AbilityScoreView(componentManager);
        ActionStateView = new ActionStateView(componentManager, EntityActions, context.Items, localTierRoster, context.EffectServices, context.Toggles, context.SimulationClock);
        TargetingView = new TargetingView(context.TargetResolution, EntityActions, context.Items, componentManager);
        TransformView = new TransformView(componentManager);
        AuraPower = new AuraPowerView(context.AuraField, context.Auras);
        ActionSourceNaming = new ActionSourceNaming(context.Terrain, context.Auras);
        ContentSpriteNames = new ContentSpriteNames(context.Definitions, context.Terrain, context.Actions, context.Items, context.Lootboxes);
    }

    public IMapViewQuery MapView { get; }

    /// <summary>Every sprite name the content declares, for Presentation to check against the manifest it loaded.</summary>
    public ContentSpriteNames ContentSpriteNames { get; }

    public EntityBodyParts EntityBodyParts { get; }

    public EntityActions EntityActions { get; }

    public EntityNaming EntityNaming { get; }

    public PlayerActionGate PlayerActionGate { get; }

    public InventoryView InventoryView { get; }

    public ShopView ShopView { get; }

    public CurrencyView CurrencyView { get; }

    public HotkeyBindingView HotkeyBindingView { get; }

    public HealthView HealthView { get; }

    public StatModifierView StatModifierView { get; }

    public AbilityScoreView AbilityScoreView { get; }

    public ActionStateView ActionStateView { get; }

    /// <summary>Targeting selections, previews and windup landings, all through the resolution activations use.</summary>
    public TargetingView TargetingView { get; }

    public TransformView TransformView { get; }

    /// <summary>Where auras reach on the map and how strongly.</summary>
    public AuraPowerView AuraPower { get; }

    /// <summary>The name shown for what caused an effect: an entity, a terrain, an aura.</summary>
    public ActionSourceNaming ActionSourceNaming { get; }
}
