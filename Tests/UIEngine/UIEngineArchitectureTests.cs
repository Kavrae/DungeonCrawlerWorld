using UIEngine.UI;

namespace Tests.UIEngine;

[TestClass]
public sealed class UIEngineArchitectureTests
{
    private static List<string> UIEngineReferences() =>
        typeof(Element).Assembly.GetReferencedAssemblies().Select(reference => reference.Name!).ToList();

    /// <summary>The window framework knows no game: the engine IDE builds on it without one, and the game reaches it only through Presentation.</summary>
    [TestMethod]
    public void UIEngine_DoesNotReferenceGame()
    {
        CollectionAssert.DoesNotContain(UIEngineReferences(), typeof(Game.World.World).Assembly.GetName().Name);
    }

    /// <summary>Presentation builds on UIEngine, never the reverse -- a game window or rule UIEngine needs comes in through one of its hooks (IUiInputHandler, IContentDragSource, IContentDropHandler).</summary>
    [TestMethod]
    public void UIEngine_DoesNotReferencePresentation()
    {
        CollectionAssert.DoesNotContain(UIEngineReferences(), typeof(global::Presentation.UI.MapWindow).Assembly.GetName().Name);
    }
}
