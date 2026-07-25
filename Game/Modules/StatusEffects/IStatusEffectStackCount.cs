namespace Game.Modules.StatusEffects;

/// <summary>
/// Implemented by a status effect's own timer/tracking component (BurningTimerComponent,
/// PoisonTimerComponent, ...) that caches how many stacks an entity currently holds. Lets
/// TimerBasedAuraApplier&lt;T&gt; read that count generically, without hardcoding any one
/// effect's own component type.
/// </summary>
public interface IStatusEffectStackCount
{
    int StackCount { get; }
}
