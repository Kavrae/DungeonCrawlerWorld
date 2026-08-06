using Engine.ECS.Components;
using Engine.ECS.Systems;
using Engine.Math;
using Engine.Utilities;
using Microsoft.Xna.Framework;

namespace Game.Modules.Inventory;

/// <summary>
/// Registers the first real, permanent item catalog -- race/class-agnostic items any entity can
/// carry. See PlayerBlueprint for where these are granted.
/// </summary>
public sealed class CoreItemsModule : IGameModule
{
    public Guid Id { get; } = new("d9f6a1c4-8b2e-4f3a-9c1d-000000000011");

    public IReadOnlyList<Type> Dependencies { get; } = [];

    public static readonly Guid HealthPotionId = new("7c3e9a1d-4b6f-4e2a-8d1c-000000000001");

    private const int ConsumableMaximumStackSize = 999;

    private const string HealthPotionSummary = "Heal target(s) by 50%.";

    public void Configure(GameModuleContext context)
    {
        context.Items.Register(new ItemDefinition(
            HealthPotionId, "Health Potion", "HealthPotion", "h", Color.Green,
            Tags: [Tag.Potion, Tag.Consumable, Tag.Healing],
            Description: "Increases your health by at least 50%. Doesn't cure poison or other health-seeping conditions such as succubus-inflicted gonorrhea. So remember to wrap it up, bucko.",
            Summary: HealthPotionSummary,
            MaxStackSize: ConsumableMaximumStackSize,
            Consumable: new ConsumableEffect(
                ConsumableKind.Potion,
                HealFraction: 0.5f,
                Targeting: new TargetingSpec(Shape: TargetShape.Burst, Range: 3, AreaSize: 1),
                ActionLockFrames: (short)GameTiming.FramesForSeconds(1f))));
    }

    public void RegisterComponents(ComponentManager componentManager)
    {
        // No components of its own -- see class doc comment.
    }

    public void RegisterSystems(SystemManager systemManager, ComponentManager componentManager)
    {
        // No systems of its own -- see class doc comment.
    }
}
