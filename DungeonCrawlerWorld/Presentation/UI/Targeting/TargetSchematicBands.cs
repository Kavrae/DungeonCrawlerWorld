namespace Presentation.UI.Targeting;

/// <summary>The few lengths a target schematic draws a distance at: ordered like the distances, never to scale.</summary>
public static class TargetSchematicBands
{
    /// <summary>The range from which a path is drawn with a break mark.</summary>
    public const int BrokenPathRange = 10;

    /// <summary>How long a path of range is drawn, in schematic units.</summary>
    public static float PathUnits(int range) => range switch
    {
        <= 0 => 0f,
        1 => 1f,
        <= 4 => 2f,
        <= 9 => 3f,
        _ => 4f,
    };

    /// <summary>Whether a path of range is drawn shorter than its band would make it look, with a break mark.</summary>
    public static bool IsPathBroken(int range) => range >= BrokenPathRange;

    /// <summary>How big a shape of size is drawn -- a radius, or a length from where it starts -- in schematic units.</summary>
    public static float ShapeUnits(int size) => size switch
    {
        <= 1 => 1f,
        <= 4 => 1.5f,
        <= 9 => 2f,
        _ => 2.5f,
    };
}
