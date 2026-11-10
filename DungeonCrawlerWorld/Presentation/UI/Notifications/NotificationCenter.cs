using Engine.Events;
using Game.Notifications;
using Microsoft.Xna.Framework;
using Presentation.UI.Chrome;
using UIEngine.UI;
using UIEngine.UI.ColorPalettes;

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
    /// <summary>The Folder's and the badges' glow while they have unread notifications: five 2-second pulses from each new one, then steady.</summary>
    private static readonly GlowAnimation UnreadNotificationGlowPulse = GlowAnimation.Pulse(TimeSpan.FromSeconds(2));

    /// <summary>A category's unread queue and the badge that counts it.</summary>
    private sealed record UnreadCategory(NotificationCategory Category, TextWindow SummaryWindow, List<Notification> Notifications);

    private readonly List<UnreadCategory> _unreadByCategory = [];
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
            Layout = new ElementLayoutOptions { RelativePosition = NotificationChrome.FolderPosition, MaximumSize = NotificationChrome.FolderMaximumSize, DisplayMode = ElementDisplayMode.WrapContent },
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
        _folder.TooltipText = "Achievements";
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
                    Layout = new ElementLayoutOptions { DisplayMode = ElementDisplayMode.Fixed, Size = NotificationChrome.SummaryEntrySize, IsTransparent = false },
                    Chrome = new ElementChromeOptions { ShowBorder = true, ShowTitle = false },
                    Content = new ElementContentOptions { ContentColor = WindowPalette.PanelContentColor },
                    Text = new TextOptions { Text = $"{category}: 0" },
                });

                _unreadByCategory.Add(new UnreadCategory(category, countWindow, []));
                _folder.AddChild(countWindow);

                // Summary count windows are created once here and never pooled/reused (unlike
                // active notification windows), so these subscriptions live for the game's
                // lifetime -- no unsubscribe-on-fire needed, unlike OnActiveNotificationClosed.
                // A click is the menu's "Open" and a double-click its "Open All": the first click
                // opens one, the second the rest, so a double-click opens every one.
                countWindow.Clicked += _ => OpenNextNotification(category);
                countWindow.DoubleClicked += _ => OpenAllNotifications(category);
                countWindow.OnRightClicked = position =>
                {
                    var hasUnread = UnreadCategoryFor(category).Notifications.Count > 0;
                    contextMenuController.Open(new Vector2(position.X, position.Y),
                    [
                        new DropdownOption("Open", null, hasUnread, () => OpenNextNotification(category)),
                        new DropdownOption("Open All", null, hasUnread, () => OpenAllNotifications(category)),
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
    public void Update() => eventBus.DispatchBuffered<NotificationRequestedEvent>();

    public Guid AddNotification(NotificationCategory category, string text, bool showImmediately = true, string? title = null, AchievementNotificationDetails? achievement = null)
    {
        var notification = new Notification(text, category, title, achievement);

        if (showImmediately)
        {
            ShowActive(notification);
        }
        else
        {
            var unread = UnreadCategoryFor(category);
            unread.Notifications.Add(notification);
            RefreshUnreadSummary(unread);
            _folder.RestartGlowPulse();
            unread.SummaryWindow.RestartGlowPulse();
        }

        return notification.Id;
    }

    public void OpenNextNotification(NotificationCategory category)
    {
        var unread = UnreadCategoryFor(category);
        if (unread.Notifications.Count == 0)
        {
            return;
        }

        var notification = unread.Notifications[0];
        unread.Notifications.RemoveAt(0);
        RefreshUnreadSummary(unread);

        ShowActive(notification);
    }

    /// <summary>Opens every currently-unread notification in category, one popup per call (see OpenNextNotification) -- each stacks visually via ActiveNotificationStackOffset, the same as opening them one at a time by hand. Scoped to this one category only, not every category -- the summary badge being right-clicked is itself category-specific.</summary>
    public void OpenAllNotifications(NotificationCategory category)
    {
        var unread = UnreadCategoryFor(category);
        while (unread.Notifications.Count > 0)
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
        var offset = _activeNotifications.Count * NotificationChrome.ActiveNotificationStackOffset;
        // System notifications are uncloseable-except-by-resolution (closing IS the
        // resolution); Quest and Achievement notifications can be dismissed (see
        // NotificationMinimizeBehavior) freely.
        var canMinimize = notification.Category != NotificationCategory.System;

        var notificationWindow = elementPoolService.CreateElement<TextWindow>(null, new ElementOptions
        {
            Hierarchy = new ElementHierarchyOptions { CanContainChildren = false },
            Layout = new ElementLayoutOptions
            {
                RelativePosition = NotificationChrome.ActiveNotificationBasePosition + new Vector2(offset, offset),
                MaximumSize = NotificationChrome.ActiveNotificationMaximumSize,
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
            Text = new TextOptions { Text = BuildDisplayText(notification), TextColor = WindowPalette.TitleTextColor },
        });

        notificationWindow.Closed += OnActiveNotificationClosed;
        _activeNotifications.Add((notificationWindow, notification));
        notificationWindow.Initialize();

        if (notificationWindow.MaxScrollOffset == Vector2.Zero)
        {
            notificationWindow.CanUserScrollVertical = false;
        }

        // Every popup is a menu window from the moment it opens, so the game pauses while it is
        // read (see GameLoop's pause check) and it is never left behind the dim when another
        // menu window opens later.
        layers.Add(UiLayer.DynamicHud, notificationWindow);
        layers.OpenMenuWindow(notificationWindow);

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
    private List<DropdownOption> BuildRightClickMenu(Notification notification, bool canMinimize)
    {
        List<DropdownOption> options =
        [
            new DropdownOption("Close", null, Enabled: true, () => CloseNotification(notification.Id)),
            new DropdownOption("Close All", null, Enabled: true, CloseAllNotifications),
        ];

        if (canMinimize)
        {
            options.Add(new DropdownOption("Minimize", null, Enabled: true, () => MinimizeNotification(notification)));
            options.Add(new DropdownOption("Minimize All", null, Enabled: true, MinimizeAllNotifications));
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
        var unread = UnreadCategoryFor(notification.Category);
        unread.Notifications.Add(notification);
        RefreshUnreadSummary(unread);

        CloseNotification(notification.Id);
    }

    private void OnActiveNotificationClosed(Element closedWindow)
    {
        var index = _activeNotifications.FindIndex(entry => entry.ActiveWindow == closedWindow);
        Notification? closedNotification = null;
        if (index >= 0)
        {
            closedNotification = _activeNotifications[index].Notification;
            _activeNotifications.RemoveAt(index);
        }

        layers.Remove(UiLayer.DynamicHud, closedWindow);
        layers.CloseMenuWindow(closedWindow);

        // Minimizing closes the popup too, but puts the notification back in its unread queue first.
        if (closedNotification?.Achievement is { } achievement && !UnreadCategoryFor(closedNotification.Category).Notifications.Contains(closedNotification))
        {
            eventBus.Publish(new AchievementNotificationDismissedEvent(achievement.AchievementId));
        }

        // Closing the last unread notification auto-tidies the HUD back down -- SetWindowDisplayMode
        // no-ops if the Folder is already Minimized, so this is safe to call unconditionally.
        if (!HasAnyUnread())
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

        // RewardText only explains an unusual reward, why a particular one was given, or why none was --
        // an achievement that just grants its loot box has none, and gets no Reward line at all.
        var rewardSection = string.IsNullOrWhiteSpace(achievement.RewardText)
            ? string.Empty
            : $" \n\n Reward: {achievement.RewardText}";

        return $"{notification.Text} \n\n Requirement fulfilled: {achievement.RequirementText} \n\n {lootboxLine}{rewardSection}";
    }

    private UnreadCategory UnreadCategoryFor(NotificationCategory category)
    {
        foreach (var unread in _unreadByCategory)
        {
            if (unread.Category == category)
            {
                return unread;
            }
        }

        throw new InvalidOperationException($"No unread queue for {category}.");
    }

    private bool HasAnyUnread()
    {
        foreach (var unread in _unreadByCategory)
        {
            if (unread.Notifications.Count > 0)
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// Refreshes a category badge's text alongside both glow conditions this same unread-count
    /// change affects: the badge itself glows while its own category has a queued-unread
    /// notification, and the Folder glows while *any* category does.
    /// </summary>
    /// <remarks>A glow turning on starts its pulse; a new unread notification restarts the Folder's and its badge's in AddNotification.</remarks>
    private void RefreshUnreadSummary(UnreadCategory unread)
    {
        unread.SummaryWindow.UpdateText($"{unread.Category}: {unread.Notifications.Count}");
        unread.SummaryWindow.SetGlow(unread.Notifications.Count > 0, WindowPalette.AttentionGlow, animation: UnreadNotificationGlowPulse);

        _folder.SetGlow(HasAnyUnread(), WindowPalette.AttentionGlow, animation: UnreadNotificationGlowPulse);
    }
}