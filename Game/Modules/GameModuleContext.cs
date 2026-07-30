using Engine.Events;
using Engine.Math;
using Game.Modules.Abilities;
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

    /// <summary>
    /// Shared across every module's Configure call within one GameBootstrapper.Build (or one
    /// DryRunValidateMods trial) -- a fresh registry per GameModuleContext instance, so the
    /// dry-run trial's registrations never leak into the real build's. See
    /// StatusEffectAuraApplierRegistry's own doc comment for why registering here (during
    /// Configure) rather than in RegisterComponents/RegisterSystems is what makes ordering safe.
    /// </summary>
    public StatusEffectAuraApplierRegistry StatusEffectAuraAppliers { get; init; } = new();

    /// <summary>Shared across every module's Configure call within one build -- same reasoning as StatusEffectAuraAppliers above.</summary>
    public AbilityCatalog Abilities { get; init; } = new();
}