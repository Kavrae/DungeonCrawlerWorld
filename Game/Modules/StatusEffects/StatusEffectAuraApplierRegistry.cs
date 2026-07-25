namespace Game.Modules.StatusEffects;

/// <summary>
/// Collects each concrete status-effect module's own IStatusEffectAuraApplier during
/// IGameModule.Configure. Every IGameModule's Configure call completes before any
/// RegisterSystems runs (see GameBootstrapper.Build's ConfigureGameModules-then-Bootstrapper.
/// Build ordering), so by the time StatusEffectAuraSystem is constructed (in
/// StatusEffectAuraModule.RegisterSystems), every effect registered here -- regardless of
/// Configure call order -- is available. This is what lets StatusEffectAuraModule depend only
/// on StatusEffectsModule again, instead of a concrete effect module: it never needs to know
/// which effects exist, only that whichever ones registered themselves can be looked up by
/// StatusEffectType.
/// </summary>
public sealed class StatusEffectAuraApplierRegistry
{
    private readonly Dictionary<StatusEffectType, IStatusEffectAuraApplier> _appliersByEffectType = [];

    public void Register(IStatusEffectAuraApplier applier) => _appliersByEffectType[applier.EffectType] = applier;

    public bool TryGet(StatusEffectType effectType, out IStatusEffectAuraApplier applier) =>
        _appliersByEffectType.TryGetValue(effectType, out applier!);
}
