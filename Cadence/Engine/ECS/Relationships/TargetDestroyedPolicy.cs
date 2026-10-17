namespace Engine.ECS.Relationships;

/// <summary>What happens to an entity's sources when it is destroyed, declared once per relationship type.</summary>
public enum TargetDestroyedPolicy : byte
{
    /// <summary>The sources lose their link and live on.</summary>
    UnlinkSources,

    /// <summary>The sources are destroyed too, before the target's own EntityDestroying handlers run.</summary>
    DestroySources,
}
