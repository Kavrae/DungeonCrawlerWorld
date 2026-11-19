using Engine.Tags;

namespace Engine.ECS.Properties;

/// <summary>Receives a described value's properties in table order, one call per property, with no boxing.</summary>
/// <remarks>
/// Integers widen to long and numbers to double, so a float round-trips exactly. A nested property arrives as
/// BeginNested, its own properties, EndNested; an indexed one as BeginIndexed, one element call per row, EndIndexed;
/// an optional one that holds nothing as Absent. A pool's read brackets each instance with BeginInstance/EndInstance.
/// </remarks>
public abstract class ComponentPropertyVisitor
{
    public abstract void Integer(ComponentPropertyDescriptor property, long value);

    public abstract void Number(ComponentPropertyDescriptor property, double value);

    public abstract void Boolean(ComponentPropertyDescriptor property, bool value);

    public abstract void Text(ComponentPropertyDescriptor property, string? value);

    public abstract void Enum(ComponentPropertyDescriptor property, long value);

    public abstract void Guid(ComponentPropertyDescriptor property, Guid value);

    public abstract void Color(ComponentPropertyDescriptor property, uint packedValue);

    public abstract void Tag(ComponentPropertyDescriptor property, GameplayTag value);

    public abstract void TagQuery(ComponentPropertyDescriptor property, GameplayTagQuery? value);

    public abstract void Definition(ComponentPropertyDescriptor property, object? value);

    public abstract void Absent(ComponentPropertyDescriptor property);

    public abstract void BeginNested(ComponentPropertyDescriptor property);

    public abstract void EndNested(ComponentPropertyDescriptor property);

    public abstract void BeginIndexed(ComponentPropertyDescriptor property, int count);

    public abstract void EndIndexed(ComponentPropertyDescriptor property);

    /// <summary>A pool's read is starting one of the entity's instances: its position among them and its version.</summary>
    public virtual void BeginInstance(ComponentPropertyTable table, int instance, uint version)
    {
    }

    public virtual void EndInstance(ComponentPropertyTable table, int instance)
    {
    }
}
