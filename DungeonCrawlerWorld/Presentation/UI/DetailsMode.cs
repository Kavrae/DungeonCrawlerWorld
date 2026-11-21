namespace Presentation.UI;

/// <summary>How much a details window says about an action or item.</summary>
public enum DetailsMode : byte
{
    /// <summary>What it does, at a glance: effects and their amounts, without how they are placed or kept.</summary>
    Simple,

    /// <summary>Everything it carries: also each effect's body part, and whether its duration is locked.</summary>
    Advanced,
}
