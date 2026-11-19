using Engine.ECS.Properties;

namespace Game.Modules.Currency.Components;

/// <summary>
/// An entity's spendable currency balances. Gold is the common currency shops will accept;
/// Credits is an extremely rare currency reserved for late-game features, no consumer yet.
/// </summary>
[DescribedComponent]
public partial struct CurrencyComponent(int gold, int credits)
{
    public int Gold { get; set; } = gold;
    public int Credits { get; set; } = credits;
}
