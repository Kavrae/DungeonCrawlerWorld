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
/// NotificationRequestedEvent, so a Game-layer caller (which can't reference this
/// Presentation-layer type at all) can request a notification without a direct reference.
/// </summary>
public sealed class NotificationCenter(ElementPoolService elementPoolService, EventBus eventBus, UiLayerStack layers, ContextMenuController contextMenuController)
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
    /// shrinking to just its icon). Width is 117 (78 * 1.5) to keep pace with TextWindow.
    /// ContentFont's own 8 -> 12 (50%) increase -- otherwise the widest label ("Achievement: 0")
    /// would overflow the tile at the larger font.
    /// </summary>
    private static readonly Vector2 SummaryEntrySize = new(117, HudMetrics.EntrySize.Y);

    private static readonly Vector2 ActiveNotificationBasePosition = new(200, 200);
    private static readonly Vector2 ActiveNotificationMaximumSize = new(640, 176);
    private const int ActiveNotificationStackOffset = 10;

    private readonly List<(NotificationCategory Category, TextWindow SummaryWindow, List<Notification> Notifications)> _unreadByCategory = [];
    private readonly List<(Window ActiveWindow, Notification Notification)> _activeNotifications = [];

    private Folder _folder = null!;

    /// <summary>
    /// True while a System-category notification is active. GameLoop's own pause check now reads
    /// UiLayerStack.IsMenuModeActive instead (see ShowActive's OpenMenuWindow call) -- this
    /// property is no longer what drives that, but stays as an independently useful, tested
    /// query of the same underlying state. A plain loop over the concrete List&lt;&gt; avoids both
    /// the per-frame closure and the boxed enumerator Enumerable.Any() would otherwise cost
    /// through IEnumerable&lt;&gt; dispatch.
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
    /// ShowActive. GameLoop subscribes once it has a UiInputController (which doesn't exist
    /// yet while ShellBootstrapper.Build, and so NotificationCenter.Initialize, are still
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

        // Initialized before any count window is added -- Element.Initialize/Measure/Arrange now
        // tolerate a child being added at any point relative to its parent's own Initialize and
        // DisplayMode (see Element.Measure's and Element.Initialize's own Minimized guards), so
        // there's no ordering constraint here anymore; this order was chosen to match the rest
        // of the codebase's convention of a control's own Initialize running before its children
        // are attached (see Window.OnChildrenInitialized/GridControl/AbilityScoreWindow).
        _folder.Initialize();
        layers.Add(UiLayer.DynamicHud, _folder);

        // Opening another category's notification (or re-opening this one) from the summary
        // folder is a normal part of the menu-mode workflow (see UiLayerStack.MarkMenuModeExempt's
        // own doc comment) -- not something a blocking System notification should itself block.
        layers.MarkMenuModeExempt(_folder);

        using (_folder.BeginLayoutBatch())
        {
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
                countWindow.OnRightClicked = position =>
                {
                    var hasUnread = UnreadListFor(category).Count > 0;
                    contextMenuController.Open(new Vector2(position.X, position.Y),
                    [
                        new ContextMenuOption("Open", null, hasUnread, () => OpenNextNotification(category)),
                        new ContextMenuOption("Open All", null, hasUnread, () => OpenAllNotifications(category)),
                    ]);
                };
            }
        }

        eventBus.Subscribe<NotificationRequestedEvent>(OnNotificationRequested);
    }

    /// <summary>
    /// Notifications update even while the game is paused (see GameLoop) -- true today because
    /// ShellContext's own per-layer Update loop over UiLayer.DynamicHud is unconditional,
    /// the same way it already is for UiLayer.Base, not because of anything here. This method
    /// only does the notification-domain part: dispatching buffered NotificationRequestedEvent
    /// events, which must run before GameLoop's pause check reads UiLayerStack.IsMenuModeActive
    /// (a System notification published this same frame needs to have already called
    /// OpenMenuWindow, in ShowActive, before that check).
    /// </summary>
    public void Update(GameTime gameTime) => eventBus.DispatchBuffered<NotificationRequestedEvent>();

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

    /// <summary>Opens every currently-unread notification in category, one popup per call (see OpenNextNotification) -- each stacks visually via ActiveNotificationStackOffset, the same as opening them one at a time by hand. Scoped to this one category only, not every category -- the summary badge being right-clicked is itself category-specific.</summary>
    public void OpenAllNotifications(NotificationCategory category)
    {
        while (UnreadListFor(category).Count > 0)
        {
            OpenNextNotification(category);
        }
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

    private void OnNotificationRequested(NotificationRequestedEvent requested) =>
        AddNotification(requested.Category, requested.Text, requested.ShowImmediately, requested.Title, requested.Achievement);

    private void ShowActive(Notification notification)
    {
        var offset = _activeNotifications.Count * ActiveNotificationStackOffset;
        // System notifications are uncloseable-except-by-resolution (closing IS the
        // resolution), open as a menu window (see below), and pause the game as a result (see
        // GameLoop, which checks UiLayerStack.IsMenuModeActive); Quest notifications can be
        // dismissed (see NotificationMinimizeBehavior) freely.
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
        layers.Add(UiLayer.DynamicHud, notificationWindow);

        // A System notification is the menu-window case -- see
        // UiLayerStack.OpenMenuWindow/GameLoop's pause check. Quest/Achievement notifications
        // stay ordinary DynamicHud popups.
        if (notification.Category == NotificationCategory.System)
        {
            layers.OpenMenuWindow(notificationWindow);
        }

        ActiveNotificationOpened?.Invoke(notificationWindow);

        // Attached after Initialize() (which already attached WindowCloseBehavior, since
        // CanUserClose is true) so the dismiss button lands to the close button's left, the
        // same right-to-left ordering every other window's minimize/restore button uses.
        if (canMinimize)
        {
            notificationWindow.AddChromeBehavior(new NotificationMinimizeBehavior(() => MinimizeNotification(notification)));
        }

        notificationWindow.OnRightClicked = position => contextMenuController.Open(new Vector2(position.X, position.Y), BuildRightClickMenu(notification, canMinimize));
    }

    /// <summary>Close/Close All always (mirrors what every popup's own Close button already permits -- CanUserClose is unconditionally true for every category, see ShowActive); Minimize/Minimize All only for a non-System notification, the same canMinimize gate ShowActive already computes for the dedicated minimize button.</summary>
    private List<ContextMenuOption> BuildRightClickMenu(Notification notification, bool canMinimize)
    {
        List<ContextMenuOption> options =
        [
            new ContextMenuOption("Close", null, Enabled: true, () => CloseNotification(notification.Id)),
            new ContextMenuOption("Close All", null, Enabled: true, CloseAllNotifications),
        ];

        if (canMinimize)
        {
            options.Add(new ContextMenuOption("Minimize", null, Enabled: true, () => MinimizeNotification(notification)));
            options.Add(new ContextMenuOption("Minimize All", null, Enabled: true, MinimizeAllNotifications));
        }

        return options;
    }

    /// <summary>Closes every active notification popup -- no more permissive than what each one's own Close button already does today (CanUserClose is unconditionally true for every category, System included, see ShowActive).</summary>
    public void CloseAllNotifications()
    {
        foreach (var entry in _activeNotifications.ToArray())
        {
            entry.ActiveWindow.Close();
        }
    }

    /// <summary>Minimizes (see MinimizeNotification) every active *non-System* notification -- System notifications are uncloseable-except-by-resolution (see ShowActive's own canMinimize computation) and are deliberately skipped here entirely, not merely left without their own Minimize button.</summary>
    public void MinimizeAllNotifications()
    {
        foreach (var entry in _activeNotifications.ToArray())
        {
            if (entry.Notification.Category != NotificationCategory.System)
            {
                MinimizeNotification(entry.Notification);
            }
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
        var index = _activeNotifications.FindIndex(entry => entry.ActiveWindow == closedWindow);
        if (index >= 0)
        {
            _activeNotifications.RemoveAt(index);
        }

        layers.Remove(UiLayer.DynamicHud, closedWindow);
        layers.CloseMenuWindow(closedWindow); // No-op for a non-System notification, which was never opened as a menu window.

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