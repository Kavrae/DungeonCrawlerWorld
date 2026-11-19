using Engine.ECS.Properties;
using Game.Modules.Health.Components;

namespace Game.ComponentProperties;

/// <summary>BodyPartStateComponent's property table, by hand: three rows per part, held in inline arrays read through methods that take a part id.</summary>
/// <remarks>Every part slot is read; which are real, and what each is called, is the entity's race's (the BodyPartId meaning).</remarks>
public sealed class BodyPartStateComponentPropertyAccess : ComponentPropertyAccess<BodyPartStateComponent>
{
    private static readonly ComponentPropertyMeaning BodyPartId = new(GamePropertyMeanings.BodyPartId);

    private static readonly ComponentPropertyDescriptor CurrentHealthRow = new(0, "CurrentHealth", ComponentPropertyKind.Number, typeof(float), default, ComponentPropertyFlags.None);
    private static readonly ComponentPropertyDescriptor RegenLockedUntilRow = new(0, "RegenLockedUntilFrame", ComponentPropertyKind.Integer, typeof(uint), new(ComponentPropertyMeaningNames.Frame), ComponentPropertyFlags.None);
    private static readonly ComponentPropertyDescriptor DisabledRow = new(0, "Disabled", ComponentPropertyKind.Boolean, typeof(bool), default, ComponentPropertyFlags.None);

    private static readonly ComponentPropertyDescriptor CurrentHealthProperty = new(0, "CurrentHealth", ComponentPropertyKind.Indexed, typeof(float), BodyPartId, ComponentPropertyFlags.None, element: CurrentHealthRow);
    private static readonly ComponentPropertyDescriptor RegenLockedUntilProperty = new(1, "RegenLockedUntilFrame", ComponentPropertyKind.Indexed, typeof(uint), BodyPartId, ComponentPropertyFlags.None, element: RegenLockedUntilRow);
    private static readonly ComponentPropertyDescriptor DisabledProperty = new(2, "Disabled", ComponentPropertyKind.Indexed, typeof(bool), BodyPartId, ComponentPropertyFlags.None, element: DisabledRow);

    public static readonly BodyPartStateComponentPropertyAccess Instance = new();

    public static ComponentPropertyTable Description { get; } = new(typeof(BodyPartStateComponent), [CurrentHealthProperty, RegenLockedUntilProperty, DisabledProperty]);

    public override ComponentPropertyTable Table => Description;

    public override void Read(in BodyPartStateComponent component, ComponentPropertyVisitor visitor)
    {
        visitor.BeginIndexed(CurrentHealthProperty, BodyPartStateComponent.MaximumParts);
        for (var part = 0; part < BodyPartStateComponent.MaximumParts; part++)
        {
            visitor.Number(CurrentHealthRow, component.CurrentHealthOf(part));
        }

        visitor.EndIndexed(CurrentHealthProperty);
        visitor.BeginIndexed(RegenLockedUntilProperty, BodyPartStateComponent.MaximumParts);
        for (var part = 0; part < BodyPartStateComponent.MaximumParts; part++)
        {
            visitor.Integer(RegenLockedUntilRow, component.RegenLockedUntilFrameOf(part));
        }

        visitor.EndIndexed(RegenLockedUntilProperty);
        visitor.BeginIndexed(DisabledProperty, BodyPartStateComponent.MaximumParts);
        for (var part = 0; part < BodyPartStateComponent.MaximumParts; part++)
        {
            visitor.Boolean(DisabledRow, component.IsDisabled(part));
        }

        visitor.EndIndexed(DisabledProperty);
    }

    public override bool Write(ref BodyPartStateComponent component, ReadOnlySpan<int> path, in ComponentPropertyValue value)
    {
        if (path.Length != 2 || (uint)path[1] >= BodyPartStateComponent.MaximumParts || value.IsAbsent)
        {
            return false;
        }

        var part = path[1];
        switch (path[0])
        {
            case 0:
                var currentHealth = (float)value.Number;
                if (!float.IsFinite(currentHealth))
                {
                    return false;
                }

                component.SetCurrentHealth(part, currentHealth);
                return true;
            case 1:
                if (value.Integer < uint.MinValue || value.Integer > uint.MaxValue)
                {
                    return false;
                }

                component.SetRegenLockedUntilFrame(part, (uint)value.Integer);
                return true;
            case 2:
                component.SetDisabled(part, value.Boolean);
                return true;
            default:
                return false;
        }
    }

    /// <remarks>Every row is writable, so a restore is a write.</remarks>
    public override bool Restore(ref BodyPartStateComponent component, ReadOnlySpan<int> path, in ComponentPropertyValue value) => Write(ref component, path, in value);
}
