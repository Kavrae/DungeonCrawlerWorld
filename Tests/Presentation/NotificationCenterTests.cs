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
/// -- production code (GameInputController) hit-tests the shared dynamicHudWindows list
/// directly. ClickDynamicHud below mirrors that exact topmost-first
/// TryHitTestInteraction-then-HandleClick sequence, against the same list this
/// NotificationCenter was constructed with, so these tests still exercise real click-to-
/// window routing rather than only NotificationCenter's own bookkeeping.
/// </summary>
[TestClass]
public sealed class NotificationCenterTests
{
    private static readonly Point FirstActiveNotificationTopLeft = new(200, 200);

    private static ElementPoolService CreateWindowService()
    {
        var fontService = new FontService("Fonts");
        var glyphRenderer = new GlyphRenderer();
        var windowService = new ElementPoolService(fontService, glyphRenderer);
        windowService.RegisterFactory<Folder>((_, _) => new Folder(
            fontService, windowService, glyphRenderer, new SpriteSheetService(null, "Spritesheets"), new SpriteRenderer()));
        return windowService;
    }

    private static NotificationCenter CreateNotificationCenter(ElementPoolService windowService, List<Element> dynamicHudWindows)
    {
        var notificationCenter = new NotificationCenter(windowService, new EventBus(), dynamicHudWindows);
        notificationCenter.Initialize();
        return notificationCenter;
    }

    private static bool ClickDynamicHud(List<Element> dynamicHudWindows, Point position)
    {
        for (var index = dynamicHudWindows.Count - 1; index >= 0; index--)
        {
            var interaction = dynamicHudWindows[index].TryHitTestInteraction(position);
            if (interaction.Element is not null)
            {
                interaction.Element.HandleClick(position);
                return true;
            }
        }

        return false;
    }

    [TestMethod]
    public void AddNotification_ShowImmediately_CreatesAClickableActiveWindow()
    {
        var dynamicHudWindows = new List<Element>();
        var notificationCenter = CreateNotificationCenter(CreateWindowService(), dynamicHudWindows);

        notificationCenter.AddNotification(NotificationCategory.Quest, "Hello", showImmediately: true);

        Assert.IsTrue(ClickDynamicHud(dynamicHudWindows, FirstActiveNotificationTopLeft));
    }

    /// <summary>Feature: opening a notification (fresh, via showImmediately: true) should let a caller (GameInputController, in production) focus the new popup -- see ActiveNotificationOpened.</summary>
    [TestMethod]
    public void AddNotification_ShowImmediately_RaisesActiveNotificationOpenedWithTheNewWindow()
    {
        var dynamicHudWindows = new List<Element>();
        var notificationCenter = CreateNotificationCenter(CreateWindowService(), dynamicHudWindows);
        Window? openedWindow = null;
        notificationCenter.ActiveNotificationOpened += window => openedWindow = window;

        notificationCenter.AddNotification(NotificationCategory.Quest, "Hello", showImmediately: true);

        Assert.IsNotNull(openedWindow);
        Assert.Contains(openedWindow, dynamicHudWindows);
    }

    /// <summary>Same event, via the other path a popup can appear through -- promoting a queued/unread notification back to active.</summary>
    [TestMethod]
    public void OpenNextNotification_RaisesActiveNotificationOpenedWithThePromotedWindow()
    {
        var dynamicHudWindows = new List<Element>();
        var notificationCenter = CreateNotificationCenter(CreateWindowService(), dynamicHudWindows);
        notificationCenter.AddNotification(NotificationCategory.Quest, "Hello", showImmediately: false);
        Window? openedWindow = null;
        notificationCenter.ActiveNotificationOpened += window => openedWindow = window;

        notificationCenter.OpenNextNotification(NotificationCategory.Quest);

        Assert.IsNotNull(openedWindow);
        Assert.Contains(openedWindow, dynamicHudWindows);
    }

    [TestMethod]
    public void AddNotification_NotShownImmediately_CreatesNoActiveWindow()
    {
        var dynamicHudWindows = new List<Element>();
        var notificationCenter = CreateNotificationCenter(CreateWindowService(), dynamicHudWindows);

        notificationCenter.AddNotification(NotificationCategory.Quest, "Hello", showImmediately: false);

        Assert.IsFalse(ClickDynamicHud(dynamicHudWindows, FirstActiveNotificationTopLeft));
    }

    [TestMethod]
    public void CloseNotification_ActiveNotification_RemovesItFromActiveList()
    {
        var dynamicHudWindows = new List<Element>();
        var notificationCenter = CreateNotificationCenter(CreateWindowService(), dynamicHudWindows);
        var notificationId = notificationCenter.AddNotification(NotificationCategory.Quest, "Hello", showImmediately: true);

        var closed = notificationCenter.CloseNotification(notificationId);

        Assert.IsTrue(closed);
        Assert.IsFalse(ClickDynamicHud(dynamicHudWindows, FirstActiveNotificationTopLeft));
    }

    private static (ElementPoolService WindowService, Func<Folder> GetFolder) CreateWindowServiceCapturingFolder()
    {
        var fontService = new FontService("Fonts");
        var glyphRenderer = new GlyphRenderer();
        var windowService = new ElementPoolService(fontService, glyphRenderer);
        Folder? capturedFolder = null;
        windowService.RegisterFactory<Folder>((_, _) =>
        {
            capturedFolder = new Folder(fontService, windowService, glyphRenderer, new SpriteSheetService(null, "Spritesheets"), new SpriteRenderer());
            return capturedFolder;
        });
        return (windowService, () => capturedFolder ?? throw new InvalidOperationException("Folder not created yet."));
    }

    /// <summary>Closing a notification auto-tidies the HUD back down once nothing is left unread anywhere -- see NotificationCenter.OnActiveNotificationClosed.</summary>
    [TestMethod]
    public void CloseNotification_WithNoUnreadNotificationsRemaining_MinimizesTheFolder()
    {
        var (windowService, getFolder) = CreateWindowServiceCapturingFolder();
        var notificationCenter = CreateNotificationCenter(windowService, []);
        var notificationId = notificationCenter.AddNotification(NotificationCategory.Quest, "Hello", showImmediately: true);
        var folder = getFolder();

        // Force-expand first -- proves the close itself re-collapses it, not that it just never opened.
        folder.HandleClick(new Point(35, 35));
        Assert.AreEqual(ElementDisplayMode.WrapContent, folder.DisplayMode);

        notificationCenter.CloseNotification(notificationId);

        Assert.AreEqual(ElementDisplayMode.Minimized, folder.DisplayMode);
    }

    /// <summary>The Folder stays open for the user to keep working through what's left -- auto-minimize only triggers once every category's unread queue is actually empty, not just because one popup closed.</summary>
    [TestMethod]
    public void CloseNotification_WithUnreadNotificationsStillQueued_DoesNotMinimizeTheFolder()
    {
        var (windowService, getFolder) = CreateWindowServiceCapturingFolder();
        var notificationCenter = CreateNotificationCenter(windowService, []);
        var activeId = notificationCenter.AddNotification(NotificationCategory.Quest, "Active", showImmediately: true);
        notificationCenter.AddNotification(NotificationCategory.Achievement, "Queued", showImmediately: false);
        var folder = getFolder();
        folder.HandleClick(new Point(35, 35));

        notificationCenter.CloseNotification(activeId);

        Assert.AreEqual(ElementDisplayMode.WrapContent, folder.DisplayMode);
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
        var dynamicHudWindows = new List<Element>();
        var notificationCenter = CreateNotificationCenter(CreateWindowService(), dynamicHudWindows);
        notificationCenter.AddNotification(NotificationCategory.Quest, "Hello", showImmediately: false);

        notificationCenter.OpenNextNotification(NotificationCategory.Quest);

        Assert.IsTrue(ClickDynamicHud(dynamicHudWindows, FirstActiveNotificationTopLeft));
    }

    [TestMethod]
    public void OpenNextNotification_WithNoUnreadNotifications_DoesNothing()
    {
        var dynamicHudWindows = new List<Element>();
        var notificationCenter = CreateNotificationCenter(CreateWindowService(), dynamicHudWindows);

        notificationCenter.OpenNextNotification(NotificationCategory.Quest);

        Assert.IsFalse(ClickDynamicHud(dynamicHudWindows, FirstActiveNotificationTopLeft));
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
        var (windowService, capturedBadges) = CreateWindowServiceCapturingTextWindows();
        var dynamicHudWindows = new List<Element>();
        var notificationCenter = CreateNotificationCenter(windowService, dynamicHudWindows);
        notificationCenter.AddNotification(NotificationCategory.Quest, "Explore the dungeon.", showImmediately: false);

        // The Folder starts collapsed (see Folder.Initialize) -- clicking anywhere within its
        // small icon-sized header at HudMetrics.Margin (30, 30) expands it, tiling the
        // category badges vertically beneath. Only then does Quest's badge have a real,
        // clickable on-screen position -- read via WindowRectangle (its exact layout depends
        // on border/title-icon sizing) rather than hand-derived pixel math.
        Assert.IsTrue(ClickDynamicHud(dynamicHudWindows, new Point(30 + 5, 30 + 5)));

        var questBadge = capturedBadges.Single(badge => badge.OriginalText == "Quest: 1");
        var handled = ClickDynamicHud(dynamicHudWindows, questBadge.Rectangle.Center);

        Assert.IsTrue(handled);
        Assert.IsTrue(ClickDynamicHud(dynamicHudWindows, FirstActiveNotificationTopLeft));
    }

    [TestMethod]
    public void ClickingSummaryBadge_WithNoUnreadNotifications_DoesNotOpenAnything()
    {
        var (windowService, capturedBadges) = CreateWindowServiceCapturingTextWindows();
        var dynamicHudWindows = new List<Element>();
        _ = CreateNotificationCenter(windowService, dynamicHudWindows);

        Assert.IsTrue(ClickDynamicHud(dynamicHudWindows, new Point(30 + 5, 30 + 5))); // expand the Folder
        var questBadge = capturedBadges.Single(badge => badge.OriginalText == "Quest: 0");
        ClickDynamicHud(dynamicHudWindows, questBadge.Rectangle.Center);

        Assert.IsFalse(ClickDynamicHud(dynamicHudWindows, FirstActiveNotificationTopLeft));
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
    /// popup was still open behind it. ClickDynamicHud (topmost-first, matching production)
    /// is what proves this stays fixed now that NotificationCenter itself doesn't route clicks.
    /// </summary>
    [TestMethod]
    public void ClickingCloseButton_OnNewerOverlappingNotification_ClosesOnlyThatOne()
    {
        var fontService = new FontService("Fonts");
        var glyphRenderer = new GlyphRenderer();
        var windowService = new ElementPoolService(fontService, glyphRenderer);
        windowService.RegisterFactory<Folder>((_, _) => new Folder(
            fontService, windowService, glyphRenderer, new SpriteSheetService(null, "Spritesheets"), new SpriteRenderer()));
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

        var dynamicHudWindows = new List<Element>();
        var notificationCenter = CreateNotificationCenter(windowService, dynamicHudWindows);

        var firstId = notificationCenter.AddNotification(NotificationCategory.Quest, "First", showImmediately: true);
        var secondId = notificationCenter.AddNotification(NotificationCategory.Quest, "Second", showImmediately: true);

        // Summary count badges (created during Initialize, above) have ShowTitle=false and so
        // never get title buttons -- only the two active popups just created do, letting us
        // pick them out regardless of how many summary badges preceded them in the capture list.
        var activePopups = capturedPopups.Where(popup => popup.TitleButtons.Count > 0).ToList();
        Assert.HasCount(2, activePopups);

        var secondPopup = activePopups[1]; // second AddNotification call -- stacked on top, see ActiveNotificationStackOffset
        var closeButton = secondPopup.TitleButtons[0]; // Close attaches first, see Window.Initialize

        var handled = ClickDynamicHud(dynamicHudWindows, closeButton.Rectangle.Center);

        Assert.IsTrue(handled);
        // Already closed by the click above -- CloseNotification returns false for an id no
        // longer in the active list.
        Assert.IsFalse(notificationCenter.CloseNotification(secondId));
        // Untouched -- proves the click reached the newer (topmost) popup, not the one behind it.
        Assert.IsTrue(notificationCenter.CloseNotification(firstId));
    }

    /// <summary>Captures every TextWindow WindowService creates -- the only way to inspect an active popup's own TitleText, since NotificationCenter doesn't expose its windows. Mirrors ClickingCloseButton_OnNewerOverlappingNotification_ClosesOnlyThatOne's technique.</summary>
    private static (ElementPoolService WindowService, List<TextWindow> CapturedPopups) CreateWindowServiceCapturingTextWindows()
    {
        var fontService = new FontService("Fonts");
        var glyphRenderer = new GlyphRenderer();
        var windowService = new ElementPoolService(fontService, glyphRenderer);
        windowService.RegisterFactory<Folder>((_, _) => new Folder(
            fontService, windowService, glyphRenderer, new SpriteSheetService(null, "Spritesheets"), new SpriteRenderer()));
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
        var dynamicHudWindows = new List<Element>();
        var notificationCenter = new NotificationCenter(CreateWindowService(), eventBus, dynamicHudWindows);
        notificationCenter.Initialize();

        eventBus.Publish(new NotificationRequestedEvent(NotificationCategory.System, "You have entered the dungeon", ShowImmediately: true));

        // Not dispatched yet -- Publish on a buffered event only enqueues.
        Assert.IsFalse(notificationCenter.HasBlockingNotification);
        Assert.IsFalse(ClickDynamicHud(dynamicHudWindows, FirstActiveNotificationTopLeft));

        notificationCenter.Update(new GameTime());

        Assert.IsTrue(notificationCenter.HasBlockingNotification);
        Assert.IsTrue(ClickDynamicHud(dynamicHudWindows, FirstActiveNotificationTopLeft));
    }

    /// <summary>
    /// Achievement notifications carry structured fields (AchievementNotificationDetails)
    /// beyond the base Text/Title -- TextWindow only renders one flat string, so
    /// NotificationCenter.ShowActive flattens them into the popup's displayed text. This
    /// confirms every field actually shows up, not just Text/Title.
    /// </summary>
    [TestMethod]
    public void AddNotification_WithAchievementDetails_IncludesEveryFieldInTheDisplayedText()
    {
        var (windowService, capturedPopups) = CreateWindowServiceCapturingTextWindows();
        var notificationCenter = CreateNotificationCenter(windowService, []);
        var achievement = new AchievementNotificationDetails(
            RequirementText: "Entered the dungeon without a human companion.",
            LootboxLabel: "Bronze Adventurer Box",
            RewardText: "A shiny bronze box.");

        notificationCenter.AddNotification(
            NotificationCategory.Achievement,
            "Didn't anyone teach you there is safety in numbers?",
            showImmediately: true,
            title: "Loner",
            achievement: achievement);

        var activePopup = capturedPopups.Single(popup => popup.TitleButtons.Count > 0);
        Assert.AreEqual("Loner", activePopup.TitleText);
        StringAssert.Contains(activePopup.OriginalText, "Didn't anyone teach you there is safety in numbers?");
        StringAssert.Contains(activePopup.OriginalText, achievement.RequirementText);
        StringAssert.Contains(activePopup.OriginalText, achievement.LootboxLabel);
        StringAssert.Contains(activePopup.OriginalText, achievement.RewardText);
    }

    [TestMethod]
    public void AddNotification_WithAchievementDetailsButNoLootbox_ShowsNoneForLootbox()
    {
        var (windowService, capturedPopups) = CreateWindowServiceCapturingTextWindows();
        var notificationCenter = CreateNotificationCenter(windowService, []);
        var achievement = new AchievementNotificationDetails(
            RequirementText: "Entered the dungeon without a human companion.",
            LootboxLabel: null,
            RewardText: "None! Haha. You are so dead.");

        notificationCenter.AddNotification(
            NotificationCategory.Achievement,
            "Didn't anyone teach you there is safety in numbers?",
            showImmediately: true,
            title: "Loner",
            achievement: achievement);

        var activePopup = capturedPopups.Single(popup => popup.TitleButtons.Count > 0);
        StringAssert.Contains(activePopup.OriginalText, "Lootbox: None.");
    }
}