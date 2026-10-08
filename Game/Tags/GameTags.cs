using Engine.Tags;

namespace Game.Tags;

/// <summary>Every built-in gameplay tag, declared by CoreModule.</summary>
/// <remarks>
/// A parent is only what every child always is (Unarmed is always Melee). A property that can apply to many unrelated
/// things is its own top-level tag, combined rather than nested: a fireball's hit is Damage.Fire plus Magic, a torch's
/// Damage.Fire alone.
/// </remarks>
/// <cleanupVersion>1</cleanupVersion>
public static class GameTags
{
    public static readonly GameplayTag ActionAttack = GameplayTag.Get("Action.Attack");
    public static readonly GameplayTag ActionSpell = GameplayTag.Get("Action.Spell");

    public static readonly GameplayTag EffectHealing = GameplayTag.Get("Effect.Healing");

    public static readonly GameplayTag DeliveryMelee = GameplayTag.Get("Delivery.Melee");
    public static readonly GameplayTag DeliveryMeleeUnarmed = GameplayTag.Get("Delivery.Melee.Unarmed");
    public static readonly GameplayTag DeliveryRanged = GameplayTag.Get("Delivery.Ranged");

    public static readonly GameplayTag TargetingSelf = GameplayTag.Get("Targeting.Self");

    public static readonly GameplayTag Item = GameplayTag.Get("Item");
    public static readonly GameplayTag ItemConsumable = GameplayTag.Get("Item.Consumable");
    public static readonly GameplayTag ItemConsumablePotion = GameplayTag.Get("Item.Consumable.Potion");
    public static readonly GameplayTag ItemConsumableScroll = GameplayTag.Get("Item.Consumable.Scroll");
    public static readonly GameplayTag ItemWand = GameplayTag.Get("Item.Wand");
    public static readonly GameplayTag ItemLootbox = GameplayTag.Get("Item.Lootbox");
    public static readonly GameplayTag ItemToggle = GameplayTag.Get("Item.Toggle");

    public static readonly GameplayTag Damage = GameplayTag.Get("Damage");
    public static readonly GameplayTag DamageFire = GameplayTag.Get("Damage.Fire");
    public static readonly GameplayTag DamagePoison = GameplayTag.Get("Damage.Poison");
    public static readonly GameplayTag DamageEnergy = GameplayTag.Get("Damage.Energy");

    public static readonly GameplayTag Magic = GameplayTag.Get("Magic");

    public static readonly GameplayTag TraitDodgeable = GameplayTag.Get("Trait.Dodgeable");
    public static readonly GameplayTag TraitStaggering = GameplayTag.Get("Trait.Staggering");

    public static readonly GameplayTag StatsAbilityScore = GameplayTag.Get("Stats.AbilityScore");
    public static readonly GameplayTag StatsAbilityScoreStrength = GameplayTag.Get("Stats.AbilityScore.Strength");
    public static readonly GameplayTag StatsAbilityScoreIntelligence = GameplayTag.Get("Stats.AbilityScore.Intelligence");
    public static readonly GameplayTag StatsAbilityScoreConstitution = GameplayTag.Get("Stats.AbilityScore.Constitution");
    public static readonly GameplayTag StatsAbilityScoreDexterity = GameplayTag.Get("Stats.AbilityScore.Dexterity");
    public static readonly GameplayTag StatsAbilityScoreCharisma = GameplayTag.Get("Stats.AbilityScore.Charisma");
    public static readonly GameplayTag StatsAbilityScoreLuck = GameplayTag.Get("Stats.AbilityScore.Luck");
    public static readonly GameplayTag StatsAbilityScoreWisdom = GameplayTag.Get("Stats.AbilityScore.Wisdom");

    /// <summary>Every tag above, for CoreModule to declare.</summary>
    public static readonly IReadOnlyList<GameplayTag> All =
    [
        ActionAttack, ActionSpell,
        EffectHealing,
        DeliveryMelee, DeliveryMeleeUnarmed, DeliveryRanged,
        TargetingSelf,
        Item, ItemConsumable, ItemConsumablePotion, ItemConsumableScroll, ItemWand, ItemLootbox, ItemToggle,
        Damage, DamageFire, DamagePoison, DamageEnergy,
        Magic,
        TraitDodgeable, TraitStaggering,
        StatsAbilityScore, StatsAbilityScoreStrength, StatsAbilityScoreIntelligence, StatsAbilityScoreConstitution, StatsAbilityScoreDexterity, StatsAbilityScoreCharisma, StatsAbilityScoreLuck, StatsAbilityScoreWisdom,
    ];
}
