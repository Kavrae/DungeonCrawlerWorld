using Engine.ECS.Components;
using Engine.Math;
using Game.Modules.Currency.Components;

namespace Game.Blueprints;

/// <summary>Grants random starting Currency -- Player calls the Gold-only overload; Goblin and Fairy call the Gold+Credits overload (see each method's own doc comment).</summary>
public static class StartingCurrencyGrant
{
    private const int MinimumStartingGold = 1;
    private const int MaximumStartingGold = 10;
    private const int MinimumStartingCredits = 0;
    private const int MaximumStartingCredits = 1;

    /// <summary>1-10 Gold, 0 Credits -- Player only.</summary>
    public static void GrantRandomStartingGold(ComponentManager componentManager, int entityId, MathUtility mathUtility) =>
        componentManager.Merge(entityId, new CurrencyComponent(mathUtility.Next(MinimumStartingGold, MaximumStartingGold + 1), credits: 0));

    /// <summary>
    /// 1-10 Gold and 0-1 Credits (Credits is an extremely rare currency, see CurrencyComponent's
    /// own doc comment) -- Goblin and Fairy. One Merge call for both fields together, never two
    /// sequential grants on the same entity: CurrencyModule's registered merge policy is a full
    /// overwrite, so a second call would silently zero out whatever the first one just set.
    /// </summary>
    public static void GrantRandomStartingGoldAndCredits(ComponentManager componentManager, int entityId, MathUtility mathUtility) =>
        componentManager.Merge(entityId, new CurrencyComponent(
            mathUtility.Next(MinimumStartingGold, MaximumStartingGold + 1),
            mathUtility.Next(MinimumStartingCredits, MaximumStartingCredits + 1)));
}
