namespace Engine.ECS.Components;

/// <summary>A pool that raises ComponentChanged and ComponentRemoving, which ComponentManager.ObserveChanges can make Observed after it is registered.</summary>
internal interface IObservableComponentPool
{
    void MarkObserved();
}
