using Engine.Events;
using Game.Notifications;
using Microsoft.Xna.Framework;

namespace Presentation.UI.Notifications;

/// <summary>
/// Owns the notification summary Folder (one count badge per NotificationCategory, tiled
/// vertically beneath it once expanded) and the currently-active notification popups.
/// Minimizing an active notification deliberately does NOT use the generic
/// WindowMinimizeRestoreBehavior (which just shrinks a window to its title bar in place) --
/// see NotificationMinimizeBehavior -- since a minimized notification should read as
/// "dismissed for now, reopen it later from the summary Folder", not "still on screen, just
/// collapsed". Closing is driven by Window's real Closed event rather than a public
/// CloseNotification(Guid) callers had to remember to call. Also subscribes to the buffered
/// NotificationRequested event, so a Game-layer caller (which can't reference this
/// Presentation-layer type at all) can request a notification without a direct reference.
/// </summary>
public sealed class NotificationCenter(ElementPoolService elementPoolService, EventBus eventBus, List<Element> dynamicHudElements)
{
    private static readonly Vector2 FolderPosition = HudMetrics.Margin;

    /// <summary>
    /// Generous ceiling for the Folder's WrapContent sizing -- a root WrapContent window's own
    /// MaximumSize is otherwise left at Vector2.Zero (see Window.BuildWindow: it only falls
    /// back to a parent's ContentSize or an explicit Layout.Size/MaximumSize, and a root Folder
    /// has neither a parent nor a fixed Size), which would zero-cap every child's own Measure
    /// pass forever, since a root window's MaximumSize is otherwise never recomputed after
    /// BuildWindow. Comfortably larger than the widest/tallest the category stack can ever be.
    /// Public: InventoryFolderController positions its own folder beneath this one, derived
    /// from this ceiling rather than a second, silently-driftable duplicate of the same number.
    /// </summary>
    public static readonly Vector2 FolderMaximumSize = new(200, 400);

    /// <summary>
    /// Deliberately its own constant, not HudMetrics.EntrySize (65px wide -- sized for short
    /// hotbar/health-bar-style content elsewhere). Also drives the Folder's own width, both
    /// expanded (RecalculateWrapContentWindowSize fits its title/content to the widest child)
    /// and collapsed (Folder.RecalculateMinimizedWindowSize matches that same width instead of
    /// shrinking to just its icon).
    /// </summary>
    private static readonly Vector2 SummaryEntrySize = new(78, HudMetrics.EntrySize.Y);

    private static readonly Vector2 ActiveNotificationBasePosition = new(200, 200);
    private static readonly Vector2 ActiveNotificationMaximumSize = new(640, 176);
    private const int ActiveNotificationStackOffset = 10;

    private readonly List<(NotificationCategory Category, TextWindow SummaryWindow, List<Notification> Notifications)> _unreadByCategory = [];
    private readonly List<(Window ActiveWindow, Notification Notification)> _activeNotifications = [];

    private Folder _folder = null!;

    /// <summary>
    /// True while a System-category notification is active -- GameLoop gates the game's own
    /// Update on this, so it's evaluated every frame; a plain loop over the concrete List&lt;&gt;
    /// avoids both the per-frame closure and the boxed enumerator Enumerable.Any() would
    /// otherwise cost through IEnumerable&lt;&gt; dispatch.
    /// </summary>
    public bool HasBlockingNotification
    {
        get
        {
            foreach (var entry in _activeNotifications)
            {
                if (entry.Notification.Category == NotificationCategory.System)
                {
                    return true;
                }
            }

            return false;
        }
    }

    /// <summary>
    /// Raised whenever a notification popup actually shows on screen (a fresh one via
    /// AddNotification(showImmediately: true), or a queued one via OpenNextNotification) -- see
    /// ShowActive. GameLoop subscribes once it has a GameInputController (which doesn't exist
    /// yet while GameShellBootstrapper.Build, and so NotificationCenter.Initialize, are still
    /// running) and focuses the new popup, the same composition-root role its existing
    /// QuestComposerOpened subscription plays for the quest-composer popup.
    /// </summary>
    public event Action<Window>? ActiveNotificationOpened;

    public void Initialize()
    {
        _folder = elementPoolService.CreateElement<Folder>(null, new ElementOptions
        {
            Layout = new ElementLayoutOptions { RelativePosition = FolderPosition, MaximumSize = FolderMaximumSize, DisplayMode = ElementDisplayMode.WrapContent },
            Chrome = new ElementChromeOptions { ShowBorder = true, BorderStyle = BorderStyle.Outset, CanUserFocus = false },
            Folder = new FolderOptions { SpriteName = "AchievementCenter", FallbackGlyph = "★" },
        });

        foreach (var category in Enum.GetValues<NotificationCategory>())
        {
            var countWindow = elementPoolService.CreateElement<TextWindow>(_folder, new ElementOptions
            {
                Hierarchy = new ElementHierarchyOptions { CanContainChildren = false },
                Layout = new ElementLayoutOptions { DisplayMode = ElementDisplayMode.Fixed, Size = SummaryEntrySize, IsTransparent = false },
                Chrome = new ElementChromeOptions { ShowBorder = true, ShowTitle = false },
                Content = new ElementContentOptions { ContentColor = Color.LightGray },
                Text = new TextOptions { Text = $"{category}: 0" },
            });

            _unreadByCategory.Add((category, countWindow, []));
            _folder.AddChild(countWindow);

            // Summary count windows are created once here and never pooled/reused (unlike
            // active notification windows), so this subscription lives for the game's
            // lifetime -- no unsubscribe-on-fire needed, unlike OnActiveNotificationClosed.
            countWindow.Clicked += _ => OpenNextNotification(category);
        }

        // Initialized last -- see the WrapContent comment above for why this must run only
        // after every child is already correctly tiled.
        _folder.Initialize();
        dynamicHudElements.Add(_folder);

        eventBus.Subscribe<NotificationRequested>(OnNotificationRequested);
    }

    /// <summary>
    /// Notifications update even while the game is paused (see GameLoop) -- true today because
    /// GameShellContext's own per-tier Update loop over DynamicHudWindows is unconditional,
    /// the same way it already is for BaseWindows, not because of anything here. This method
    /// only does the notification-domain part: dispatching buffered NotificationRequested
    /// events, which must run before GameLoop's pause check reads HasBlockingNotification (a
    /// notification published this same frame needs to be reflected before that check).
    /// </summary>
    public void Update(GameTime gameTime) => eventBus.DispatchBuffered<NotificationRequested>();

    public Guid AddNotification(NotificationCategory category, string text, bool showImmediately = true, string? title = null, AchievementNotificationDetails? achievement = null)
    {
        var notification = new Notification(text, category, title, achievement);

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
        AddNotification(requested.Category, requested.Text, requested.ShowImmediately, requested.Title, requested.Achievement);

    private void ShowActive(Notification notification)
    {
        var offset = _activeNotifications.Count * ActiveNotificationStackOffset;
        // System notifications are uncloseable-except-by-resolution (closing IS the
        // resolution) and pause the game (see GameLoop, which checks HasBlockingNotification);
        // Quest notifications can be dismissed (see NotificationMinimizeBehavior) freely.
        var canMinimize = notification.Category != NotificationCategory.System;

        var notificationWindow = elementPoolService.CreateElement<TextWindow>(null, new ElementOptions
        {
            Hierarchy = new ElementHierarchyOptions { CanContainChildren = false },
            Layout = new ElementLayoutOptions
            {
                RelativePosition = ActiveNotificationBasePosition + new Vector2(offset, offset),
                MaximumSize = ActiveNotificationMaximumSize,
                DisplayMode = ElementDisplayMode.WrapContent,
            },
            Chrome = new ElementChromeOptions
            {
                ShowTitle = true,
                ShowTitleWhenMinimized = true,
                TitleText = notification.Title ?? notification.Category.ToString(),
                ShowBorder = true,
                CanUserClose = true,
                CanUserMinimize = false,
                CanUserMove = true,
                CanUserScrollVertical = true
            },
            Text = new TextOptions { Text = BuildDisplayText(notification) },
        });

        notificationWindow.Closed += OnActiveNotificationClosed;
        _activeNotifications.Add((notificationWindow, notification));
        notificationWindow.Initialize();
        dynamicHudElements.Add(notificationWindow);
        ActiveNotificationOpened?.Invoke(notificationWindow);

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

    private void OnActiveNotificationClosed(Element closedWindow)
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

        dynamicHudElements.Remove(closedWindow);

        // Closing the last unread notification auto-tidies the HUD back down -- SetWindowDisplayMode
        // no-ops if the Folder is already Minimized, so this is safe to call unconditionally.
        if (_unreadByCategory?.Sum(category => category.Notifications.Count) == 0)
        {
            _folder.SetDisplayMode(ElementDisplayMode.Minimized);
        }
    }

    /// <summary>
    /// TextWindow renders one flat string with one font -- no per-section styling exists yet
    /// -- so an achievement's structured fields (kept separate on Notification/AchievementNotificationDetails
    /// for future consumers, e.g. an achievement log) get flattened into one displayed block
    /// here. Sections are joined with " \n\n " (space-padded) rather than bare "\n\n": TextWindow's
    /// word-wrap only splits on spaces (see StringUtility.WordWrapWithHyphenation), so an
    /// un-padded "\n\n" would get glued onto the end of the previous word as one unbroken chunk
    /// instead of standing alone as its own forced-break token.
    /// </summary>
    private static string BuildDisplayText(Notification notification)
    {
        if (notification.Achievement is not { } achievement)
        {
            return notification.Text;
        }

        var lootboxLine = achievement.LootboxLabel is { } lootboxLabel
            ? $"Lootbox: {lootboxLabel}."
            : "Lootbox: None.";

        return $"{notification.Text} \n\n Requirement fulfilled: {achievement.RequirementText} \n\n {lootboxLine} \n\n Reward: {achievement.RewardText}";
    }

    private List<Notification> UnreadListFor(NotificationCategory category) =>
        _unreadByCategory.First(entry => entry.Category == category).Notifications;

    /// <summary>
    /// Refreshes a category badge's text alongside both glow conditions this same unread-count
    /// change affects: the badge itself glows while its own category has a queued-unread
    /// notification, and the Folder glows while *any* category does.
    /// </summary>
    private void RefreshUnreadSummary(NotificationCategory category)
    {
        var entry = _unreadByCategory.First(e => e.Category == category);
        entry.SummaryWindow.UpdateText($"{category}: {entry.Notifications.Count}");
        entry.SummaryWindow.SetGlow(entry.Notifications.Count > 0, Color.Gold);

        _folder.SetGlow(_unreadByCategory.Any(e => e.Notifications.Count > 0), Color.Gold);
    }
}