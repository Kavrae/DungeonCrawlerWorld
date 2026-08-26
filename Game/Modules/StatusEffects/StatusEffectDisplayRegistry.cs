namespace Game.Modules.StatusEffects;

/// <summary>
/// Collects each concrete status-effect module's own IStatusEffectDisplay during
/// IGameModule.Configure. Every IGameModule's Configure call completes before any
/// RegisterSystems runs (see GameBootstrapper.Build's ConfigureGameModules-then-Bootstrapper.
/// Build ordering), so by the time a display consumer (HealthWindow, PlayerStatusEffectsContent)
/// is constructed, every effect registered here -- regardless of Configure call order -- is
/// available. This is what lets those consumers depend only on StatusEffectsModule again,
/// instead of a concrete effect module: they never need to know which effects exist, only that
/// whichever ones registered themselves can be looked up by StatusEffectType.
/// </summary>
public sealed class StatusEffectDisplayRegistry
{
    private readonly Dictionary<StatusEffectType, IStatusEffectDisplay> _displaysByEffectType = [];

    public void Register(IStatusEffectDisplay display) => _displaysByEffectType[display.EffectType] = display;

    public bool TryGet(StatusEffectType effectType, out IStatusEffectDisplay display) =>
        _displaysByEffectType.TryGetValue(effectType, out display!);
}
