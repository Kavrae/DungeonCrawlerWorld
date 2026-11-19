using Engine.ECS.Components;
using Engine.ECS.Systems;
using Engine.Utilities;

namespace Engine.ECS.Properties;

/// <summary>Whose value a meaning is resolving: the world it was read from (the live one, or a staging world's rebuild) and the entity holding it.</summary>
public readonly record struct ComponentPropertyContext(ComponentManager Source, int EntityId);

/// <summary>One value a picker offers, and what it is called.</summary>
public readonly record struct ComponentPropertyChoice(string Label, ComponentPropertyValue Value);

/// <summary>Resolves one meaning for display: the text a value stands for, and the rows of an indexed property it names.</summary>
/// <remarks>A null description shows the raw value. The persistent form joins this when saves need it.</remarks>
public interface IComponentPropertyMeaningResolver
{
    string? DescribeInteger(in ComponentPropertyContext context, long value) => null;

    string? DescribeGuid(in ComponentPropertyContext context, Guid value) => null;

    /// <summary>The values a picker offers for this meaning, labelled; false for one entered by typing its value.</summary>
    bool TryGetChoices(in ComponentPropertyContext context, out IReadOnlyList<ComponentPropertyChoice> choices)
    {
        choices = [];
        return false;
    }

    /// <summary>The label of an indexed property's row, or false when the entity has no such row (a body part its race doesn't have).</summary>
    bool TryNameRow(in ComponentPropertyContext context, int index, out string label)
    {
        label = index.ToString(System.Globalization.CultureInfo.InvariantCulture);
        return true;
    }
}

/// <summary>The resolvers a session has for each meaning, by name; a meaning with none shows its raw value.</summary>
/// <remarks>Built once per session from the engine's resolvers and the game's; never read by gameplay.</remarks>
public sealed class ComponentPropertyMeanings
{
    private readonly Dictionary<string, IComponentPropertyMeaningResolver> _resolvers = new(StringComparer.Ordinal);

    public void Register(string meaning, IComponentPropertyMeaningResolver resolver)
    {
        ArgumentNullException.ThrowIfNull(resolver);
        _resolvers[meaning] = resolver;
    }

    public bool TryGet(ComponentPropertyMeaning meaning, out IComponentPropertyMeaningResolver resolver)
    {
        if (meaning.Name is { } name && _resolvers.TryGetValue(name, out var found))
        {
            resolver = found;
            return true;
        }

        resolver = null!;
        return false;
    }

    /// <summary>The engine's own: Frame (a frame against the clock, in seconds) and Frames (a length of time).</summary>
    public static ComponentPropertyMeanings WithEngineResolvers(SimulationClock clock)
    {
        var meanings = new ComponentPropertyMeanings();
        meanings.Register(ComponentPropertyMeaningNames.Frame, new FrameResolver(clock));
        meanings.Register(ComponentPropertyMeaningNames.Frames, new FramesResolver());
        return meanings;
    }

    /// <summary>"frame N (in 2.3 s)", "(1.5 s ago)", or "never" for FrameDeadline.Never.</summary>
    private sealed class FrameResolver(SimulationClock clock) : IComponentPropertyMeaningResolver
    {
        public string? DescribeInteger(in ComponentPropertyContext context, long value)
        {
            if (value == FrameDeadline.Never)
            {
                return "never";
            }

            var seconds = (value - clock.CurrentFrame) / (double)GameTiming.FramesPerSecond;
            var relative = seconds >= 0 ? $"in {seconds:0.0} s" : $"{-seconds:0.0} s ago";
            return $"frame {value} ({relative})";
        }
    }

    /// <summary>"N frames (x.x s)".</summary>
    private sealed class FramesResolver : IComponentPropertyMeaningResolver
    {
        public string? DescribeInteger(in ComponentPropertyContext context, long value) =>
            $"{value} frames ({value / (double)GameTiming.FramesPerSecond:0.0} s)";
    }
}
