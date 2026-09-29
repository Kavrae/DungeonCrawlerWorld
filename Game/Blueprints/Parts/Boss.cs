using Engine.ECS.Systems;
using Game.Modules.Health;
using Game.Modules.Lootboxes;
using Game.Modules.StatModifiers;
using Game.World;

namespace Game.Blueprints.Parts;

/// <summary>Makes whatever it is built onto a boss: double its maximum health, "Boss" after its name, and a Bronze Boss loot box for the player who kills it.</summary>
/// <remarks>A trait with no race or class of its own, included after the blueprint it strengthens (see GoblinForeman), so the health it doubles is already there.</remarks>
public static class Boss
{
    public static readonly Guid Id = new("d9f6a1c4-8b2e-4f3a-9c1d-000000000115");

    public const string Name = "Boss";

    private const float MaximumHealthBonusMultiplier = 1f;

    public static readonly BlueprintDefinition Definition = new(Id, Name)
    {
        Build = Build,
        Appearance = new() { NameSuffix = Name },
        Lootbox = new LootboxReward(LootboxTypes.Boss.Id, LootboxRarity.Bronze),
    };

    private static void Build(BlueprintContext context)
    {
        var source = ActionSource.FromEntity(context.ComponentManager, context.EntityKeys, context.EntityId, context.Definitions);

        MaximumHealthShift.ApplyModifier(context.ComponentManager, context.Definitions, context.EntityId, StatModifierOperation.Multiplicative, StatModifierPolarity.Buff,
            canModify: true, magnitude: MaximumHealthBonusMultiplier, expiresAtFrame: FrameDeadline.Never, source);
    }
}
