using System.Runtime.CompilerServices;
using Engine.ECS.Systems;

namespace Game.Modules.Health.Components;

/// <summary>What has happened to one entity's body parts: their current health, which are disabled, and how long each is locked out of regen.</summary>
/// <remarks>
/// The per-entity half of a body plan. The parts themselves -- name, type, vertical position,
/// maximum health, whether they are vital -- are BodyPartTemplates on the race definition, shared by
/// every creature of that race (see EntityBodyParts). Only what a fight changes lives here, and only
/// once something has: an untouched creature holds no component at all and reads as every part at
/// full health. That is what turned 167,937 per-part components (26 MB, 3,439 distinct values) into
/// one component for each creature that has actually been hurt.
///
/// Indexed by part id, which is the part's index in its entity's own template list, so the arrays
/// line up with EntityBodyParts' enumeration order. MaximumParts caps a body plan: nothing near it
/// exists today (Goblin and Human have 11), and a definition that exceeded it would silently lose
/// parts, so EntityBodyParts throws instead.
/// </remarks>
public struct BodyPartStateComponent
{
    /// <summary>The most body parts one entity can have.</summary>
    public const int MaximumParts = 16;

    [InlineArray(MaximumParts)]
    private struct HealthValues
    {
        private float _element0;
    }

    [InlineArray(MaximumParts)]
    private struct FrameValues
    {
        private uint _element0;
    }

    private HealthValues _currentHealth;
    private FrameValues _regenLockedUntilFrame;
    private ushort _disabled;

    public readonly float CurrentHealthOf(int partId) => _currentHealth[partId];

    public void SetCurrentHealth(int partId, float value) => _currentHealth[partId] = value;

    public readonly bool IsDisabled(int partId) => (_disabled & Mask(partId)) != 0;

    /// <summary>Every disabled part as a bit per part id -- what selection reads instead of a view per part.</summary>
    public readonly ushort DisabledMask => _disabled;

    public void SetDisabled(int partId, bool disabled)
    {
        if (disabled)
        {
            _disabled |= Mask(partId);
        }
        else
        {
            _disabled = (ushort)(_disabled & ~Mask(partId));
        }
    }

    public readonly uint RegenLockedUntilFrameOf(int partId) => _regenLockedUntilFrame[partId];

    public void SetRegenLockedUntilFrame(int partId, uint frame) => _regenLockedUntilFrame[partId] = frame;

    /// <summary>True while the part is still inside its regen lockout as of now.</summary>
    public readonly bool IsRegenLockedOut(int partId, long now) => !FrameDeadline.IsReached(_regenLockedUntilFrame[partId], now);

    private static ushort Mask(int partId) => (ushort)(1 << partId);
}
