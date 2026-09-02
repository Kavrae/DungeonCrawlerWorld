using Engine.ECS.Components;
using Engine.Math;
using Game.Modules.Core.Components;

namespace Game.Blueprints.Objects;

/// <summary>
/// Shop composed with GeneralShopStock -- the same composition-chain shape GoblinEngineerBlueprint
/// uses for race+class, applied here to shell+stock. See PotionShop's own doc comment for why the
/// rename to "General Shop" uses TryUpdate rather than another DisplayTextComponent Merge.
/// </summary>
public sealed class GeneralShop(MathUtility mathUtility) : IBlueprint
{
    private const string DisplayName = "General Shop";

    private readonly CompositeBlueprint _composite = new(
        [new Shop(), new GeneralShopStock(mathUtility)],
        static (componentManager, entityId) =>
            componentManager.TryUpdate(entityId, static (ref DisplayTextComponent displayText) => displayText.Name = DisplayName));

    public void Build(ComponentManager componentManager, int entityId) => _composite.Build(componentManager, entityId);
}
