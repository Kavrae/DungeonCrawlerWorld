namespace Engine.Diagnostics;

/// <summary>One named EngineHooks channel, holding at most one listener.</summary>
/// <cleanupVersion>1</cleanupVersion>
public sealed class EngineHookChannel<TListener>(string channelName) where TListener : class
{
    /// <summary>The subscribed listener; null when nothing listens, which an emit site checks and does nothing else.</summary>
    public TListener? Listener { get; private set; }

    /// <summary>Makes listener this channel's listener until the returned subscription is disposed.</summary>
    /// <exception cref="InvalidOperationException">The channel already has a listener.</exception>
    public IDisposable Subscribe(TListener listener)
    {
        ArgumentNullException.ThrowIfNull(listener);

        if (Listener is { } existingListener)
        {
            throw new InvalidOperationException($"EngineHooks.{channelName} already has a listener ({existingListener.GetType().Name}).");
        }

        Listener = listener;
        return new ChannelSubscription(this, listener);
    }

    private sealed class ChannelSubscription(EngineHookChannel<TListener> channel, TListener listener) : IDisposable
    {
        private bool _isDisposed;

        public void Dispose()
        {
            if (_isDisposed)
            {
                return;
            }

            _isDisposed = true;
            if (ReferenceEquals(channel.Listener, listener))
            {
                channel.Listener = null;
            }
        }
    }
}
