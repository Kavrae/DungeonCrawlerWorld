using Engine.Events;
using Game.Notifications;
using Microsoft.Xna.Framework;
using Presentation.Fonts;
using Presentation.Rendering;
using Presentation.UI;
using Presentation.UI.Notifications;

namespace Tests.Presentation;

/// <summary>
/// Covers NotificationCenter's active/unread bookkeeping and the fix for Old's
/// destroy-and-requeue "minimize" hack: closing now goes through Window's real Closed
/// event, verified here by confirming a closed notification's screen position stops being
/// clickable (its Window has actually been detached from the active list, not just hidden).
///
/// Click routing/hit-testing is no longer NotificationCenter's job (Window Chrome Phase A1)
/// -- production code (GameInputController) hit-tests the shared alwaysOnTopWindows list
/// directly. ClickAlwaysOnTop below mirrors that exact topmost-first
/// TryHitTestInteraction-then-HandleClick sequence, against the same list this
/// NotificationCenter was constructed with, so these tests still exercise real click-to-
/// window routing rather than only NotificationCenter's own bookkeeping.
/// </summary>
[TestClass]
public sealed class NotificationCenterTests
{
    private static readonly Point FirstActiveNotificationTopLeft = new(200, 200);

    private static WindowService CreateWindowService() => new(new FontService("Fonts"), new GlyphRenderer());

    private static NotificationCenter CreateNotificationCenter(WindowService windowService, List<Window> alwaysOnTopWindows)
    {
        var notificationCenter = new NotificationCenter(windowService, new EventBus(), alwaysOnTopWindows);
        notificationCenter.Initialize();
        return notificationCenter;
    }

    private static bool ClickAlwaysOnTop(List<Window> alwaysOnTopWindows, Point position)
    {
        for (var index = alwaysOnTopWindows.Count - 1; index >= 0; index--)
        {
            var interaction = alwaysOnTopWindows[index].TryHitTestInteraction(position);
            if (interaction.Window is not null)
            {
                interaction.Window.HandleClick(position);
                return true;
            }
        }

        return false;
    }

    [TestMethod]
    public void AddNotification_ShowImmediately_CreatesAClickableActiveWindow()
    {
        var alwaysOnTopWindows = new List<Window>();
        var notificationCenter = CreateNotificationCenter(CreateWindowService(), alwaysOnTopWindows);

        notificationCenter.AddNotification(NotificationCategory.Quest, "Hello", showImmediately: true);

        Assert.IsTrue(ClickAlwaysOnTop(alwaysOnTopWindows, FirstActiveNotificationTopLeft));
    }

    /// <summary>Feature: opening a notification (fresh, via showImmediately: true) should let a caller (GameInputController, in production) focus the new popup -- see ActiveNotificationOpened.</summary>
    [TestMethod]
    public void AddNotification_ShowImmediately_RaisesActiveNotificationOpenedWithTheNewWindow()
    {
        var alwaysOnTopWindows = new List<Window>();
        var notificationCenter = CreateNotificationCenter(CreateWindowService(), alwaysOnTopWindows);
        Window? openedWindow = null;
        notificationCenter.ActiveNotificationOpened += window => openedWindow = window;

        notificationCenter.AddNotification(NotificationCategory.Quest, "Hello", showImmediately: true);

        Assert.IsNotNull(openedWindow);
        Assert.Contains(openedWindow, alwaysOnTopWindows);
    }

    /// <summary>Same event, via the other path a popup can appear through -- promoting a queued/unread notification back to active.</summary>
    [TestMethod]
    public void OpenNextNotification_RaisesActiveNotificationOpenedWithThePromotedWindow()
    {
        var alwaysOnTopWindows = new List<Window>();
        var notificationCenter = CreateNotificationCenter(CreateWindowService(), alwaysOnTopWindows);
        notificationCenter.AddNotification(NotificationCategory.Quest, "Hello", showImmediately: false);
        Window? openedWindow = null;
        notificationCenter.ActiveNotificationOpened += window => openedWindow = window;

        notificationCenter.OpenNextNotification(NotificationCategory.Quest);

        Assert.IsNotNull(openedWindow);
        Assert.Contains(openedWindow, alwaysOnTopWindows);
    }

    [TestMethod]
    public void AddNotification_NotShownImmediately_CreatesNoActiveWindow()
    {
        var alwaysOnTopWindows = new List<Window>();
        var notificationCenter = CreateNotificationCenter(CreateWindowService(), alwaysOnTopWindows);

        notificationCenter.AddNotification(NotificationCategory.Quest, "Hello", showImmediately: false);

        Assert.IsFalse(ClickAlwaysOnTop(alwaysOnTopWindows, FirstActiveNotificationTopLeft));
    }

    [TestMethod]
    public void CloseNotification_ActiveNotification_RemovesItFromActiveList()
    {
        var alwaysOnTopWindows = new List<Window>();
        var notificationCenter = CreateNotificationCenter(CreateWindowService(), alwaysOnTopWindows);
        var notificationId = notificationCenter.AddNotification(NotificationCategory.Quest, "Hello", showImmediately: true);

        var closed = notificationCenter.CloseNotification(notificationId);

        Assert.IsTrue(closed);
        Assert.IsFalse(ClickAlwaysOnTop(alwaysOnTopWindows, FirstActiveNotificationTopLeft));
    }

    [TestMethod]
    public void CloseNotification_UnknownId_ReturnsFalse()
    {
        var notificationCenter = CreateNotificationCenter(CreateWindowService(), []);

        Assert.IsFalse(notificationCenter.CloseNotification(Guid.NewGuid()));
    }

    [TestMethod]
    public void OpenNextNotification_WithUnreadNotification_PromotesItToActive()
    {
        var alwaysOnTopWindows = new List<Window>();
        var notificationCenter = CreateNotificationCenter(CreateWindowService(), alwaysOnTopWindows);
        notificationCenter.AddNotification(NotificationCategory.Quest, "Hello", showImmediately: false);

        notificationCenter.OpenNextNotification(NotificationCategory.Quest);

        Assert.IsTrue(ClickAlwaysOnTop(alwaysOnTopWindows, FirstActiveNotificationTopLeft));
    }

    [TestMethod]
    public void OpenNextNotification_WithNoUnreadNotifications_DoesNothing()
    {
        var alwaysOnTopWindows = new List<Window>();
        var notificationCenter = CreateNotificationCenter(CreateWindowService(), alwaysOnTopWindows);

        notificationCenter.OpenNextNotification(NotificationCategory.Quest);

        Assert.IsFalse(ClickAlwaysOnTop(alwaysOnTopWindows, FirstActiveNotificationTopLeft));
    }

    /// <summary>
    /// Regression guard for the pooled-window handler leak: WindowService reuses closed
    /// Window instances for later notifications, so NotificationCenter's Closed subscription
    /// must detach itself on fire -- otherwise a second notification reusing the same pooled
    /// window would accumulate a second (stale) handler and double-process on close.
    /// </summary>
    [TestMethod]
    public void CloseNotification_TwiceAcrossPooledWindowReuse_DoesNotThrow()
    {
        var notificationCenter = CreateNotificationCenter(CreateWindowService(), []);

        var firstId = notificationCenter.AddNotification(NotificationCategory.Quest, "First", showImmediately: true);
        notificationCenter.CloseNotification(firstId);

        // The TextWindow instance just closed is now sitting in WindowService's pool and
        // will very likely be handed back out here.
        var secondId = notificationCenter.AddNotification(NotificationCategory.Quest, "Second", showImmediately: true);
        var closed = notificationCenter.CloseNotification(secondId);

        Assert.IsTrue(closed);
    }

    /// <summary>
    /// Regression test for the reported bug: clicking a summary count badge (e.g. "Quest: 1")
    /// did nothing -- the click correctly routed all the way down to the specific summary
    /// TextWindow, but nothing called OpenNextNotification from there. Unlike
    /// OpenNextNotification_WithUnreadNotification_PromotesItToActive above (which calls
    /// OpenNextNotification directly and would have passed even with the bug present), this
    /// drives it through the summary badge's actual screen position, exercising the
    /// click-to-callback wiring itself.
    /// </summary>
    [TestMethod]
    public void ClickingSummaryBadge_WithUnreadNotification_OpensItAsActive()
    {
        var alwaysOnTopWindows = new List<Window>();
        var notificationCenter = CreateNotificationCenter(CreateWindowService(), alwaysOnTopWindows);
        notificationCenter.AddNotification(NotificationCategory.Quest, "Explore the dungeon.", showImmediately: false);

        // Quest is the second declared NotificationCategory, tiled horizontally after System's
        // summary badge (65px wide) starting at the summary bar's position (12, 30).
        var questSummaryBadge = new Point(12 + 65 + 5, 30 + 5);
        var handled = ClickAlwaysOnTop(alwaysOnTopWindows, questSummaryBadge);

        Assert.IsTrue(handled);
        Assert.IsTrue(ClickAlwaysOnTop(alwaysOnTopWindows, FirstActiveNotificationTopLeft));
    }

    [TestMethod]
    public void ClickingSummaryBadge_WithNoUnreadNotifications_DoesNotOpenAnything()
    {
        var alwaysOnTopWindows = new List<Window>();
        _ = CreateNotificationCenter(CreateWindowService(), alwaysOnTopWindows);

        var questSummaryBadge = new Point(12 + 65 + 5, 30 + 5);
        ClickAlwaysOnTop(alwaysOnTopWindows, questSummaryBadge);

        Assert.IsFalse(ClickAlwaysOnTop(alwaysOnTopWindows, FirstActiveNotificationTopLeft));
    }

    [TestMethod]
    public void HasBlockingNotification_SystemNotificationActive_IsTrue()
    {
        var notificationCenter = CreateNotificationCenter(CreateWindowService(), []);

        notificationCenter.AddNotification(NotificationCategory.System, "You have entered the dungeon", showImmediately: true);

        Assert.IsTrue(notificationCenter.HasBlockingNotification);
    }

    [TestMethod]
    public void HasBlockingNotification_OnlyQuestNotificationActive_IsFalse()
    {
        var notificationCenter = CreateNotificationCenter(CreateWindowService(), []);

        notificationCenter.AddNotification(NotificationCategory.Quest, "Take your first steps!", showImmediately: true);

        Assert.IsFalse(notificationCenter.HasBlockingNotification);
    }

    [TestMethod]
    public void HasBlockingNotification_AfterClosingTheSystemNotification_IsFalseAgain()
    {
        var notificationCenter = CreateNotificationCenter(CreateWindowService(), []);
        var notificationId = notificationCenter.AddNotification(NotificationCategory.System, "You have entered the dungeon", showImmediately: true);

        notificationCenter.CloseNotification(notificationId);

        Assert.IsFalse(notificationCenter.HasBlockingNotification);
    }

    /// <summary>
    /// Regression test: click routing used to check _activeNotifications oldest-first, but
    /// ShowActive stacks each new popup ActiveNotificationStackOffset (10px) further
    /// down-right and Draw renders them in the same order -- so a newer popup is both on top
    /// on screen and last in the list, while an older popup's much larger bounding rectangle
    /// (the diagonal offset is tiny next to a real popup's size) still covers the newer
    /// popup's own buttons. Checking oldest-first meant the older popup claimed clicks meant
    /// for the newer one's close button, making it effectively unclickable whenever an older
    /// popup was still open behind it. ClickAlwaysOnTop (topmost-first, matching production)
    /// is what proves this stays fixed now that NotificationCenter itself doesn't route clicks.
    /// </summary>
    [TestMethod]
    public void ClickingCloseButton_OnNewerOverlappingNotification_ClosesOnlyThatOne()
    {
        var fontService = new FontService("Fonts");
        var windowService = new WindowService(fontService, new GlyphRenderer());
        var capturedPopups = new List<TextWindow>();

        // Overrides WindowService's default TextWindow factory just to capture each created
        // instance -- NotificationCenter doesn't expose its windows, and this is the only
        // way to get real Button/ButtonRectangle references (needed to click precisely,
        // since a WrapContent popup's exact size depends on font metrics) without duplicating
        // Window's internal layout math in the test.
        windowService.RegisterFactory<TextWindow>((_, _) =>
        {
            var window = new TextWindow(fontService, windowService, new GlyphRenderer());
            capturedPopups.Add(window);
            return window;
        });

        var alwaysOnTopWindows = new List<Window>();
        var notificationCenter = CreateNotificationCenter(windowService, alwaysOnTopWindows);

        var firstId = notificationCenter.AddNotification(NotificationCategory.Quest, "First", showImmediately: true);
        var secondId = notificationCenter.AddNotification(NotificationCategory.Quest, "Second", showImmediately: true);

        // Summary count badges (created during Initialize, above) have ShowTitle=false and so
        // never get title buttons -- only the two active popups just created do, letting us
        // pick them out regardless of how many summary badges preceded them in the capture list.
        var activePopups = capturedPopups.Where(popup => popup.TitleButtons.Count > 0).ToList();
        Assert.HasCount(2, activePopups);

        var secondPopup = activePopups[1]; // second AddNotification call -- stacked on top, see ActiveNotificationStackOffset
        var closeButton = secondPopup.TitleButtons[0]; // Close attaches first, see Window.Initialize

        var handled = ClickAlwaysOnTop(alwaysOnTopWindows, closeButton.ButtonRectangle.Center);

        Assert.IsTrue(handled);
        // Already closed by the click above -- CloseNotification returns false for an id no
        // longer in the active list.
        Assert.IsFalse(notificationCenter.CloseNotification(secondId));
        // Untouched -- proves the click reached the newer (topmost) popup, not the one behind it.
        Assert.IsTrue(notificationCenter.CloseNotification(firstId));
    }

    /// <summary>Captures every TextWindow WindowService creates -- the only way to inspect an active popup's own TitleText, since NotificationCenter doesn't expose its windows. Mirrors ClickingCloseButton_OnNewerOverlappingNotification_ClosesOnlyThatOne's technique.</summary>
    private static (WindowService WindowService, List<TextWindow> CapturedPopups) CreateWindowServiceCapturingTextWindows()
    {
        var fontService = new FontService("Fonts");
        var windowService = new WindowService(fontService, new GlyphRenderer());
        var capturedPopups = new List<TextWindow>();
        windowService.RegisterFactory<TextWindow>((_, _) =>
        {
            var window = new TextWindow(fontService, windowService, new GlyphRenderer());
            capturedPopups.Add(window);
            return window;
        });
        return (windowService, capturedPopups);
    }

    [TestMethod]
    public void AddNotification_WithCustomTitle_UsesItInsteadOfTheCategoryName()
    {
        var (windowService, capturedPopups) = CreateWindowServiceCapturingTextWindows();
        var notificationCenter = CreateNotificationCenter(windowService, []);

        notificationCenter.AddNotification(NotificationCategory.Quest, "Explore the dungeon.", showImmediately: true, title: "New Quest");

        var activePopup = capturedPopups.Single(popup => popup.TitleButtons.Count > 0);
        Assert.AreEqual("New Quest", activePopup.TitleText);
    }

    [TestMethod]
    public void AddNotification_WithoutCustomTitle_FallsBackToTheCategoryName()
    {
        var (windowService, capturedPopups) = CreateWindowServiceCapturingTextWindows();
        var notificationCenter = CreateNotificationCenter(windowService, []);

        notificationCenter.AddNotification(NotificationCategory.Quest, "Explore the dungeon.", showImmediately: true);

        var activePopup = capturedPopups.Single(popup => popup.TitleButtons.Count > 0);
        Assert.AreEqual("Quest", activePopup.TitleText);
    }

    /// <summary>The quest composer's exact call shape: created minimized (showImmediately: false) with a custom title -- the title must survive being queued and only later shown via OpenNextNotification.</summary>
    [TestMethod]
    public void AddNotification_MinimizedWithCustomTitle_ShowsTheTitleWhenLaterOpened()
    {
        var (windowService, capturedPopups) = CreateWindowServiceCapturingTextWindows();
        var notificationCenter = CreateNotificationCenter(windowService, []);
        notificationCenter.AddNotification(NotificationCategory.Quest, "Explore the dungeon.", showImmediately: false, title: "New Quest");

        notificationCenter.OpenNextNotification(NotificationCategory.Quest);

        var activePopup = capturedPopups.Single(popup => popup.TitleButtons.Count > 0);
        Assert.AreEqual("New Quest", activePopup.TitleText);
    }

    [TestMethod]
    public void PublishingNotificationRequested_ThenUpdate_ProducesSameResultAsDirectAddNotification()
    {
        var eventBus = new EventBus();
        var alwaysOnTopWindows = new List<Window>();
        var notificationCenter = new NotificationCenter(CreateWindowService(), eventBus, alwaysOnTopWindows);
        notificationCenter.Initialize();

        eventBus.Publish(new NotificationRequested(NotificationCategory.System, "You have entered the dungeon", ShowImmediately: true));

        // Not dispatched yet -- Publish on a buffered event only enqueues.
        Assert.IsFalse(notificationCenter.HasBlockingNotification);
        Assert.IsFalse(ClickAlwaysOnTop(alwaysOnTopWindows, FirstActiveNotificationTopLeft));

        notificationCenter.Update(new GameTime());

        Assert.IsTrue(notificationCenter.HasBlockingNotification);
        Assert.IsTrue(ClickAlwaysOnTop(alwaysOnTopWindows, FirstActiveNotificationTopLeft));
    }
}