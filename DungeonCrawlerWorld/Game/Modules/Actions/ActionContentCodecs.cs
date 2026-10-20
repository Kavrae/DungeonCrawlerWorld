using Engine.Content;
using Engine.Tiles;
using Game.Content;
using Game.Effects;
using Game.Modules.Actions.Activators;
using Game.Modules.Actions.Effects;

namespace Game.Modules.Actions;

/// <summary>Codecs for everything Actions owns: ActionDefinition and the specs it holds, every activator, and its own effect entries (Dodge, hotkey expansion); registered by ActionsModule.</summary>
internal static class ActionContentCodecs
{
    public const string ActionsTable = "Actions";

    public static void Register(ContentTypeDeclarations contentTypes)
    {
        contentTypes.RegisterValue(new TargetingSpecCodec());
        contentTypes.RegisterValue(new ActionTimingCodec());
        contentTypes.RegisterValue(new ToggleSpecCodec());
        contentTypes.RegisterValue(new TogglePeriodicEffectsCodec());
        contentTypes.RegisterValue(new ActionDefinitionCodec());

        contentTypes.RegisterPolymorphic<IActionActivator, DirectAction>("Direct", new DirectActionCodec());
        contentTypes.RegisterPolymorphic<IActionActivator, SpellActivator>("Spell", new SpellActivatorCodec());
        contentTypes.RegisterPolymorphic<IActionActivator, PotionActivator>("Potion", new PotionActivatorCodec());
        contentTypes.RegisterPolymorphic<IActionActivator, ScrollActivator>("Scroll", new ScrollActivatorCodec());
        contentTypes.RegisterPolymorphic<IActionActivator, WandActivator>("Wand", new WandActivatorCodec());
        contentTypes.RegisterPolymorphic<IActionActivator, ToggleItemActivator>("ToggleItem", new ToggleItemActivatorCodec());

        contentTypes.RegisterPolymorphic<IEffectEntry, DodgeActivation>(nameof(DodgeActivation), new DodgeActivationCodec());
        contentTypes.RegisterPolymorphic<IEffectEntry, HotkeyExpansionGrant>(nameof(HotkeyExpansionGrant), new HotkeyExpansionGrantCodec());
    }

    private sealed class TargetingSpecCodec : ContentCodec<TargetingSpec>
    {
        public override ContentSchema Schema { get; } = new(nameof(TargetingSpec),
        [
            ContentField.Enum<TargetShape>("sh", "Shape"),
            ContentField.Integer("r", "Range", 0),
            ContentField.Integer("a", "Area size", 0),
            ContentField.Enum<DistanceMetric>("me", "Distance metric"),
            ContentField.Enum<TargetingModes>("mo", "Targeting modes"),
            ContentField.Enum<TargetModeAffects>("ta", "Target mode affects"),
        ]);

        public override void WriteProperties(ContentWriter writer, TargetingSpec value)
        {
            writer.Enum("sh", value.Shape);
            writer.Integer("r", value.Range);
            writer.Integer("a", value.AreaSize);
            writer.Enum("me", value.Metric, DistanceMetric.Manhattan);
            writer.Enum("mo", value.Modes, TargetingModes.Both);
            writer.Enum("ta", value.TargetModeAffects, TargetModeAffects.Area);
        }

        public override TargetingSpec Read(ContentObject element) => new(
            element.Enum<TargetShape>("sh"),
            ContentNumbers.Int32(element.Integer("r"), "r"),
            ContentNumbers.Int32(element.Integer("a"), "a"),
            element.Enum("me", DistanceMetric.Manhattan),
            element.Enum("mo", TargetingModes.Both),
            element.Enum("ta", TargetModeAffects.Area));
    }

    private sealed class ActionTimingCodec : ContentCodec<ActionTiming>
    {
        public override ContentSchema Schema { get; } = new(nameof(ActionTiming),
        [
            ContentField.Enum<ActionTimingCategory>("c", "Category"),
            ContentField.Integer("lf", "Action lock in frames (none: the entity's own)", 0, ushort.MaxValue),
            ContentField.Integer("cd", "Cooldown in frames", 0, ushort.MaxValue),
            ContentField.Boolean("rl", "Releases the action lock"),
        ]);

        public override void WriteProperties(ContentWriter writer, ActionTiming value)
        {
            writer.Enum("c", value.Category);
            writer.OptionalInteger("lf", value.ActionLockFrames);
            writer.OptionalInteger("cd", value.CooldownFrames);
            writer.Boolean("rl", value.ReleasesActionLock);
        }

        public override ActionTiming Read(ContentObject element) => new(
            element.Enum<ActionTimingCategory>("c"),
            ContentNumbers.UInt16(element.OptionalInteger("lf"), "lf"),
            ContentNumbers.UInt16(element.OptionalInteger("cd"), "cd"),
            element.Boolean("rl"));
    }

    /// <summary>A toggle; "{}" is one that only holds its effects (ToggleSpec.HoldsEffectsOnly).</summary>
    private sealed class ToggleSpecCodec : ContentCodec<ToggleSpec>
    {
        public override ContentSchema Schema { get; } = new(nameof(ToggleSpec), [ContentField.Object<TogglePeriodicEffects>("per", "Periodic effects")]);

        public override void WriteProperties(ContentWriter writer, ToggleSpec value) => writer.Object("per", value.Periodic);

        public override ToggleSpec Read(ContentObject element) =>
            element.Object<TogglePeriodicEffects>("per") is { } periodic ? new ToggleSpec(periodic) : ToggleSpec.HoldsEffectsOnly;
    }

    private sealed class TogglePeriodicEffectsCodec : ContentCodec<TogglePeriodicEffects>
    {
        public override ContentSchema Schema { get; } = new(nameof(TogglePeriodicEffects),
        [
            ContentField.ListOf<Effect>("fx", "Effects", ContentFieldKind.Object),
            ContentField.Integer("i", "Interval in frames", 1, ushort.MaxValue),
        ]);

        public override void WriteProperties(ContentWriter writer, TogglePeriodicEffects value)
        {
            writer.List("fx", value.Effects);
            writer.Integer("i", value.IntervalFrames);
        }

        public override TogglePeriodicEffects Read(ContentObject element) =>
            new(element.List<Effect>("fx"), ContentNumbers.UInt16(element.Integer("i"), "i"));
    }

    private sealed class ActionDefinitionCodec : ContentCodec<ActionDefinition>
    {
        public override ContentSchema Schema { get; } = new(nameof(ActionDefinition),
        [
            .. ActivatableContent.LeadingFields,
            ContentField.Polymorphic<IActionActivator>("ac", "Activator", isRequired: true),
            .. ActivatableContent.TrailingFields,
        ]);

        public override void WriteProperties(ContentWriter writer, ActionDefinition value)
        {
            ActivatableContent.WriteLeading(writer, value);
            writer.Polymorphic("ac", value.Activator);
            ActivatableContent.WriteTrailing(writer, value);
        }

        public override ActionDefinition Read(ContentObject element)
        {
            var activator = element.Polymorphic<IActionActivator>("ac")!;
            return new ActionDefinition(
                element.Guid(ContentTable.IdKey),
                element.String("n"),
                element.OptionalString("sp"),
                element.String("g"),
                element.Color("gc"),
                element.Tags("t"),
                element.List<Effect>("fx"),
                activator,
                element.String("d"),
                element.String("su"),
                element.Object<ToggleSpec>("tog"))
            {
                ActivationEffects = element.List<Effect>("ae"),
            };
        }
    }

    private sealed class DirectActionCodec : ContentCodec<DirectAction>
    {
        public override ContentSchema Schema { get; } = ActivatorContent.TargetedSchema(nameof(DirectAction));

        public override void WriteProperties(ContentWriter writer, DirectAction value) => ActivatorContent.WriteTargeted(writer, value.Targeting, value.Timing);

        public override DirectAction Read(ContentObject element) => new(ActivatorContent.Targeting(element), ActivatorContent.Timing(element));
    }

    private sealed class SpellActivatorCodec : ContentCodec<SpellActivator>
    {
        public override ContentSchema Schema { get; } = ActivatorContent.TargetedSchema(nameof(SpellActivator));

        public override void WriteProperties(ContentWriter writer, SpellActivator value) => ActivatorContent.WriteTargeted(writer, value.Targeting, value.Timing);

        public override SpellActivator Read(ContentObject element) => new(ActivatorContent.Targeting(element), ActivatorContent.Timing(element));
    }

    private sealed class PotionActivatorCodec : ContentCodec<PotionActivator>
    {
        public override ContentSchema Schema { get; } = ActivatorContent.TargetedSchema(nameof(PotionActivator));

        public override void WriteProperties(ContentWriter writer, PotionActivator value) => ActivatorContent.WriteTargeted(writer, value.Targeting, value.Timing);

        public override PotionActivator Read(ContentObject element) => new(ActivatorContent.Targeting(element), ActivatorContent.Timing(element));
    }

    /// <summary>A scroll's spell is a Guid in the Actions table, but need not name an action there (a synthesised spell), so it is read as a Guid and never resolved here.</summary>
    private sealed class ScrollActivatorCodec : ContentCodec<ScrollActivator>
    {
        public override ContentSchema Schema { get; } = ActivatorContent.TargetedSchema(nameof(ScrollActivator), ContentField.Reference("sp", "Spell", ActionsTable, isRequired: true));

        public override void WriteProperties(ContentWriter writer, ScrollActivator value)
        {
            ActivatorContent.WriteTargeted(writer, value.Targeting, value.Timing);
            writer.Guid("sp", value.SpellId);
        }

        public override ScrollActivator Read(ContentObject element) => new(ActivatorContent.Targeting(element), ActivatorContent.Timing(element), element.Guid("sp"));
    }

    /// <summary>Charges and MaxCharges are omitted at 0, as authored.</summary>
    private sealed class WandActivatorCodec : ContentCodec<WandActivator>
    {
        public override ContentSchema Schema { get; } = ActivatorContent.TargetedSchema(nameof(WandActivator),
            ContentField.Integer("c", "Charges", 0, ushort.MaxValue),
            ContentField.Integer("mc", "Maximum charges", 0, ushort.MaxValue));

        public override void WriteProperties(ContentWriter writer, WandActivator value)
        {
            ActivatorContent.WriteTargeted(writer, value.Targeting, value.Timing);
            writer.Integer("c", value.Charges);
            writer.Integer("mc", value.MaxCharges);
        }

        public override WandActivator Read(ContentObject element) => new(
            ActivatorContent.Targeting(element),
            ActivatorContent.Timing(element),
            ContentNumbers.UInt16(element.Integer("c"), "c"),
            ContentNumbers.UInt16(element.Integer("mc"), "mc"));
    }

    private sealed class ToggleItemActivatorCodec : ContentCodec<ToggleItemActivator>
    {
        public override ContentSchema Schema { get; } = new(nameof(ToggleItemActivator),
        [
            ContentField.Object<ActionTiming>("tm", "Timing", isRequired: true),
            ContentField.Boolean("on", "Toggled on"),
        ]);

        public override void WriteProperties(ContentWriter writer, ToggleItemActivator value)
        {
            writer.Object("tm", value.Timing);
            writer.Boolean("on", value.IsToggledOn);
        }

        public override ToggleItemActivator Read(ContentObject element) => new(ActivatorContent.Timing(element), element.Boolean("on"));
    }

    private sealed class DodgeActivationCodec : ContentCodec<DodgeActivation>
    {
        public override ContentSchema Schema { get; } = new(nameof(DodgeActivation), []);

        public override void WriteProperties(ContentWriter writer, DodgeActivation value)
        {
        }

        public override DodgeActivation Read(ContentObject element) => new();
    }

    private sealed class HotkeyExpansionGrantCodec : ContentCodec<HotkeyExpansionGrant>
    {
        public override ContentSchema Schema { get; } = new(nameof(HotkeyExpansionGrant), [ContentField.Integer("n", "Slots", 0, byte.MaxValue)]);

        public override void WriteProperties(ContentWriter writer, HotkeyExpansionGrant value) => writer.Integer("n", value.Slots);

        public override HotkeyExpansionGrant Read(ContentObject element) => new(ContentNumbers.Byte(element.Integer("n"), "n"));
    }
}

/// <summary>The targeting and timing every targeted activator holds.</summary>
internal static class ActivatorContent
{
    public static ContentSchema TargetedSchema(string typeName, params ContentField[] extraFields) => new(typeName,
    [
        ContentField.Object<TargetingSpec>("tg", "Targeting", isRequired: true),
        ContentField.Object<ActionTiming>("tm", "Timing", isRequired: true),
        .. extraFields,
    ]);

    public static void WriteTargeted(ContentWriter writer, TargetingSpec targeting, ActionTiming timing)
    {
        writer.Object("tg", targeting);
        writer.Object("tm", timing);
    }

    public static TargetingSpec Targeting(ContentObject element) => element.Object<TargetingSpec>("tg")!;

    public static ActionTiming Timing(ContentObject element) => element.Object<ActionTiming>("tm")!;
}

/// <summary>The fields an ActionDefinition and an ItemDefinition share, written in the same order by both: identity and look first, then (after each type's own) text, toggle and activation effects.</summary>
/// <remarks>"t" is DeclaredTags -- the activator's implied tags are never written, since they follow the activator.</remarks>
public static class ActivatableContent
{
    public static IReadOnlyList<ContentField> LeadingFields { get; } =
    [
        ContentField.Guid(ContentTable.IdKey, "Id", isRequired: true),
        ContentField.Text("n", "Name", isRequired: true),
        ContentField.Text("sp", "Sprite name"),
        ContentField.Text("g", "Glyph"),
        ContentField.Color("gc", "Glyph colour"),
        ContentField.TagSet("t", "Tags"),
        ContentField.ListOf<Effect>("fx", "Effects", ContentFieldKind.Object),
    ];

    public static IReadOnlyList<ContentField> TrailingFields { get; } =
    [
        ContentField.Text("d", "Description"),
        ContentField.Text("su", "Summary"),
        ContentField.Object<ToggleSpec>("tog", "Toggle"),
        ContentField.ListOf<Effect>("ae", "Activation effects", ContentFieldKind.Object),
    ];

    public static void WriteLeading(ContentWriter writer, ActivatableDefinition value)
    {
        writer.Guid(ContentTable.IdKey, value.Id);
        writer.String("n", value.Name);
        writer.OptionalString("sp", value.SpriteName);
        writer.String("g", value.Glyph);
        writer.Color("gc", value.GlyphColor);
        writer.Tags("t", value.DeclaredTags);
        writer.List("fx", value.Effects);
    }

    public static void WriteTrailing(ContentWriter writer, ActivatableDefinition value)
    {
        writer.String("d", value.Description);
        writer.String("su", value.Summary);
        writer.Object("tog", value.Toggle);
        writer.List("ae", value.ActivationEffects);
    }
}
