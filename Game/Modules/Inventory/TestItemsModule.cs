using Engine.ECS.Components;
using Engine.ECS.Systems;
using Engine.Math;
using Engine.Utilities;
using Microsoft.Xna.Framework;

namespace Game.Modules.Inventory;

/// <summary>
/// TEMPORARY: registers two hardcoded item definitions purely to exercise the inventory storage
/// + viewing pipeline while real item authoring/content doesn't exist yet -- not real game
/// content. Remove once a real item catalog/authoring path supersedes it. See PlayerBlueprint
/// for where these are granted.
/// </summary>
public sealed class TestItemsModule : IGameModule
{
    public Guid Id { get; } = new("d9f6a1c4-8b2e-4f3a-9c1d-000000000011");

    public IReadOnlyList<Type> Dependencies { get; } = [];

    public static readonly Guid HealthPotionId = new("7c3e9a1d-4b6f-4e2a-8d1c-000000000001");
    public static readonly Guid HammerId = new("7c3e9a1d-4b6f-4e2a-8d1c-000000000002");

    private const float HealthPotionHealFraction = 0.5f;
    private const int HealthPotionMaxStackSize = 999;
    private const int HealthPotionSplashRange = 3;
    private const int HealthPotionSplashAreaSize = 1;
    private static readonly short HealthPotionActionLockFrames = (short)GameTiming.FramesForSeconds(1f);

    private const string HealthPotionDescription =
        "Increases your health by at least 50%. Doesn't cure poison or other health-seeping " +
        "conditions such as succubus-inflicted gonorrhea. So remember to wrap it up, bucko.";

    private const string HealthPotionSummary = "Heal target(s) by 50%.";
    private const string HammerSummary = "Bonk";

    public void Configure(GameModuleContext context)
    {
        context.Items.Register(new ItemDefinition(
            HealthPotionId, "Health Potion", "HealthPotion", "h", Color.Green,
            Tags: ["Potion", "Consumable", "Healing"],
            Description: HealthPotionDescription,
            Summary: HealthPotionSummary,
            MaxStackSize: HealthPotionMaxStackSize,
            Consumable: new ConsumableEffect(
                ConsumableKind.Potion,
                HealFraction: HealthPotionHealFraction,
                Targeting: new TargetingSpec(TargetShape.Burst, HealthPotionSplashRange, HealthPotionSplashAreaSize),
                ActionLockFrames: HealthPotionActionLockFrames)));

        context.Items.Register(new ItemDefinition(HammerId, "Hammer", "Hammer", "h", Color.Gray, Tags: ["Equipment", "Tool"], Summary: HammerSummary));
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
