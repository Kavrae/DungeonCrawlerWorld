using Engine.Content;
using Game.Content;
using Game.Effects;
using Game.Modules.Actions;

namespace Game.Modules.Inventory;

/// <summary>Codecs for ItemDefinition and SetItemContents; registered by InventoryModule.</summary>
internal static class InventoryContentCodecs
{
    public const string ItemsTable = "Items";

    public static void Register(ContentTypeRegistration registration)
    {
        registration.RegisterValue(new ItemDefinitionCodec());
        registration.RegisterValue(new ItemContentsEntryCodec());
        registration.RegisterPolymorphic<IItemContents, SetItemContents>("Set", new SetItemContentsCodec());
    }

    private sealed class ItemDefinitionCodec : ContentCodec<ItemDefinition>
    {
        public override ContentSchema Schema { get; } = new(nameof(ItemDefinition),
        [
            .. ActivatableContent.LeadingFields,
            ContentField.Polymorphic<IActionActivator>("ac", "Activator"),
            ContentField.Integer("gv", "Gold value", 0),
            ContentField.Integer("ms", "Maximum shop stock (none: the default)", 0),
            ContentField.Polymorphic<IItemContents>("ct", "Contents"),
            ContentField.Boolean("tr", "Can trade"),
            .. ActivatableContent.TrailingFields,
        ]);

        public override void WriteProperties(ContentWriter writer, ItemDefinition value)
        {
            ActivatableContent.WriteLeading(writer, value);
            writer.Polymorphic("ac", value.Activator);
            writer.Integer("gv", value.GoldValue);
            writer.OptionalInteger("ms", value.MaximumShopStock);
            writer.Polymorphic("ct", value.Contents);
            writer.Boolean("tr", value.CanTrade, defaultValue: true);
            ActivatableContent.WriteTrailing(writer, value);
        }

        public override ItemDefinition Read(ContentObject element) => new(
            element.Guid(ContentTable.IdKey),
            element.String("n"),
            element.OptionalString("sp"),
            element.String("g"),
            element.Color("gc"),
            element.Tags("t"),
            element.List<Effect>("fx"),
            element.String("d"),
            element.String("su"),
            element.Polymorphic<IActionActivator>("ac"),
            ContentNumbers.Int32(element.Integer("gv"), "gv"),
            ContentNumbers.Int32(element.OptionalInteger("ms"), "ms"),
            element.Polymorphic<IItemContents>("ct"),
            element.Boolean("tr", defaultValue: true),
            element.Object<ToggleSpec>("tog"))
        {
            ActivationEffects = element.List<Effect>("ae"),
        };
    }

    /// <summary>The item is a Guid in the Items table; it may be one registered after this record (a box holding a later item), so it is never resolved here.</summary>
    private sealed class ItemContentsEntryCodec : ContentCodec<ItemContentsEntry>
    {
        public override ContentSchema Schema { get; } = new(nameof(ItemContentsEntry),
        [
            ContentField.Reference(ContentTable.IdKey, "Item", ItemsTable, isRequired: true),
            ContentField.Integer("q", "Quantity", 1, ushort.MaxValue),
        ]);

        public override void WriteProperties(ContentWriter writer, ItemContentsEntry value)
        {
            writer.Guid(ContentTable.IdKey, value.ItemDefinitionId);
            writer.Integer("q", value.Quantity, defaultValue: 1);
        }

        public override ItemContentsEntry Read(ContentObject element) =>
            new(element.Guid(ContentTable.IdKey), ContentNumbers.UInt16(element.Integer("q", 1), "q"));
    }

    private sealed class SetItemContentsCodec : ContentCodec<SetItemContents>
    {
        public override ContentSchema Schema { get; } = new(nameof(SetItemContents), [ContentField.ListOf<ItemContentsEntry>("e", "Entries", ContentFieldKind.Object)]);

        public override void WriteProperties(ContentWriter writer, SetItemContents value) => writer.List("e", value.Entries);

        public override SetItemContents Read(ContentObject element) => new(element.List<ItemContentsEntry>("e"));
    }
}
