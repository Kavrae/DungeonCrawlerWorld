using Engine.Events;

namespace Game.Notifications;

/// <summary>
/// Requests that a notification be shown, without the publisher needing a NotificationCenter
/// reference (Presentation-layer, unreachable from Game). Buffered, not immediate: showing a
/// notification a frame late is imperceptible, and NotificationCenter already has a natural
/// per-frame drain point (the top of its own Update). Mirrors NotificationCenter.AddNotification's
/// existing parameters exactly, so it's a drop-in alternate entry point, not a new concept.
/// </summary>
public sealed record NotificationRequested(NotificationCategory Category, string Text, bool ShowImmediately) : IBufferedEvent;
