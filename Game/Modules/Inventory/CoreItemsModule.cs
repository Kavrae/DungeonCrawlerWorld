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
    public static readonly Guid ManaPotionId = new("a4f2e8c1-6b3d-4e97-9a1f-2d8c5b7e4a63");

    private const int ConsumableMaximumStackSize = 999;

    public void Configure(GameModuleContext context)
    {
        context.Items.Register(new ItemDefinition(
            HealthPotionId, "Health Potion", "HealthPotion", "h", Color.Green,
            Tags: [Tag.Potion, Tag.Consumable, Tag.Healing],
            Description: "Increases your health by at least 50%. Doesn't cure poison or other health-seeping conditions such as succubus-inflicted gonorrhea. So remember to wrap it up, bucko.",
            Summary: "Heal target(s) by 50%.",
            MaxStackSize: ConsumableMaximumStackSize,
            Consumable: new ConsumableEffect(
                ConsumableKind.Potion,
                HealFraction: 0.5f,
                Targeting: new TargetingSpec(Shape: TargetShape.Burst, Range: 3, AreaSize: 1),
                ActionLockFrames: (short)GameTiming.FramesForSeconds(1f))));

        context.Items.Register(new ItemDefinition(
            ManaPotionId, "Regular Mana Potion", "HealthPotion", "m", Color.Blue,
            Tags: [Tag.Potion, Tag.Consumable],
            Description: "Fully restores the target(s) mana. Oddly tastes like TV static.",
            Summary: "Restore target's mana.",
            MaxStackSize: ConsumableMaximumStackSize,
            Consumable: new ConsumableEffect(
                ConsumableKind.Potion,
                HealFraction: 0f,
                Targeting: new TargetingSpec(Shape: TargetShape.Burst, Range: 3, AreaSize: 1),
                ActionLockFrames: (short)GameTiming.FramesForSeconds(1f),
                ManaFraction: 1f)));
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
