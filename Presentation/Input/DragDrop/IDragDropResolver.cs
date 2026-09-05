namespace Presentation.Input.DragDrop;

/// <summary>
/// One feature's self-contained drag-drop resolution strategy -- UiInputController.ResolveContentDrag
/// tries each registered resolver in a fixed priority order and stops at the first one that claims
/// the drag, instead of accumulating every feature's eligibility rules inline. See
/// UiInputController's own _dragDropResolvers construction for registration order.
/// </summary>
internal interface IDragDropResolver
{
    /// <summary>
    /// Returns true once this resolver has claimed the drag -- based on who the origin/destination
    /// entities are, regardless of whether the underlying action it performed inside actually
    /// succeeded (e.g. an unaffordable shop purchase still claims the drag, it just moves nothing).
    /// Dispatch stops at the first true; a resolver that isn't the right owner for this drag returns
    /// false so the next one in priority order gets a chance.
    /// </summary>
    bool TryResolve(in DragDropContext context);
}
