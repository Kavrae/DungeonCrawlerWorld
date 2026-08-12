using Engine.ECS.Systems;
using Engine.Events;
using Engine.Math;
using Game.Modules.Achievements;
using Game.Modules.Actions;
using Game.Modules.Inventory;
using Game.Modules.ProcessingTier;
using Game.Modules.StatusEffects;
using Game.World;

namespace Game.Modules;

/// <summary>
/// A record, not positional Configure parameters, so a future fourth piece of context is a
/// new property rather than a signature break for every module already written against
/// IGameModule.Configure -- PlayerQuery below is exactly that: an optional init property, not
/// a fourth positional parameter, so existing test call sites didn't need to change.
/// </summary>
public sealed record GameModuleContext(IMapQuery MapQuery, MathUtility MathUtility, EventBus EventBus)
{
    public IPlayerQuery? PlayerQuery { get; init; }

    /// <summary>Mandatory map-occupancy sync MovementModule wires into MovementSystem -- nullable like PlayerQuery, but MovementModule.RegisterSystems throws if this is still null when it actually tries to construct MovementSystem, since (unlike PlayerQuery) movement genuinely can't work without it.</summary>
    public IEntityMoveSync? EntityMoveSync { get; init; }

    /// <summary>
    /// Shared across every module's Configure call within one GameBootstrapper.Build (or one
    /// DryRunValidateMods trial) -- a fresh registry per GameModuleContext instance, so the
    /// dry-run trial's registrations never leak into the real build's. See
    /// StatusEffectAuraApplierRegistry's own doc comment for why registering here (during
    /// Configure) rather than in RegisterComponents/RegisterSystems is what makes ordering safe.
    /// </summary>
    public StatusEffectAuraApplierRegistry StatusEffectAuraAppliers { get; init; } = new();

    /// <summary>Shared across every module's Configure call within one build -- same reasoning as StatusEffectAuraAppliers above.</summary>
    public ActionCatalog Actions { get; init; } = new();

    /// <summary>Shared across every module's Configure call within one build -- same reasoning as Actions above; a mod could register its own achievements the same way a mod could register its own actions.</summary>
    public AchievementCatalog Achievements { get; init; } = new();

    /// <summary>Shared across every module's Configure call within one build -- same reasoning as Actions/Achievements above; a mod could register its own items the same way.</summary>
    public ItemCatalog Items { get; init; } = new();

    /// <summary>
    /// MovementSystem's confirmed moves this frame, shared with ContactDamageSystem/
    /// StatusEffectAuraSystem so they can react without a per-move EventBus dispatch -- see
    /// FrameEventBuffer's own doc comment. Always a real instance (never null), the same
    /// always-safe-default reasoning as Actions/StatusEffectAuraAppliers above.
    /// </summary>
    public FrameEventBuffer<EntityMovedEvent> MovedEntities { get; init; } = new();

    /// <summary>
    /// Shared across every module's Configure call within one build -- same always-real-default
    /// reasoning as MovedEntities above. Any module can subscribe to TierChanged regardless of
    /// whether ProcessingTierModule has run its own Configure/RegisterSystems yet -- see
    /// ProcessingTierEvents' own doc comment.
    /// </summary>
    public ProcessingTierEvents ProcessingTierEvents { get; init; } = new();
}