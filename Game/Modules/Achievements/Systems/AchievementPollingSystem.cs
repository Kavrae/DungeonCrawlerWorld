using Engine.ECS.Systems;

namespace Game.Modules.Achievements.Systems;

/// <summary>
/// Runs every polled achievement condition once per frame (see AchievementTriggerContext.
/// SubscribePolled) -- the handful of achievements whose unlock condition is a standing state check
/// rather than a discrete event. AchievementModule only registers this system at all if at least one
/// achievement actually called SubscribePolled. StripeCount 1: the shared list is small (at most a
/// few polled achievements ever), so there's no population to stripe across the way entity-indexed
/// systems do. A condition is removed from the list once it returns true -- it has already unlocked
/// (Unlock itself is idempotent besides), so there's no reason to keep re-checking it every frame.
/// </summary>
public sealed class AchievementPollingSystem(List<Func<bool>> polledConditions) : ISystem
{
    public byte StripeCount => 1;

    public void Update(EngineTime time, byte stripeIndex)
    {
        for (var i = polledConditions.Count - 1; i >= 0; i--)
        {
            if (polledConditions[i]())
            {
                polledConditions.RemoveAt(i);
            }
        }
    }
}
