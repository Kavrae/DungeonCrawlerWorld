using Engine.Tags;

namespace Game.Tags;

/// <summary>Every built-in gameplay tag of an activation -- what a use of something is -- declared, with its roots, by CoreModule.</summary>
/// <remarks>
/// A parent is only what every child always is (Unarmed is always Melee). A property that can apply to many unrelated
/// things is its own top-level tag, combined rather than nested: a fireball's hit is Damage.Fire plus Magic, a torch's
/// Damage.Fire alone. Core owns these roots, rather than the modules that read them, because content any build decodes
/// names them -- the null-zone auras around the starting area's shrines disable Spell and Item.Consumable.Scroll -- and
/// an owner missing from a build would leave those tags undeclared. Area tags are the Areas module's (AreaTags).
/// </remarks>
/// <cleanupVersion>1</cleanupVersion>
public static class GameTags
{
    public static readonly GameplayTag Action = GameplayTag.Get("Action");
    public static readonly GameplayTag ActionAttack = GameplayTag.Get("Action.Attack");

    /// <summary>Casting a spell: a spell action, or reading a scroll of one.</summary>
    public static readonly GameplayTag Spell = GameplayTag.Get("Spell");

    public static readonly GameplayTag Effect = GameplayTag.Get("Effect");
    public static readonly GameplayTag EffectHealing = GameplayTag.Get("Effect.Healing");

    public static readonly GameplayTag Delivery = GameplayTag.Get("Delivery");
    public static readonly GameplayTag DeliveryMelee = GameplayTag.Get("Delivery.Melee");
    public static readonly GameplayTag DeliveryMeleeUnarmed = GameplayTag.Get("Delivery.Melee.Unarmed");
    public static readonly GameplayTag DeliveryRanged = GameplayTag.Get("Delivery.Ranged");

    public static readonly GameplayTag Targeting = GameplayTag.Get("Targeting");
    public static readonly GameplayTag TargetingSelf = GameplayTag.Get("Targeting.Self");

    public static readonly GameplayTag Item = GameplayTag.Get("Item");
    public static readonly GameplayTag ItemConsumable = GameplayTag.Get("Item.Consumable");
    public static readonly GameplayTag ItemConsumablePotion = GameplayTag.Get("Item.Consumable.Potion");
    public static readonly GameplayTag ItemConsumableScroll = GameplayTag.Get("Item.Consumable.Scroll");
    public static readonly GameplayTag ItemConsumableTome = GameplayTag.Get("Item.Consumable.Tome");
    public static readonly GameplayTag ItemWand = GameplayTag.Get("Item.Wand");
    public static readonly GameplayTag ItemLootbox = GameplayTag.Get("Item.Lootbox");
    public static readonly GameplayTag ItemToggle = GameplayTag.Get("Item.Toggle");

    public static readonly GameplayTag Damage = GameplayTag.Get("Damage");
    public static readonly GameplayTag DamageFire = GameplayTag.Get("Damage.Fire");
    public static readonly GameplayTag DamagePoison = GameplayTag.Get("Damage.Poison");
    public static readonly GameplayTag DamageEnergy = GameplayTag.Get("Damage.Energy");

    public static readonly GameplayTag Magic = GameplayTag.Get("Magic");

    public static readonly GameplayTag Trait = GameplayTag.Get("Trait");
    public static readonly GameplayTag TraitDodgeable = GameplayTag.Get("Trait.Dodgeable");
    public static readonly GameplayTag TraitStaggering = GameplayTag.Get("Trait.Staggering");

    public static readonly GameplayTag Stats = GameplayTag.Get("Stats");
    public static readonly GameplayTag StatsAbilityScore = GameplayTag.Get("Stats.AbilityScore");
    public static readonly GameplayTag StatsAbilityScoreStrength = GameplayTag.Get("Stats.AbilityScore.Strength");
    public static readonly GameplayTag StatsAbilityScoreIntelligence = GameplayTag.Get("Stats.AbilityScore.Intelligence");
    public static readonly GameplayTag StatsAbilityScoreConstitution = GameplayTag.Get("Stats.AbilityScore.Constitution");
    public static readonly GameplayTag StatsAbilityScoreDexterity = GameplayTag.Get("Stats.AbilityScore.Dexterity");
    public static readonly GameplayTag StatsAbilityScoreCharisma = GameplayTag.Get("Stats.AbilityScore.Charisma");
    public static readonly GameplayTag StatsAbilityScoreLuck = GameplayTag.Get("Stats.AbilityScore.Luck");
    public static readonly GameplayTag StatsAbilityScoreWisdom = GameplayTag.Get("Stats.AbilityScore.Wisdom");

    /// <summary>The roots CoreModule owns, each with what it is for and whether other modules may declare tags under it.</summary>
    public static readonly IReadOnlyList<(GameplayTag Root, string Description, bool Extensible)> Roots =
    [
        (Action, "What kind of thing an action does.", false),
        (Spell, "Casting a spell: a spell action, or reading a scroll of one.", false),
        (Effect, "What kind of effect a use applies.", true),
        (Delivery, "How a use reaches its target: in melee or at range.", false),
        (Targeting, "Who or what a use is aimed at.", false),
        (Item, "An item's category; each category is an inventory tab.", true),
        (Damage, "The kind of damage a use deals.", true),
        (Magic, "Done by magic: a spell, a scroll or a wand.", false),
        (Trait, "A property of a use that other rules react to.", true),
        (Stats, "The statistics a use draws on.", false),
    ];

    /// <summary>Every built-in tag below a root, with what it means.</summary>
    public static readonly IReadOnlyList<(GameplayTag Tag, string Description)> Tags =
    [
        (ActionAttack, "An attack: it is meant to hurt its target."),
        (EffectHealing, "Restores health."),
        (DeliveryMelee, "Reaches only what stands beside its user."),
        (DeliveryMeleeUnarmed, "A melee use made without a weapon."),
        (DeliveryRanged, "Reaches its target from a distance."),
        (TargetingSelf, "Aimed at its user."),
        (ItemConsumable, "Used up when it is used."),
        (ItemConsumablePotion, "A potion: drunk, and gone."),
        (ItemConsumableScroll, "A scroll: read to cast the spell written on it, and gone."),
        (ItemConsumableTome, "A tome: read to learn what it teaches, and gone."),
        (ItemWand, "A wand: casts its spell for a charge."),
        (ItemLootbox, "A loot box: opened for what is inside."),
        (ItemToggle, "Switched on and off, holding its effects while on."),
        (DamageFire, "Fire damage."),
        (DamagePoison, "Poison damage."),
        (DamageEnergy, "Raw magical energy."),
        (TraitDodgeable, "Can be dodged when it lands."),
        (TraitStaggering, "Staggers what it hits, interrupting its windup."),
        (StatsAbilityScore, "Scales with one of its user's ability scores."),
        (StatsAbilityScoreStrength, "Scales with its user's Strength."),
        (StatsAbilityScoreIntelligence, "Scales with its user's Intelligence."),
        (StatsAbilityScoreConstitution, "Scales with its user's Constitution."),
        (StatsAbilityScoreDexterity, "Scales with its user's Dexterity."),
        (StatsAbilityScoreCharisma, "Scales with its user's Charisma."),
        (StatsAbilityScoreLuck, "Scales with its user's Luck."),
        (StatsAbilityScoreWisdom, "Scales with its user's Wisdom."),
    ];

    /// <summary>Every tag above, roots first.</summary>
    public static readonly IReadOnlyList<GameplayTag> All = [.. Roots.Select(root => root.Root), .. Tags.Select(entry => entry.Tag)];
}
