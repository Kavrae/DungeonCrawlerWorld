using Game.Modules.Abilities;
using Game.World;

namespace Game.Modules.Achievements.Definitions;

/// <summary>
/// Awarded the first time the player activates an ability that reads as a spell (a buff, debuff,
/// or other magic effect) rather than a mundane physical attack. AbilityDefinition has no Tags
/// field yet (see ItemDefinition.Tags, which items already have -- TODO.md's "Tag abilities"
/// item), so there's no real "Spell" tag to check today -- SpellAbilityIds below is a temporary
/// hardcoded stand-in listing the two abilities that currently read as spells (QuickCastTestModule's
/// own two), the same "make it earnable today with an honest, clearly-marked stopgap" reasoning
/// UnarmedCombatAchievement/LonerAchievement/EmptyPocketsAchievement already use for their own
/// unconditional conditions.
///
/// Revisit once AbilityDefinition.Tags exists: replace SpellAbilityIds/the Contains check below
/// with a real ability.Tags.Contains("Spell") check (looked up via AbilityCatalog, not the raw
/// AbilityId), so this covers every Spell-tagged ability automatically -- including the planned
/// starter self-heal spell (see TODO.md's "Self heal ability" item), which should make this
/// trivially easy to earn once it exists.
/// </summary>
public sealed class SpellCasterAchievement : IAchievementDefinition
{
    private static readonly HashSet<Guid> SpellAbilityIds =
    [
        QuickCastTestModule.QuickCastAbilityId,
        QuickCastTestModule.RangedTestDebuffAbilityId
    ];

    public Guid Id { get; } = new("3a1f8c2e-9d4b-47a6-8e2f-000000000009");

    public string Name => "You're a wizard, <copyright warning>";

    public string RequirementText => "Activated your first spell.";

    public string Description =>
        "You cast your first spell! Lets hope it's not your last.";

    public LootboxReward? Lootbox => null;

    public string RewardText => "";

    public void RegisterTrigger(AchievementTriggerContext context) =>
        context.SubscribeUntilUnlocked<AbilityActivated>(activated =>
            activated.EntityId == context.PlayerQuery!.PlayerEntityId
            && SpellAbilityIds.Contains(activated.AbilityId));
}
