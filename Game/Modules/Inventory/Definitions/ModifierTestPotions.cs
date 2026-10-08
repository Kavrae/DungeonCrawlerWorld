using Engine.Math;
using Engine.Utilities;
using Game.Effects;
using Game.Effects.Entries;
using Game.Modules.Actions;
using Game.Modules.Actions.Activators;
using Game.Modules.StatModifiers;
using Game.Tags;
using Microsoft.Xna.Framework;

namespace Game.Modules.Inventory.Definitions;

/// <summary>TEMPORARY test content: one potion per amount an effect entry now scales through stat modifiers (EffectModifiers), each a two-minute buff on whoever drinks it.</summary>
/// <remarks>
/// Plenty doubles mana restored to you (a Mana Potion), Thrift halves your mana costs (spells, Toxic
/// Aura's upkeep), Venom doubles the status stacks you apply (a Toxic Potion, Toxic Strike), and
/// Radiance doubles the power and size of auras you grant (Scroll of Torch, the
/// Lantern). Remove once real content grants these modifiers.
/// </remarks>
public static class ModifierTestPotions
{
    public static readonly Guid PlentyId = new("f1a4c7e2-3b9d-4e6f-8a2c-000000000041");
    public static readonly Guid ThriftId = new("f1a4c7e2-3b9d-4e6f-8a2c-000000000042");
    public static readonly Guid VenomId = new("f1a4c7e2-3b9d-4e6f-8a2c-000000000043");
    public static readonly Guid RadianceId = new("f1a4c7e2-3b9d-4e6f-8a2c-000000000044");

    private static readonly ushort DurationFrames = GameTiming.FramesForSeconds(120f);

    private static StatModifierGrant Modifier(StatModifierTarget target, StatModifierOperation operation, float magnitude) =>
        new(target, operation, StatModifierPolarity.Buff, CanModify: false, Magnitude: magnitude, DurationFrames: DurationFrames);

    private static ItemDefinition Potion(Guid id, string name, string glyph, Color color, string description, string summary, params StatModifierGrant[] modifiers) => new(
        id, name, "HealthPotion", glyph, color,
        Tags: [GameTags.TargetingSelf],
        Effects: [new Effect([.. modifiers])],
        Description: description,
        Summary: summary,
        GoldValue: 5,
        Activator: new PotionActivator(
            new TargetingSpec(Shape: TargetShape.Burst, Range: 3, AreaSize: 1, TargetModeAffects: TargetModeAffects.MarkedOnly),
            new ActionTiming(ActionTimingCategory.Immediate, CooldownFrames: null)));

    public static ItemDefinition BuildPlenty() => Potion(PlentyId, "Draught of Plenty", "1", Color.CornflowerBlue,
        "A test draught: mana restored to you is doubled for two minutes.", "Doubles mana restored to you for 2 minutes.",
        Modifier(StatModifierTarget.IncomingManaRestore, StatModifierOperation.Multiplicative, 1f));

    public static ItemDefinition BuildThrift() => Potion(ThriftId, "Draught of Thrift", "2", Color.SteelBlue,
        "A test draught: your mana costs, spells included, are halved for two minutes.", "Halves your mana costs for 2 minutes.",
        Modifier(StatModifierTarget.IncomingManaDrain, StatModifierOperation.Multiplicative, -0.5f));

    public static ItemDefinition BuildVenom() => Potion(VenomId, "Draught of Venom", "3", Color.MediumPurple,
        "A test draught: the status effect stacks you apply are doubled for two minutes.", "Doubles the status stacks you apply for 2 minutes.",
        Modifier(StatModifierTarget.OutgoingStatusStacks, StatModifierOperation.Multiplicative, 1f));

    public static ItemDefinition BuildRadiance() => Potion(RadianceId, "Draught of Radiance", "4", Color.LightYellow,
        "A test draught: auras you grant are twice as strong and twice the size for two minutes.", "Doubles the power and size of auras you grant for 2 minutes.",
        Modifier(StatModifierTarget.OutgoingAuraPower, StatModifierOperation.Multiplicative, 1f),
        Modifier(StatModifierTarget.OutgoingAuraSize, StatModifierOperation.Multiplicative, 1f));
}
