using Engine.Content;
using Engine.Tags;
using Game.Content;
using Game.Modules.Health;
using Game.Modules.StatModifiers;
using Game.Modules.StatusEffects;
using Game.Tags;

namespace Game.Effects.Entries;

/// <summary>Codecs for the general effect entries in Game.Effects.Entries, each registered under its type's name; registered by Core.</summary>
internal static class EffectEntryCodecs
{
    public static void Register(ContentTypeDeclarations contentTypes)
    {
        contentTypes.RegisterPolymorphic<IEffectEntry, DirectDamage>(nameof(DirectDamage), new DirectDamageCodec());
        contentTypes.RegisterPolymorphic<IEffectEntry, DirectHeal>(nameof(DirectHeal), new DirectHealCodec());
        contentTypes.RegisterPolymorphic<IEffectEntry, DirectManaRestore>(nameof(DirectManaRestore), new DirectManaRestoreCodec());
        contentTypes.RegisterPolymorphic<IEffectEntry, HealthDrain>(nameof(HealthDrain), new HealthDrainCodec());
        contentTypes.RegisterPolymorphic<IEffectEntry, ManaDrain>(nameof(ManaDrain), new ManaDrainCodec());
        contentTypes.RegisterPolymorphic<IEffectEntry, StatModifierGrant>(nameof(StatModifierGrant), new StatModifierGrantCodec());
        contentTypes.RegisterPolymorphic<IEffectEntry, StatusEffectGrant>(nameof(StatusEffectGrant), new StatusEffectGrantCodec());
        contentTypes.RegisterPolymorphic<IEffectEntry, StatusEffectImmunityGrant>(nameof(StatusEffectImmunityGrant), new StatusEffectImmunityGrantCodec());
        contentTypes.RegisterPolymorphic<IEffectEntry, ActivationDisableGrant>(nameof(ActivationDisableGrant), new ActivationDisableGrantCodec());
        contentTypes.RegisterPolymorphic<IEffectEntry, StatusEffectRemoval>(nameof(StatusEffectRemoval), new StatusEffectRemovalCodec());
        contentTypes.RegisterPolymorphic<IEffectEntry, ChainedEffect>(nameof(ChainedEffect), new ChainedEffectCodec());
        contentTypes.RegisterPolymorphic<IEffectEntry, TerrainChange>(nameof(TerrainChange), new TerrainChangeCodec());
    }

    private sealed class DirectDamageCodec : ContentCodec<DirectDamage>
    {
        public override ContentSchema Schema { get; } = new(nameof(DirectDamage),
        [
            ContentField.Integer("mn", "Minimum flat damage", short.MinValue, short.MaxValue),
            ContentField.Integer("mx", "Maximum flat damage", short.MinValue, short.MaxValue),
            ContentField.Number("pct", "Percentage damage"),
            BodyPartTargetingContent.Field,
            ContentField.Enum<BodyPartTargetMode>("bm", "Body part target mode"),
        ]);

        public override void WriteProperties(ContentWriter writer, DirectDamage value)
        {
            writer.Integer("mn", value.MinFlatDamage);
            writer.Integer("mx", value.MaxFlatDamage);
            writer.Number("pct", value.PercentageDamage);
            BodyPartTargetingContent.Write(writer, value.BodyPart);
            writer.Enum("bm", value.BodyPartTargetMode, BodyPartTargetMode.SingleTarget);
        }

        public override DirectDamage Read(ContentObject element) => new(
            ContentNumbers.Int16(element.Integer("mn"), "mn"),
            ContentNumbers.Int16(element.Integer("mx"), "mx"),
            element.Number("pct"),
            BodyPartTargetingContent.Read(element),
            element.Enum("bm", BodyPartTargetMode.SingleTarget));
    }

    private sealed class DirectHealCodec : ContentCodec<DirectHeal>
    {
        public override ContentSchema Schema { get; } = new(nameof(DirectHeal),
        [
            ContentField.Number("pct", "Percent of maximum health"),
            ContentField.Number("fl", "Flat amount"),
            BodyPartTargetingContent.Field,
            ContentField.Enum<BodyPartTargetMode>("bm", "Body part target mode"),
        ]);

        public override void WriteProperties(ContentWriter writer, DirectHeal value)
        {
            writer.Number("pct", value.PercentOfMaxHealth);
            writer.Number("fl", value.FlatAmount);
            BodyPartTargetingContent.Write(writer, value.BodyPart);
            writer.Enum("bm", value.BodyPartTargetMode, BodyPartTargetMode.All);
        }

        public override DirectHeal Read(ContentObject element) => new(
            element.Number("pct"),
            element.Number("fl"),
            BodyPartTargetingContent.Read(element),
            element.Enum("bm", BodyPartTargetMode.All));
    }

    private sealed class DirectManaRestoreCodec : ContentCodec<DirectManaRestore>
    {
        public override ContentSchema Schema { get; } = new(nameof(DirectManaRestore), [ContentField.Number("f", "Fraction of maximum mana", 0, 1)]);

        public override void WriteProperties(ContentWriter writer, DirectManaRestore value) => writer.Number("f", value.Fraction);

        public override DirectManaRestore Read(ContentObject element) => new(element.Number("f"));
    }

    private sealed class HealthDrainCodec : ContentCodec<HealthDrain>
    {
        public override ContentSchema Schema { get; } = new(nameof(HealthDrain), [ContentField.Integer("a", "Amount", 0, ushort.MaxValue)]);

        public override void WriteProperties(ContentWriter writer, HealthDrain value) => writer.Integer("a", value.Amount);

        public override HealthDrain Read(ContentObject element) => new(ContentNumbers.UInt16(element.Integer("a"), "a"));
    }

    private sealed class ManaDrainCodec : ContentCodec<ManaDrain>
    {
        public override ContentSchema Schema { get; } = new(nameof(ManaDrain), [ContentField.Integer("a", "Amount", 0, ushort.MaxValue)]);

        public override void WriteProperties(ContentWriter writer, ManaDrain value) => writer.Integer("a", value.Amount);

        public override ManaDrain Read(ContentObject element) => new(ContentNumbers.UInt16(element.Integer("a"), "a"));
    }

    private sealed class TerrainChangeCodec : ContentCodec<TerrainChange>
    {
        public override ContentSchema Schema { get; } = new(nameof(TerrainChange), [ContentField.Text("tr", "Terrain key", isRequired: true)]);

        public override void WriteProperties(ContentWriter writer, TerrainChange value) => writer.String("tr", value.TerrainKey);

        public override TerrainChange Read(ContentObject element) => new(element.String("tr"));
    }

    private sealed class StatModifierGrantCodec : ContentCodec<StatModifierGrant>
    {
        public override ContentSchema Schema { get; } = new(nameof(StatModifierGrant),
        [
            ContentField.Enum<StatModifierTarget>("tg", "Target"),
            ContentField.Enum<StatModifierOperation>("op", "Operation"),
            ContentField.Enum<StatModifierPolarity>("pol", "Polarity"),
            ContentField.Boolean("cm", "Can be modified"),
            ContentField.Number("m", "Magnitude"),
            ContentField.Integer("d", "Duration in frames (none: permanent)", 0, ushort.MaxValue),
            ContentField.Tag("ct", "Condition tag", GameTagDomains.Activation),
            ContentField.Enum<StatModifierStacking>("st", "Stacking"),
            ContentField.Boolean("fd", "Duration is fixed (never scaled)"),
        ]);

        public override void WriteProperties(ContentWriter writer, StatModifierGrant value)
        {
            writer.Enum("tg", value.Target);
            writer.Enum("op", value.Operation);
            writer.Enum("pol", value.Polarity);
            writer.Boolean("cm", value.CanModify);
            writer.Number("m", value.Magnitude);
            writer.OptionalInteger("d", value.DurationFrames);
            writer.Tag("ct", value.ConditionTag);
            writer.Enum("st", value.Stacking, StatModifierStacking.Add);
            writer.Boolean("fd", value.FixedDuration);
        }

        public override StatModifierGrant Read(ContentObject element) => new(
            element.Enum<StatModifierTarget>("tg"),
            element.Enum<StatModifierOperation>("op"),
            element.Enum<StatModifierPolarity>("pol"),
            element.Boolean("cm"),
            element.Number("m"),
            ContentNumbers.UInt16(element.OptionalInteger("d"), "d"),
            element.Tag("ct"),
            element.Enum("st", StatModifierStacking.Add),
            element.Boolean("fd"));
    }

    private sealed class StatusEffectGrantCodec : ContentCodec<StatusEffectGrant>
    {
        public override ContentSchema Schema { get; } = new(nameof(StatusEffectGrant),
        [
            ContentField.Enum<StatusEffectType>("ty", "Status effect"),
            ContentField.Integer("c", "Stacks", 0),
            ContentField.Enum<StatusEffectGrantMode>("md", "Mode"),
            BodyPartTargetingContent.Field,
            ContentField.Integer("d", "Duration in the effect's ticks (none: the effect's own)", 1, ushort.MaxValue),
        ]);

        public override void WriteProperties(ContentWriter writer, StatusEffectGrant value)
        {
            writer.Enum("ty", value.Type);
            writer.Integer("c", value.StackCount, defaultValue: 1);
            writer.Enum("md", value.Mode, StatusEffectGrantMode.Add);
            BodyPartTargetingContent.Write(writer, value.BodyPart);
            writer.OptionalInteger("d", value.DurationTicks);
        }

        public override StatusEffectGrant Read(ContentObject element) => new(
            element.Enum<StatusEffectType>("ty"),
            ContentNumbers.Int32(element.Integer("c", 1), "c"),
            element.Enum("md", StatusEffectGrantMode.Add),
            BodyPartTargetingContent.Read(element),
            ContentNumbers.UInt16(element.OptionalInteger("d"), "d"));
    }

    private sealed class StatusEffectImmunityGrantCodec : ContentCodec<StatusEffectImmunityGrant>
    {
        public override ContentSchema Schema { get; } = new(nameof(StatusEffectImmunityGrant),
        [
            ContentField.Enum<StatusEffectType>("ty", "Status effect"),
            ContentField.Integer("d", "Duration in frames (none: until revoked)", 0, ushort.MaxValue),
        ]);

        public override void WriteProperties(ContentWriter writer, StatusEffectImmunityGrant value)
        {
            writer.Enum("ty", value.Type);
            writer.OptionalInteger("d", value.DurationFrames);
        }

        public override StatusEffectImmunityGrant Read(ContentObject element) =>
            new(element.Enum<StatusEffectType>("ty"), ContentNumbers.UInt16(element.OptionalInteger("d"), "d"));
    }

    /// <summary>The query is three tag lists -- all of, any of, none of -- each omitted when empty; it is read into one shared instance per entry.</summary>
    private sealed class ActivationDisableGrantCodec : ContentCodec<ActivationDisableGrant>
    {
        public override ContentSchema Schema { get; } = new(nameof(ActivationDisableGrant),
        [
            ContentField.TagSet("all", "Disables what has all of", GameTagDomains.Activation),
            ContentField.TagSet("any", "Disables what has any of", GameTagDomains.Activation),
            ContentField.TagSet("none", "Except what has any of", GameTagDomains.Activation),
            ContentField.Integer("d", "Duration in frames", 1, ushort.MaxValue),
            ContentField.Boolean("fd", "Duration is fixed (never scaled)"),
        ]);

        public override void WriteProperties(ContentWriter writer, ActivationDisableGrant value)
        {
            writer.Tags("all", value.Query.RequireAll);
            writer.Tags("any", value.Query.RequireAny);
            writer.Tags("none", value.Query.Exclude);
            writer.Integer("d", value.DurationFrames);
            writer.Boolean("fd", value.FixedDuration);
        }

        /// <exception cref="ContentFormatException">The record names no tag at all: its query would match, and so disable, every action and item.</exception>
        public override ActivationDisableGrant Read(ContentObject element)
        {
            var query = new GameplayTagQuery(element.Tags("all"), element.Tags("any"), element.Tags("none"));
            if (query.RequireAll.IsEmpty && query.RequireAny.IsEmpty && query.Exclude.IsEmpty)
            {
                throw new ContentFormatException("An ActivationDisableGrant names no tag in \"all\", \"any\" or \"none\": it would disable every action and item.");
            }

            return new(query, ContentNumbers.UInt16(element.Integer("d"), "d"), element.Boolean("fd"));
        }
    }

    private sealed class StatusEffectRemovalCodec : ContentCodec<StatusEffectRemoval>
    {
        public override ContentSchema Schema { get; } = new(nameof(StatusEffectRemoval), [ContentField.Enum<StatusEffectType>("ty", "Status effect")]);

        public override void WriteProperties(ContentWriter writer, StatusEffectRemoval value) => writer.Enum("ty", value.Type);

        public override StatusEffectRemoval Read(ContentObject element) => new(element.Enum<StatusEffectType>("ty"));
    }

    /// <summary>Nested effects to any depth up to ChainedEffect.MaxChainDepth, which the content checks enforce.</summary>
    private sealed class ChainedEffectCodec : ContentCodec<ChainedEffect>
    {
        public override ContentSchema Schema { get; } = new(nameof(ChainedEffect),
        [
            ContentField.Number("ch", "Trigger chance", 0, 1),
            ContentField.ListOf<Effect>("fx", "Triggered effects", ContentFieldKind.Object),
        ]);

        public override void WriteProperties(ContentWriter writer, ChainedEffect value)
        {
            writer.Number("ch", value.TriggerChance);
            writer.List("fx", value.TriggeredEffects);
        }

        public override ChainedEffect Read(ContentObject element) => new(element.Number("ch"), element.List<Effect>("fx"));
    }
}
