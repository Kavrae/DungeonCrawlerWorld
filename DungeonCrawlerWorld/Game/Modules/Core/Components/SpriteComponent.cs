using Engine.ECS.Properties;

namespace Game.Modules.Core.Components;

/// <summary>
/// A per-instance sprite override: the sprite manifest entry this one entity draws as, and the seed its
/// variant is picked from, in place of its blueprint's appearance. Wins over a GlyphComponent and over the
/// blueprint -- see MapViewQuery.TryGetVisual. Which cell that means is Presentation's to decide.
/// </summary>
[DescribedComponent]
public partial struct SpriteComponent(string spriteName, uint seed)
{
    /// <summary>The sprite manifest entry's name.</summary>
    [PropertyMeaning(GamePropertyMeanings.SpriteName)]
    public string SpriteName { get; set; } = spriteName;

    /// <summary>The seed the entry's variant is picked from.</summary>
    public uint Seed { get; set; } = seed;
}
