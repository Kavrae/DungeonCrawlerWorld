namespace Engine.ECS.Relationships;

/// <summary>Why a source stopped being linked to a target.</summary>
public enum UnlinkReason : byte
{
    /// <summary>The link was removed from a source that lives on.</summary>
    Unlinked,

    /// <summary>The link was rewritten to name another target.</summary>
    Retargeted,

    /// <summary>The source is being destroyed.</summary>
    SourceDestroyed,

    /// <summary>The target is being destroyed; the relationship's TargetDestroyedPolicy decides what happens to the source next.</summary>
    TargetDestroyed,
}
