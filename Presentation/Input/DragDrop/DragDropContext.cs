using System;
using Engine.ECS.Components;
using Game.Modules.Currency;

namespace Presentation.Input.DragDrop;

/// <summary>
/// Everything an IDragDropResolver needs to decide what a content-drag drop means -- the origin and
/// destination entities a drop-target entity was already resolved for (see
/// UiInputController.FindDropTargetEntityId), and the payload being dragged. Exactly one of
/// ItemStackInstanceId/MergedItemDefinitionId/CurrencyType is set, mirroring the mutually-exclusive
/// _contentDrag* fields UiInputController captures at drag-start.
/// </summary>
internal readonly record struct DragDropContext(
    ComponentManager ComponentManager,
    int OriginEntityId,
    int DestinationEntityId,
    Guid? ItemStackInstanceId,
    Guid? MergedItemDefinitionId,
    CurrencyType? CurrencyType);
