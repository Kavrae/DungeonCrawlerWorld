using Engine.Content;
using Engine.Tiles;
using Game.Content;
using Game.Effects;
using Game.Modules.Actions.Activators;
using Game.Modules.Actions.Effects;
using Game.Tags;

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
        contentTypes.RegisterPolymorphic<IActionActivator, ConsumableActivator>("Consumable", new ConsumableActivatorCodec());

        contentTypes.RegisterPolymorphic<IEffectEntry, DodgeActivation>(nameof(DodgeActivation), new DodgeActivationCodec());
        contentTypes.RegisterPolymorphic<IEffectEntry, HotkeyExpansionGrant>(nameof(HotkeyExpansionGrant), new HotkeyExpansionGrantCodec());
        contentTypes.RegisterPolymorphic<IEffectEntry, SpellGrant>(nameof(SpellGrant), new SpellGrantCodec());
    }

    private sealed class TargetingSpecCodec : ContentCodec<TargetingSpec>
    {
        public override ContentSchema Schema { get; } = new(nameof(TargetingSpec),
        [
            ContentField.Enum<TargetShape>("sh", "Shape"),
            ContentField.Integer("r", "Maximum range", 0),
            ContentField.Integer("a", "Area size", 0),
            ContentField.Enum("mo", "Targeting modes", TargetingModes.Both),
            ContentField.Enum("ta", "Target mode affects", TargetModeAffects.Area),
            ContentField.Enum("an", "Anchor", TargetAnchor.PathEnd),
            ContentField.Integer("mn", "Minimum range", 0),
            ContentField.Enum("pa", "Path", TargetPath.Placed),
            ContentField.Enum("pe", "Projectile path end", ProjectilePathEnd.AtAimedTile),
            ContentField.Integer("ph", "Projectile hit count (65535: every entity on the way)", 0, ushort.MaxValue),
            ContentField.Enum("al", "Aim line of sight", LineOfSightRule.NotRequired),
            ContentField.Enum("pw", "Path walls", PathWallRule.StopsAtWall),
            ContentField.Integer("wb", "Wall bounce count", 0, byte.MaxValue),
            ContentField.Enum("sl", "Shape line of sight", LineOfSightRule.NotRequired),
        ]);

        public override void WriteProperties(ContentWriter writer, TargetingSpec value)
        {
            writer.Enum("sh", value.Shape);
            writer.Integer("r", value.MaximumRange);
            writer.Integer("a", value.AreaSize);
            writer.Enum("mo", value.Modes, TargetingModes.Both);
            writer.Enum("ta", value.TargetModeAffects, TargetModeAffects.Area);
            writer.Enum("an", value.Anchor, TargetAnchor.PathEnd);
            writer.Integer("mn", value.MinimumRange);
            writer.Enum("pa", value.Path, TargetPath.Placed);
            writer.Enum("pe", value.PathEnd, ProjectilePathEnd.AtAimedTile);
            writer.Integer("ph", value.ProjectileHitCount);
            writer.Enum("al", value.AimLineOfSight, LineOfSightRule.NotRequired);
            writer.Enum("pw", value.PathWalls, PathWallRule.StopsAtWall);
            writer.Integer("wb", value.WallBounceCount);
            writer.Enum("sl", value.ShapeLineOfSight, LineOfSightRule.NotRequired);
        }

        public override TargetingSpec Read(ContentObject element) => new(
            element.Enum<TargetShape>("sh"),
            ContentNumbers.Int32(element.Integer("r"), "r"),
            ContentNumbers.Int32(element.Integer("a"), "a"),
            element.Enum("mo", TargetingModes.Both),
            element.Enum("ta", TargetModeAffects.Area),
            element.Enum("an", TargetAnchor.PathEnd),
            ContentNumbers.Int32(element.Integer("mn"), "mn"),
            element.Enum("pa", TargetPath.Placed),
            element.Enum("pe", ProjectilePathEnd.AtAimedTile),
            ContentNumbers.UInt16(element.Integer("ph"), "ph"),
            element.Enum("al", LineOfSightRule.NotRequired),
            element.Enum("pw", PathWallRule.StopsAtWall),
            ContentNumbers.Byte(element.Integer("wb"), "wb"),
            element.Enum("sl", LineOfSightRule.NotRequired));
    }

    private sealed class ActionTimingCodec : ContentCodec<ActionTiming>
    {
        public override ContentSchema Schema { get; } = new(nameof(ActionTiming),
        [
            ContentField.Enum<ActionTimingCategory>("c", "Category"),
            ContentField.OptionalInteger("lf", "Action lock in frames (none: the entity's own)", 0, ushort.MaxValue),
            ContentField.OptionalInteger("cd", "Cooldown in frames", 0, ushort.MaxValue),
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
            ContentField.Polymorphic<IActionActivator>(ActivatableContent.ActivatorKey, "Activator", isRequired: true),
            .. ActivatableContent.TrailingFields,
        ]);

        public override void WriteProperties(ContentWriter writer, ActionDefinition value)
        {
            ActivatableContent.WriteLeading(writer, value);
            writer.Polymorphic(ActivatableContent.ActivatorKey, value.Activator);
            ActivatableContent.WriteTrailing(writer, value);
        }

        public override ActionDefinition Read(ContentObject element)
        {
            var activator = element.Polymorphic<IActionActivator>(ActivatableContent.ActivatorKey)!;
            return new ActionDefinition(
                element.Guid(ContentTable.IdKey),
                element.String(ActivatableContent.NameKey),
                element.OptionalString(ActivatableContent.SpriteNameKey),
                element.String(ActivatableContent.GlyphKey),
                element.Color(ActivatableContent.GlyphColorKey),
                element.Tags(ActivatableContent.TagsKey),
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

    private sealed class ConsumableActivatorCodec : ContentCodec<ConsumableActivator>
    {
        public override ContentSchema Schema { get; } = ActivatorContent.TargetedSchema(nameof(ConsumableActivator));

        public override void WriteProperties(ContentWriter writer, ConsumableActivator value) => ActivatorContent.WriteTargeted(writer, value.Targeting, value.Timing);

        public override ConsumableActivator Read(ContentObject element) => new(ActivatorContent.Targeting(element), ActivatorContent.Timing(element));
    }

    /// <summary>The spell is written whole, not named by Guid: a spell made at runtime is in no table.</summary>
    private sealed class SpellGrantCodec : ContentCodec<SpellGrant>
    {
        public override ContentSchema Schema { get; } = new(nameof(SpellGrant), [ContentField.Object<ActionDefinition>("sp", "Spell", isRequired: true)]);

        public override void WriteProperties(ContentWriter writer, SpellGrant value) => writer.Object("sp", value.Spell);

        public override SpellGrant Read(ContentObject element) => new(element.Object<ActionDefinition>("sp")!);
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
    public const string NameKey = "n";

    public const string SpriteNameKey = "sp";

    public const string GlyphKey = "g";

    public const string GlyphColorKey = "gc";

    public const string TagsKey = "t";

    /// <summary>The activator's key, which each type writes after the leading fields.</summary>
    public const string ActivatorKey = "ac";

    public static IReadOnlyList<ContentField> LeadingFields { get; } =
    [
        ContentField.Guid(ContentTable.IdKey, "Id", isRequired: true),
        ContentField.Text(NameKey, "Name", isRequired: true),
        ContentField.OptionalText(SpriteNameKey, "Sprite name"),
        ContentField.Text(GlyphKey, "Glyph"),
        ContentColors.Field(GlyphColorKey, "Glyph colour"),
        ContentField.TagSet(TagsKey, "Tags", GameTagDomains.Activation),
        ContentField.ListOf<Effect>("fx", "Effects (what using it does to its targets; what a toggle holds while on)", ContentFieldKind.Object),
    ];

    public static IReadOnlyList<ContentField> TrailingFields { get; } =
    [
        ContentField.Text("d", "Description") with { IsMultiline = true },
        ContentField.Text("su", "Summary"),
        ContentField.Object<ToggleSpec>("tog", "Toggle"),
        ContentField.ListOf<Effect>("ae", "Activation effects (the costs on the user to activate it: applied to whoever uses it, all or nothing, before anything else -- a mana drain, a health drain)", ContentFieldKind.Object),
    ];

    public static void WriteLeading(ContentWriter writer, ActivatableDefinition value)
    {
        writer.Guid(ContentTable.IdKey, value.Id);
        writer.String(NameKey, value.Name);
        writer.OptionalString(SpriteNameKey, value.SpriteName);
        writer.String(GlyphKey, value.Glyph);
        writer.Color(GlyphColorKey, value.GlyphColor);
        writer.Tags(TagsKey, value.DeclaredTags);
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
