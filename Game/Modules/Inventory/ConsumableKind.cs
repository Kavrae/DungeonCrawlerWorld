namespace Game.Modules.Inventory;

/// <summary>
/// Which activation rules a ConsumableEffect follows -- ConsumableActivationSystem switches on
/// this. Potion is the only kind today (global PotionCooldown, splash-throw-or-self-drink); a
/// future kind like a user-only-targeting Bandage would add its own case rather than Potion's
/// rules growing conditionals for behavior that isn't actually shared.
/// </summary>
public enum ConsumableKind
{
    Potion,
}
