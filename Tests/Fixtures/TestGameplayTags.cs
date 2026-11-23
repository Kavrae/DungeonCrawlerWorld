using Engine.Tags;
using Game.Modules.Core;

namespace Tests.Fixtures;

/// <summary>The built-in gameplay tags, declared the way a real build declares them, for a test that shows or checks tags without a whole build.</summary>
internal static class TestGameplayTags
{
    public static GameplayTagRegistry BuiltIn { get; } = GameplayTagRegistry.Declare([new CoreModule()]);
}
