namespace Engine.Tiles;

/// <summary>Whether a targeting rule needs line of sight.</summary>
public enum LineOfSightRule : byte
{
    /// <summary>Walls do not matter.</summary>
    NotRequired,

    /// <summary>The tile must be visible: from the caster to aim at it, or from the shape's anchor to be part of the shape.</summary>
    Required,
}
