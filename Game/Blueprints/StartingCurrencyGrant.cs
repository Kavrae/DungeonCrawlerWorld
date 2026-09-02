using Engine.ECS.Components;
using Engine.Math;
using Game.Modules.Currency.Components;

namespace Game.Blueprints;

/// <summary>Grants a random 1-10 starting Gold, 0 Credits -- Player, Goblin, and Fairy each call this from their own Build.</summary>
public static class StartingCurrencyGrant
{
    private const int MinimumStartingGold = 1;
    private const int MaximumStartingGold = 10;

    public static void GrantRandomStartingGold(ComponentManager componentManager, int entityId, MathUtility mathUtility) =>
        componentManager.Merge(entityId, new CurrencyComponent(mathUtility.Next(MinimumStartingGold, MaximumStartingGold + 1), credits: 0));
}
