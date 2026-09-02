using Engine.ECS.Components;
using Engine.Math;
using Game.Modules.Core.Components;

namespace Game.Blueprints.Objects;

/// <summary>
/// Shop composed with PotionShopStock -- the same composition-chain shape GoblinEngineerBlueprint
/// uses for race+class, applied here to shell+stock. The override step renames the shared "Shop"
/// shell to "Potion Shop" via TryUpdate, not another DisplayTextComponent Merge -- CoreModule's
/// merge policy concatenates Name/Description across stages (see Shop's own doc comment on why
/// that's usually desirable), which would otherwise read as "Shop Potion Shop" instead of cleanly
/// replacing it, the same TryUpdate-not-Merge override GoblinEngineerBlueprint's own ActionLockComponent
/// step already has to use for the identical reason.
/// </summary>
public sealed class PotionShop(MathUtility mathUtility) : IBlueprint
{
    private const string DisplayName = "Potion Shop";

    private readonly CompositeBlueprint _composite = new(
        [new Shop(), new PotionShopStock(mathUtility)],
        static (componentManager, entityId) =>
            componentManager.TryUpdate(entityId, static (ref DisplayTextComponent displayText) => displayText.Name = DisplayName));

    public void Build(ComponentManager componentManager, int entityId) => _composite.Build(componentManager, entityId);
}
