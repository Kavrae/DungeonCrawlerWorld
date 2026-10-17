using Game.Modules.Currency;

namespace Presentation.Input.DragDrop;

/// <summary>
/// Everything an IDragDropResolver needs to decide what a content-drag drop means -- the origin and
/// destination entities a drop-target entity was already resolved for (see
/// InventoryTransferDropHandler.FindDropTargetEntityId), and the payload being dragged. Exactly one of
/// ItemStackInstanceId/MergedItemDefinitionId/CurrencyType is set, copied from the DragPayload the drag
/// started with.
/// </summary>
internal readonly record struct DragDropContext(
    int OriginEntityId,
    int DestinationEntityId,
    uint? ItemStackInstanceId,
    Guid? MergedItemDefinitionId,
    CurrencyType? CurrencyType);
