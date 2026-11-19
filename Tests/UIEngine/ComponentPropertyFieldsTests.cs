using Engine.ECS.Properties;
using Game.Modules.Actions;
using Game.Modules.Core.Components;
using UIEngine.UI.Fields;

namespace Tests.UIEngine;

/// <summary>A component property mapped onto a FieldRow and a committed field value back onto the value an edit writes.</summary>
[TestClass]
public sealed class ComponentPropertyFieldsTests
{
    private static ComponentPropertyDescriptor Property(ComponentPropertyKind kind, Type valueType, ComponentPropertyFlags flags = ComponentPropertyFlags.None) =>
        new(0, "Value", kind, valueType, default, flags);

    [TestMethod]
    public void AnInteger_IsANumberFieldLimitedToItsType()
    {
        var property = Property(ComponentPropertyKind.Integer, typeof(byte));

        var description = ComponentPropertyFields.Describe(property, "Size", 1, "3", ComponentPropertyValue.FromInteger(3), isEditable: true, isHeading: false, choices: null);

        Assert.AreEqual(FieldValueKind.Integer, description.Kind);
        Assert.AreEqual(0d, description.Minimum);
        Assert.AreEqual(255d, description.Maximum);
        Assert.AreEqual(1, description.Depth);
        Assert.IsTrue(ComponentPropertyFields.TryRead(description, property, FieldValue.OfNumber(7), null, out var written));
        Assert.AreEqual(7, written.Integer);
    }

    [TestMethod]
    public void A64BitInteger_IsTextReadBackExactlyPastWhatADoubleHolds()
    {
        var property = Property(ComponentPropertyKind.Integer, typeof(long));
        const long ticks = 638_700_000_000_000_123;

        var description = ComponentPropertyFields.Describe(property, "EarnedAtUtcTicks", 0, ticks.ToString(), ComponentPropertyValue.FromInteger(ticks), isEditable: true, isHeading: false, choices: null);
        var shown = ComponentPropertyFields.ValueOf(description, property, ticks.ToString(), ComponentPropertyValue.FromInteger(ticks), null);

        Assert.AreEqual(FieldValueKind.Text, description.Kind);
        Assert.IsTrue(ComponentPropertyFields.TryRead(description, property, shown, null, out var written));
        Assert.AreEqual(ticks, written.Integer);
        Assert.IsFalse(ComponentPropertyFields.TryRead(description, property, FieldValue.OfText("12.5"), null, out _));
    }

    [TestMethod]
    public void AFrame_ShowsItsMeaningAsTheHint()
    {
        var property = Property(ComponentPropertyKind.Integer, typeof(uint));

        var description = ComponentPropertyFields.Describe(property, "ExpiresAtFrame", 0, "frame 120 (in 2.0 s)", ComponentPropertyValue.FromInteger(120), isEditable: true, isHeading: false, choices: null);

        Assert.AreEqual("frame 120 (in 2.0 s)", description.Hint);
    }

    [TestMethod]
    public void AnOptionalEnum_OffersNoneFirstAndWritesAbsentForIt()
    {
        var property = Property(ComponentPropertyKind.Enum, typeof(HotkeySlot), ComponentPropertyFlags.Optional);

        var description = ComponentPropertyFields.Describe(property, "ActivatedFromSlot", 0, "none", ComponentPropertyValue.Absent, isEditable: true, isHeading: false, choices: null);
        var value = ComponentPropertyFields.ValueOf(description, property, "none", ComponentPropertyValue.Absent, null);

        Assert.AreEqual(FieldValueKind.Choice, description.Kind);
        Assert.AreEqual(ComponentPropertyFields.NoneChoice, description.Choices[0]);
        Assert.AreEqual(0, value.ChoiceIndex);
        Assert.IsTrue(ComponentPropertyFields.TryRead(description, property, FieldValue.OfChoice(0), null, out var absent));
        Assert.IsTrue(absent.IsAbsent);
        Assert.IsTrue(ComponentPropertyFields.TryRead(description, property, FieldValue.OfChoice(2), null, out var slot));
        Assert.AreEqual((long)HotkeySlot.Base2, slot.Integer);
    }

    [TestMethod]
    public void Flags_AreOneChoicePerBit()
    {
        var property = Property(ComponentPropertyKind.Flags, typeof(NonBlockingKind));

        var description = ComponentPropertyFields.Describe(property, "Kind", 0, "Phasing", ComponentPropertyValue.FromEnum((long)NonBlockingKind.Phasing), isEditable: true, isHeading: false, choices: null);
        var value = ComponentPropertyFields.ValueOf(description, property, "Phasing", ComponentPropertyValue.FromEnum((long)NonBlockingKind.Phasing), null);

        CollectionAssert.AreEqual(new[] { "Tiny", "Phasing" }, description.Choices.ToArray());
        CollectionAssert.AreEqual(new[] { 1 }, value.ChoiceIndices!.ToArray());
        Assert.IsTrue(ComponentPropertyFields.TryRead(description, property, FieldValue.OfChoices([0, 1]), null, out var both));
        Assert.AreEqual((long)(NonBlockingKind.Tiny | NonBlockingKind.Phasing), both.Integer);
    }

    [TestMethod]
    public void AMeaningWithChoices_IsASearchableChoice()
    {
        var property = Property(ComponentPropertyKind.Integer, typeof(ushort));
        IReadOnlyList<ComponentPropertyChoice> choices = [new("none", ComponentPropertyValue.FromInteger(0)), new("Goblin (4)", ComponentPropertyValue.FromInteger(4))];

        var description = ComponentPropertyFields.Describe(property, "Class1", 0, "Goblin (4)", ComponentPropertyValue.FromInteger(4), isEditable: true, isHeading: false, choices);
        var value = ComponentPropertyFields.ValueOf(description, property, "Goblin (4)", ComponentPropertyValue.FromInteger(4), choices);

        Assert.AreEqual(FieldValueKind.SearchableChoice, description.Kind);
        Assert.AreEqual(1, value.ChoiceIndex);
        Assert.IsTrue(ComponentPropertyFields.TryRead(description, property, FieldValue.OfChoice(0), choices, out var none));
        Assert.AreEqual(0, none.Integer);
        Assert.IsFalse(ComponentPropertyFields.TryRead(description, property, FieldValue.OfChoice(-1, "typed"), choices, out _));
    }

    [TestMethod]
    public void ReadOnlyAndHeadingRows_AreTextThatCantTakeFocus()
    {
        var property = Property(ComponentPropertyKind.Integer, typeof(uint), ComponentPropertyFlags.ReadOnly);

        var readOnly = ComponentPropertyFields.Describe(property, "StackInstanceId", 0, "12", ComponentPropertyValue.FromInteger(12), isEditable: false, isHeading: false, choices: null);
        var heading = ComponentPropertyFields.Describe(property, "Selection", 0, "", default, isEditable: false, isHeading: true, choices: null);
        var definition = ComponentPropertyFields.Describe(Property(ComponentPropertyKind.Definition, typeof(object)), "Definition", 0, "Fireball", default, isEditable: true, isHeading: false, choices: null);

        Assert.AreEqual(FieldValueKind.ReadOnlyText, readOnly.Kind);
        Assert.IsFalse(readOnly.IsSet, "A read-only row is greyed.");
        Assert.IsTrue(heading.IsLabelOnly);
        Assert.AreEqual(FieldValueKind.ReadOnlyText, definition.Kind);
        Assert.AreEqual("12", ComponentPropertyFields.ValueOf(readOnly, property, "12", ComponentPropertyValue.FromInteger(12), null).Text);
    }

    [TestMethod]
    public void AGuid_IsTextThatMustParse()
    {
        var property = Property(ComponentPropertyKind.Guid, typeof(Guid));
        var id = Guid.NewGuid();

        var description = ComponentPropertyFields.Describe(property, "ActionId", 0, id.ToString(), ComponentPropertyValue.FromGuid(id), isEditable: true, isHeading: false, choices: null);

        Assert.AreEqual(FieldValueKind.Text, description.Kind);
        Assert.IsFalse(ComponentPropertyFields.TryRead(description, property, FieldValue.OfText("not a guid"), null, out _));
        Assert.IsTrue(ComponentPropertyFields.TryRead(description, property, FieldValue.OfText(id.ToString()), null, out var written));
        Assert.AreEqual(id, written.Guid);
    }
}
