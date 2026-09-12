namespace Engine.ECS.Components.Stores;

/// <summary>A component pool's one-and-only "a timer wheel drives me" claim.</summary>
/// <remarks>
/// A wheel's scheduling state lives on the component (Engine.ECS.Components.IScheduledTimer's
/// TimerWheelMark), not in the wheel, so two wheels over one pool fight over it silently:
/// whichever one's ComponentChanged observer runs first marks the timer scheduled, and the second
/// reads that mark as its own and schedules nothing at all -- its timers simply never fire, with
/// no error anywhere to say so. A wheel also observes the pool for the pool's whole lifetime
/// (there is no unsubscribe), so the claim is one-way and never released.
///
/// Enforced here rather than left to a doc comment: the second wheel throws as it is constructed,
/// which is composition time -- a wiring mistake fails the build-up, not a play session.
/// </remarks>
/// <cleanupVersion>1</cleanupVersion>
internal struct TimerWheelClaim
{
    private bool _claimed;

    /// <summary>Takes the claim for a wheel being constructed over a pool of <paramref name="componentType"/>, throwing if one already holds it.</summary>
    public void Claim(Type componentType)
    {
        if (_claimed)
        {
            throw new InvalidOperationException(
                $"A timer wheel already drives the {componentType.Name} pool. Timer scheduling state lives on the component, so a second wheel over the same pool would never schedule anything -- share the one wheel instead of building another.");
        }

        _claimed = true;
    }
}
