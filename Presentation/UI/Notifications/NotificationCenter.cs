using Engine.Events;
using Game.Notifications;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;

namespace Presentation.UI.Notifications;

/// <summary>
/// Owns the notification summary bar (one count per NotificationCategory, tiled horizontally)
/// and the currently-active notification popups. Minimizing an active notification
/// deliberately does NOT use the generic WindowMinimizeRestoreBehavior (which just shrinks a
/// window to its title bar in place) -- see NotificationMinimizeBehavior -- since a
/// minimized notification should read as "dismissed for now, reopen it later from the
/// summary bar", not "still on screen, just collapsed". Closing is driven by Window's real
/// Closed event rather than a public CloseNotification(Guid) callers had to remember to
/// call. Also subscribes to the buffered NotificationRequested event, so a Game-layer caller
/// (which can't reference this Presentation-layer type at all) can request a notification
/// without a direct reference.
/// </summary>
public sealed class NotificationCenter(WindowService windowService, EventBus eventBus)
{
    private static readonly Vector2 SummaryPosition = new(12, 30);
    private static readonly Vector2 SummarySize = new(300, 25);
    private static readonly Vector2 SummaryEntrySize = new(65, 21);

    private static readonly Vector2 ActiveNotificationBasePosition = new(200, 200);
    private static readonly Vector2 ActiveNotificationMaximumSize = new(400, 300);
    private const int ActiveNotificationStackOffset = 10;

    private readonly List<(NotificationCategory Category, TextWindow SummaryWindow, List<Notification> Notifications)> _unreadByCategory = [];
    private readonly List<(Window ActiveWindow, Notification Notification)> _activeNotifications = [];

    private Window _summaryWindow = null!;

    /// <summary>True while a System-category notification is active -- GameLoop gates the game's own Update on this.</summary>
    public bool HasBlockingNotification => _activeNotifications.Any(entry => entry.Notification.Category == NotificationCategory.System);

    public void Initialize()
    {
        _summaryWindow = windowService.CreateWindow<Window>(null, new WindowOptions
        {
            Hierarchy = new WindowHierarchyOptions { CanContainChildWindows = true, ChildWindowTileMode = WindowTileMode.Horizontal },
            Layout = new WindowLayoutOptions { DisplayMode = WindowDisplayMode.Fixed, IsTransparent = true, RelativePosition = SummaryPosition, Size = SummarySize },
            Chrome = new WindowChromeOptions { ShowTitle = false },
        });
        _summaryWindow.Initialize();

        foreach (var category in Enum.GetValues<NotificationCategory>())
        {
            var countWindow = windowService.CreateWindow<TextWindow>(_summaryWindow, new WindowOptions
            {
                Hierarchy = new WindowHierarchyOptions { CanContainChildWindows = false },
                Layout = new WindowLayoutOptions { DisplayMode = WindowDisplayMode.Fixed, Size = SummaryEntrySize, IsTransparent = false },
                Chrome = new WindowChromeOptions { ShowBorder = true, ShowTitle = false },
                Content = new WindowContentOptions { ContentColor = Color.LightGray },
                Text = new TextOptions { Text = $"{category}: 0" },
            });

            _unreadByCategory.Add((category, countWindow, []));
            _summaryWindow.AddChildWindow(countWindow);

            // Summary count windows are created once here and never pooled/reused (unlike
            // active notification windows), so this subscription lives for the game's
            // lifetime -- no unsubscribe-on-fire needed, unlike OnActiveNotificationClosed.
            countWindow.Clicked += _ => OpenNextNotification(category);
        }

        eventBus.Subscribe<NotificationRequested>(OnNotificationRequested);
    }

    public void LoadContent()
    {
        _summaryWindow.LoadContent();

        foreach (var (activeWindow, _) in _activeNotifications)
        {
            activeWindow.LoadContent();
        }
    }

    /// <summary>Notifications update even while the game is paused -- deliberately not gated by GameLoop's pause state.</summary>
    public void Update(GameTime gameTime)
    {
        eventBus.DispatchBuffered<NotificationRequested>();

        _summaryWindow.Update(gameTime);

        foreach (var (activeWindow, _) in _activeNotifications)
        {
            activeWindow.Update(gameTime);
        }
    }

    public void Draw(GameTime gameTime, GraphicsDevice graphicsDevice, SpriteBatch spriteBatch, Texture2D unitRectangle)
    {
        _summaryWindow.Draw(gameTime, graphicsDevice, spriteBatch, unitRectangle);

        foreach (var (activeWindow, _) in _activeNotifications)
        {
            activeWindow.Draw(gameTime, graphicsDevice, spriteBatch, unitRectangle);
        }
    }

    /// <summary>Returns true if the click landed on a notification window and was handled.</summary>
    public bool HandleClick(Point mousePosition)
    {
        // Newest (last in the list) first, not oldest first: ShowActive stacks each new
        // popup ActiveNotificationStackOffset further down-right and Draw (above) renders
        // them in list order, so a later popup is both on top on screen and last in the
        // list. Overlapping bounding rectangles are the common case here (the offset is only
        // 10px against popups often 100+px wide), so checking oldest-first let an older
        // popup's much larger rectangle claim clicks meant for a newer popup's own buttons --
        // the newer popup's buttons were effectively unclickable whenever an older popup was
        // still open behind it.
        for (var index = _activeNotifications.Count - 1; index >= 0; index--)
        {
            var activeWindow = _activeNotifications[index].ActiveWindow;
            if (activeWindow.WindowRectangle.Contains(mousePosition))
            {
                activeWindow.HandleClick(mousePosition);
                return true;
            }
        }

        if (_summaryWindow.WindowRectangle.Contains(mousePosition))
        {
            _summaryWindow.HandleClick(mousePosition);
            return true;
        }

        return false;
    }

    public Guid AddNotification(NotificationCategory category, string text, bool showImmediately = true)
    {
        var notification = new Notification(text, category);

        if (showImmediately)
        {
            ShowActive(notification);
        }
        else
        {
            UnreadListFor(category).Add(notification);
            RefreshUnreadSummary(category);
        }

        return notification.Id;
    }

    public void OpenNextNotification(NotificationCategory category)
    {
        var unreadList = UnreadListFor(category);
        if (unreadList.Count == 0)
        {
            return;
        }

        var notification = unreadList[0];
        unreadList.RemoveAt(0);
        RefreshUnreadSummary(category);

        ShowActive(notification);
    }

    /// <summary>
    /// Closes an active notification by id (e.g. auto-dismissing a quest notification once
    /// its objective completes). Goes through the same real Window.Close() -> Closed event
    /// path a user clicking the close button would, so cleanup only ever happens in one place.
    /// </summary>
    public bool CloseNotification(Guid notificationId)
    {
        var entry = _activeNotifications.FirstOrDefault(e => e.Notification.Id == notificationId);
        if (entry.ActiveWindow is null)
        {
            return false;
        }

        entry.ActiveWindow.Close();
        return true;
    }

    private void OnNotificationRequested(NotificationRequested requested) =>
        AddNotification(requested.Category, requested.Text, requested.ShowImmediately);

    private void ShowActive(Notification notification)
    {
        var offset = _activeNotifications.Count * ActiveNotificationStackOffset;
        // System notifications are uncloseable-except-by-resolution (closing IS the
        // resolution) and pause the game (see GameLoop, which checks HasBlockingNotification);
        // Quest notifications can be dismissed (see NotificationMinimizeBehavior) freely.
        var canMinimize = notification.Category != NotificationCategory.System;

        var notificationWindow = windowService.CreateWindow<TextWindow>(null, new WindowOptions
        {
            Hierarchy = new WindowHierarchyOptions { CanContainChildWindows = false },
            Layout = new WindowLayoutOptions
            {
                RelativePosition = ActiveNotificationBasePosition + new Vector2(offset, offset),
                MaximumSize = ActiveNotificationMaximumSize,
                DisplayMode = WindowDisplayMode.WrapContent,
            },
            Chrome = new WindowChromeOptions
            {
                ShowTitle = true,
                ShowTitleWhenMinimized = true,
                TitleText = notification.Category.ToString(),
                ShowBorder = true,
                CanUserClose = true,
                // Never the built-in minimize/restore chrome -- see NotificationMinimizeBehavior,
                // attached below instead of via Window.Initialize()'s CanUserMinimize path.
                CanUserMinimize = false,
            },
            Text = new TextOptions { Text = notification.Text },
        });

        notificationWindow.Closed += OnActiveNotificationClosed;
        _activeNotifications.Add((notificationWindow, notification));
        notificationWindow.Initialize();

        // Attached after Initialize() (which already attached WindowCloseBehavior, since
        // CanUserClose is true) so the dismiss button lands to the close button's left, the
        // same right-to-left ordering every other window's minimize/restore button uses.
        if (canMinimize)
        {
            notificationWindow.AddChromeBehavior(new NotificationMinimizeBehavior(() => MinimizeNotification(notification)));
        }
    }

    /// <summary>
    /// The dismiss action behind NotificationMinimizeBehavior's button: return the
    /// notification to its category's unread queue (so it can be reopened later from the
    /// summary bar, exactly like a never-shown notification added via
    /// AddNotification(showImmediately: false)) and close the popup through the same real
    /// Window.Close() path CloseNotification already uses.
    /// </summary>
    private void MinimizeNotification(Notification notification)
    {
        UnreadListFor(notification.Category).Add(notification);
        RefreshUnreadSummary(notification.Category);

        CloseNotification(notification.Id);
    }

    private void OnActiveNotificationClosed(Window closedWindow)
    {
        // Pooled windows get reused for unrelated future notifications, so this handler must
        // detach itself -- otherwise it stays subscribed and keeps firing (against a stale
        // _activeNotifications lookup that will no longer find a match) every time the same
        // underlying Window instance is closed again for a later notification.
        closedWindow.Closed -= OnActiveNotificationClosed;

        var index = _activeNotifications.FindIndex(entry => entry.ActiveWindow == closedWindow);
        if (index >= 0)
        {
            _activeNotifications.RemoveAt(index);
        }
    }

    private List<Notification> UnreadListFor(NotificationCategory category) =>
        _unreadByCategory.First(entry => entry.Category == category).Notifications;

    private void RefreshUnreadSummary(NotificationCategory category)
    {
        var entry = _unreadByCategory.First(e => e.Category == category);
        entry.SummaryWindow.UpdateText($"{category}: {entry.Notifications.Count}");
    }
}
