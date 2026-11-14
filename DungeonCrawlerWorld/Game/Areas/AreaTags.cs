using Engine.Tags;

namespace Game.Areas;

/// <summary>The built-in map area tags, declared with their root by AreasModule, the only module that reads area content.</summary>
public static class AreaTags
{
    public static readonly GameplayTag Area = GameplayTag.Get("Area");

    public static readonly GameplayTag AreaTesting = GameplayTag.Get("Area.Testing");
}
