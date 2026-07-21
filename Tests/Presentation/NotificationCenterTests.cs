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
/// </summary>
[TestClass]
public sealed class NotificationCenterTests
{
    private static readonly Point FirstActiveNotificationTopLeft = new(200, 200);

    private static WindowService CreateWindowService() => new(new FontService("Fonts"), new GlyphRenderer());

    [TestMethod]
    public void AddNotification_ShowImmediately_CreatesAClickableActiveWindow()
    {
        var notificationCenter = new NotificationCenter(CreateWindowService(), new EventBus());
        notificationCenter.Initialize();

        notificationCenter.AddNotification(NotificationCategory.Quest, "Hello", showImmediately: true);

        Assert.IsTrue(notificationCenter.HandleClick(FirstActiveNotificationTopLeft));
    }

    [TestMethod]
    public void AddNotification_NotShownImmediately_CreatesNoActiveWindow()
    {
        var notificationCenter = new NotificationCenter(CreateWindowService(), new EventBus());
        notificationCenter.Initialize();

        notificationCenter.AddNotification(NotificationCategory.Quest, "Hello", showImmediately: false);

        Assert.IsFalse(notificationCenter.HandleClick(FirstActiveNotificationTopLeft));
    }

    [TestMethod]
    public void CloseNotification_ActiveNotification_RemovesItFromActiveList()
    {
        var notificationCenter = new NotificationCenter(CreateWindowService(), new EventBus());
        notificationCenter.Initialize();
        var notificationId = notificationCenter.AddNotification(NotificationCategory.Quest, "Hello", showImmediately: true);

        var closed = notificationCenter.CloseNotification(notificationId);

        Assert.IsTrue(closed);
        Assert.IsFalse(notificationCenter.HandleClick(FirstActiveNotificationTopLeft));
    }

    [TestMethod]
    public void CloseNotification_UnknownId_ReturnsFalse()
    {
        var notificationCenter = new NotificationCenter(CreateWindowService(), new EventBus());
        notificationCenter.Initialize();

        Assert.IsFalse(notificationCenter.CloseNotification(Guid.NewGuid()));
    }

    [TestMethod]
    public void OpenNextNotification_WithUnreadNotification_PromotesItToActive()
    {
        var notificationCenter = new NotificationCenter(CreateWindowService(), new EventBus());
        notificationCenter.Initialize();
        notificationCenter.AddNotification(NotificationCategory.Quest, "Hello", showImmediately: false);

        notificationCenter.OpenNextNotification(NotificationCategory.Quest);

        Assert.IsTrue(notificationCenter.HandleClick(FirstActiveNotificationTopLeft));
    }

    [TestMethod]
    public void OpenNextNotification_WithNoUnreadNotifications_DoesNothing()
    {
        var notificationCenter = new NotificationCenter(CreateWindowService(), new EventBus());
        notificationCenter.Initialize();

        notificationCenter.OpenNextNotification(NotificationCategory.Quest);

        Assert.IsFalse(notificationCenter.HandleClick(FirstActiveNotificationTopLeft));
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
        var notificationCenter = new NotificationCenter(CreateWindowService(), new EventBus());
        notificationCenter.Initialize();

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
    /// drives it through NotificationCenter.HandleClick at the summary badge's actual screen
    /// position, exercising the click-to-callback wiring itself.
    /// </summary>
    [TestMethod]
    public void ClickingSummaryBadge_WithUnreadNotification_OpensItAsActive()
    {
        var notificationCenter = new NotificationCenter(CreateWindowService(), new EventBus());
        notificationCenter.Initialize();
        notificationCenter.AddNotification(NotificationCategory.Quest, "Explore the dungeon.", showImmediately: false);

        // Quest is the second declared NotificationCategory, tiled horizontally after System's
        // summary badge (65px wide) starting at the summary bar's position (12, 30).
        var questSummaryBadge = new Point(12 + 65 + 5, 30 + 5);
        var handled = notificationCenter.HandleClick(questSummaryBadge);

        Assert.IsTrue(handled);
        Assert.IsTrue(notificationCenter.HandleClick(FirstActiveNotificationTopLeft));
    }

    [TestMethod]
    public void ClickingSummaryBadge_WithNoUnreadNotifications_DoesNotOpenAnything()
    {
        var notificationCenter = new NotificationCenter(CreateWindowService(), new EventBus());
        notificationCenter.Initialize();

        var questSummaryBadge = new Point(12 + 65 + 5, 30 + 5);
        notificationCenter.HandleClick(questSummaryBadge);

        Assert.IsFalse(notificationCenter.HandleClick(FirstActiveNotificationTopLeft));
    }

    [TestMethod]
    public void HasBlockingNotification_SystemNotificationActive_IsTrue()
    {
        var notificationCenter = new NotificationCenter(CreateWindowService(), new EventBus());
        notificationCenter.Initialize();

        notificationCenter.AddNotification(NotificationCategory.System, "You have entered the dungeon", showImmediately: true);

        Assert.IsTrue(notificationCenter.HasBlockingNotification);
    }

    [TestMethod]
    public void HasBlockingNotification_OnlyQuestNotificationActive_IsFalse()
    {
        var notificationCenter = new NotificationCenter(CreateWindowService(), new EventBus());
        notificationCenter.Initialize();

        notificationCenter.AddNotification(NotificationCategory.Quest, "Take your first steps!", showImmediately: true);

        Assert.IsFalse(notificationCenter.HasBlockingNotification);
    }

    [TestMethod]
    public void HasBlockingNotification_AfterClosingTheSystemNotification_IsFalseAgain()
    {
        var notificationCenter = new NotificationCenter(CreateWindowService(), new EventBus());
        notificationCenter.Initialize();
        var notificationId = notificationCenter.AddNotification(NotificationCategory.System, "You have entered the dungeon", showImmediately: true);

        notificationCenter.CloseNotification(notificationId);

        Assert.IsFalse(notificationCenter.HasBlockingNotification);
    }

    /// <summary>
    /// Regression test: HandleClick used to check _activeNotifications oldest-first, but
    /// ShowActive stacks each new popup ActiveNotificationStackOffset (10px) further
    /// down-right and Draw renders them in the same order -- so a newer popup is both on top
    /// on screen and last in the list, while an older popup's much larger bounding rectangle
    /// (the diagonal offset is tiny next to a real popup's size) still covers the newer
    /// popup's own buttons. Checking oldest-first meant the older popup claimed clicks meant
    /// for the newer one's close button, making it effectively unclickable whenever an older
    /// popup was still open behind it.
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

        var notificationCenter = new NotificationCenter(windowService, new EventBus());
        notificationCenter.Initialize();

        var firstId = notificationCenter.AddNotification(NotificationCategory.Quest, "First", showImmediately: true);
        var secondId = notificationCenter.AddNotification(NotificationCategory.Quest, "Second", showImmediately: true);

        // Summary count badges (created during Initialize, above) have ShowTitle=false and so
        // never get title buttons -- only the two active popups just created do, letting us
        // pick them out regardless of how many summary badges preceded them in the capture list.
        var activePopups = capturedPopups.Where(popup => popup.TitleButtons.Count > 0).ToList();
        Assert.HasCount(2, activePopups);

        var secondPopup = activePopups[1]; // second AddNotification call -- stacked on top, see ActiveNotificationStackOffset
        var closeButton = secondPopup.TitleButtons[0]; // Close attaches first, see Window.Initialize

        var handled = notificationCenter.HandleClick(closeButton.ButtonRectangle.Center);

        Assert.IsTrue(handled);
        // Already closed by the click above -- CloseNotification returns false for an id no
        // longer in the active list.
        Assert.IsFalse(notificationCenter.CloseNotification(secondId));
        // Untouched -- proves the click reached the newer (topmost) popup, not the one behind it.
        Assert.IsTrue(notificationCenter.CloseNotification(firstId));
    }

    [TestMethod]
    public void PublishingNotificationRequested_ThenUpdate_ProducesSameResultAsDirectAddNotification()
    {
        var eventBus = new EventBus();
        var notificationCenter = new NotificationCenter(CreateWindowService(), eventBus);
        notificationCenter.Initialize();

        eventBus.Publish(new NotificationRequested(NotificationCategory.System, "You have entered the dungeon", ShowImmediately: true));

        // Not dispatched yet -- Publish on a buffered event only enqueues.
        Assert.IsFalse(notificationCenter.HasBlockingNotification);
        Assert.IsFalse(notificationCenter.HandleClick(FirstActiveNotificationTopLeft));

        notificationCenter.Update(new GameTime());

        Assert.IsTrue(notificationCenter.HasBlockingNotification);
        Assert.IsTrue(notificationCenter.HandleClick(FirstActiveNotificationTopLeft));
    }
}
