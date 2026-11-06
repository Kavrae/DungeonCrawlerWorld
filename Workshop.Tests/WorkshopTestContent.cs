using Engine.Content;
using Game.Content;

namespace Workshop.Tests;

/// <summary>The content layers the tests' IDE sessions read: the master manifest copied into the test output, alone.</summary>
internal static class WorkshopTestContent
{
    public static ContentLayers Master { get; } = GameContent.MasterOnly(GameContent.DefaultMasterDirectory);
}
