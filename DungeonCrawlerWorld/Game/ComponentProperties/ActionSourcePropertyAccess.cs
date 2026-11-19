using Engine.ECS.Entities;
using Engine.ECS.Properties;
using Game.World;

namespace Game.ComponentProperties;

/// <summary>ActionSource's property table, by hand: one packed value whose parts are derived and whose only constructors are its factories.</summary>
/// <remarks>Every row is read-only to an edit. Its identity is an interned handle, so the name, crawler number and cause are its rows, never the handle; a restore interns them again.</remarks>
public static class ActionSourcePropertyAccess
{
    private static readonly ComponentPropertyDescriptor KindProperty = new(0, nameof(ActionSource.Kind), ComponentPropertyKind.Enum, typeof(ActionSourceKind), default, ComponentPropertyFlags.ReadOnly);
    private static readonly ComponentPropertyDescriptor KeyProperty = new(1, nameof(ActionSource.Key), ComponentPropertyKind.Integer, typeof(EntityKey), new(ComponentPropertyMeaningNames.EntityKey), ComponentPropertyFlags.ReadOnly);
    private static readonly ComponentPropertyDescriptor TerrainProperty = new(2, nameof(ActionSource.TerrainTypeId), ComponentPropertyKind.Integer, typeof(ushort), new(GamePropertyMeanings.TerrainTypeId), ComponentPropertyFlags.ReadOnly | ComponentPropertyFlags.Optional);
    private static readonly ComponentPropertyDescriptor AuraProperty = new(3, nameof(ActionSource.AuraId), ComponentPropertyKind.Integer, typeof(byte), new(GamePropertyMeanings.AuraId), ComponentPropertyFlags.ReadOnly | ComponentPropertyFlags.Optional);
    private static readonly ComponentPropertyDescriptor NameProperty = new(4, "Name", ComponentPropertyKind.Text, typeof(string), default, ComponentPropertyFlags.ReadOnly | ComponentPropertyFlags.Optional);
    private static readonly ComponentPropertyDescriptor CrawlerNumberProperty = new(5, "CrawlerNumber", ComponentPropertyKind.Integer, typeof(int), default, ComponentPropertyFlags.ReadOnly | ComponentPropertyFlags.Optional);
    private static readonly ComponentPropertyDescriptor CauseKindProperty = new(6, "CauseKind", ComponentPropertyKind.Enum, typeof(SourceCauseKind), default, ComponentPropertyFlags.ReadOnly);
    private static readonly ComponentPropertyDescriptor CauseNameProperty = new(7, "CauseName", ComponentPropertyKind.Text, typeof(string), default, ComponentPropertyFlags.ReadOnly | ComponentPropertyFlags.Optional);

    public static ComponentPropertyTable Description { get; } = new(
        typeof(ActionSource),
        [KindProperty, KeyProperty, TerrainProperty, AuraProperty, NameProperty, CrawlerNumberProperty, CauseKindProperty, CauseNameProperty]);

    public static void ReadProperties(in ActionSource source, ComponentPropertyVisitor visitor)
    {
        visitor.Enum(KindProperty, (long)source.Kind);
        visitor.Integer(KeyProperty, unchecked((long)source.Key.Value));
        if (source.Kind == ActionSourceKind.Terrain)
        {
            visitor.Integer(TerrainProperty, source.TerrainTypeId);
        }
        else
        {
            visitor.Absent(TerrainProperty);
        }

        if (source.Kind == ActionSourceKind.Aura)
        {
            visitor.Integer(AuraProperty, source.AuraId);
        }
        else
        {
            visitor.Absent(AuraProperty);
        }

        var identity = source.Kind == ActionSourceKind.Entity ? source.Identity : default;
        ReadOptionalText(visitor, NameProperty, identity.Name);
        if (identity.CrawlerNumber is { } crawlerNumber)
        {
            visitor.Integer(CrawlerNumberProperty, crawlerNumber);
        }
        else
        {
            visitor.Absent(CrawlerNumberProperty);
        }

        visitor.Enum(CauseKindProperty, (long)identity.Cause.Kind);
        ReadOptionalText(visitor, CauseNameProperty, identity.Cause.Name);
    }

    public static bool WriteProperty(ref ActionSource source, ReadOnlySpan<int> path, in ComponentPropertyValue value) => false;

    /// <summary>An edit writes nothing. A restore rebuilds the packed value one part at a time, in the table's order: the kind first, then the key, then whichever detail the kind holds -- a terrain, an aura, or the identity, interned again.</summary>
    /// <remarks>A detail the kind doesn't hold is refused unless it is the empty value it reads as.</remarks>
    public static bool WriteProperty(ref ActionSource source, ReadOnlySpan<int> path, in ComponentPropertyValue value, bool restoring)
    {
        if (!restoring || path.Length != 1)
        {
            return false;
        }

        var kind = source.Kind;
        switch (path[0])
        {
            case 0:
                if (!Enum.IsDefined((ActionSourceKind)value.Integer))
                {
                    return false;
                }

                source = ActionSource.FromParts((ActionSourceKind)value.Integer, source.Key, 0);
                return true;
            case 1:
                if (value.Integer < 0)
                {
                    return false;
                }

                source = ActionSource.FromParts(kind, new EntityKey(unchecked((ulong)value.Integer)), source.Detail);
                return true;
            case 2:
                return WriteDetail(ref source, ActionSourceKind.Terrain, value.Integer, ushort.MaxValue);
            case 3:
                return WriteDetail(ref source, ActionSourceKind.Aura, value.Integer, byte.MaxValue);
            case 4:
                return WriteIdentity(ref source, value, static (identity, written) => identity with { Name = written.IsAbsent ? null : written.Text });
            case 5:
                return WriteIdentity(ref source, value, static (identity, written) => identity with { CrawlerNumber = written.IsAbsent ? null : (int)written.Integer });
            case 6:
                if (!Enum.IsDefined((SourceCauseKind)value.Integer))
                {
                    return false;
                }

                return WriteIdentity(ref source, value, static (identity, written) => identity with { Cause = identity.Cause with { Kind = (SourceCauseKind)written.Integer } });
            case 7:
                return WriteIdentity(ref source, value, static (identity, written) => identity with { Cause = identity.Cause with { Name = written.IsAbsent ? null : written.Text } });
            default:
                return false;
        }
    }

    private static bool WriteDetail(ref ActionSource source, ActionSourceKind holder, long detail, long maximum)
    {
        if (detail < 0 || detail > maximum)
        {
            return false;
        }

        if (source.Kind != holder)
        {
            return detail == 0;
        }

        source = ActionSource.FromParts(holder, source.Key, (int)detail);
        return true;
    }

    private static bool WriteIdentity(ref ActionSource source, in ComponentPropertyValue value, Func<EntityIdentity, ComponentPropertyValue, EntityIdentity> change)
    {
        if (source.Kind != ActionSourceKind.Entity)
        {
            return value.IsAbsent || (value.Kind != ComponentPropertyKind.Text && value.Integer == 0);
        }

        var identity = change(source.Identity, value);
        source = ActionSource.FromParts(ActionSourceKind.Entity, source.Key, EntityIdentities.Intern(identity));
        return true;
    }

    private static void ReadOptionalText(ComponentPropertyVisitor visitor, ComponentPropertyDescriptor property, string? text)
    {
        if (text is { } present)
        {
            visitor.Text(property, present);
        }
        else
        {
            visitor.Absent(property);
        }
    }
}
