namespace Game.Modules.StatusEffects;

/// <summary>Every registered status effect's IStatusEffectApplier, looked up by StatusEffectType.</summary>
/// <remarks>
/// Filled during IGameModule.Configure by each effect module. Every Configure completes before any
/// RegisterBehavior runs, so a system built in RegisterBehavior sees every effect whatever the module
/// order, and needs to know only that an effect can be looked up, never which effects exist.
/// </remarks>
public sealed class StatusEffectApplierRegistry
{
    private readonly Dictionary<StatusEffectType, IStatusEffectApplier> _appliersByEffectType = [];

    public void Register(IStatusEffectApplier applier) => _appliersByEffectType[applier.EffectType] = applier;

    public bool TryGet(StatusEffectType effectType, out IStatusEffectApplier applier) =>
        _appliersByEffectType.TryGetValue(effectType, out applier!);
}
