using Microsoft.Xna.Framework;

namespace Workshop.Tests;

[TestClass]
public sealed class TabNavigatorTests
{
    private sealed class RecordTab(string title, params Guid[] recordIds) : IIdeTab
    {
        public string Title { get; } = title;

        public bool IsOpen { get; private set; }

        public Guid? SelectedRecordId { get; private set; }

        public void Open(WorkshopTabContext context, Rectangle area) => IsOpen = true;

        public void Resize(Rectangle area) { }

        public void Close() => IsOpen = false;

        public bool TrySelectRecord(Guid recordId)
        {
            if (!IsOpen || !recordIds.Contains(recordId))
            {
                return false;
            }

            SelectedRecordId = recordId;
            return true;
        }

        public void OpenForTest() => IsOpen = true;
    }

    private sealed class RecordingStatus : IWorkshopStatus
    {
        public List<string> Messages { get; } = [];

        public void Show(string message) => Messages.Add(message);
    }

    private static readonly Guid FireballId = Guid.NewGuid();
    private static readonly Guid PoisonId = Guid.NewGuid();

    private static (TabNavigator Navigator, RecordTab Items, RecordTab Actions, RecordTab Auras, RecordingStatus Status, List<int> OpenedTabIndexes) CreateNavigator()
    {
        var items = new RecordTab("Items");
        var actions = new RecordTab("Actions", FireballId);
        var auras = new RecordTab("Auras", PoisonId);
        IReadOnlyList<RecordTab> tabs = [items, actions, auras];
        var status = new RecordingStatus();
        var openedTabIndexes = new List<int>();

        void OpenTab(int index)
        {
            openedTabIndexes.Add(index);
            foreach (var tab in tabs)
            {
                tab.Close();
            }

            tabs[index].OpenForTest();
        }

        return (new TabNavigator([.. tabs], OpenTab, status), items, actions, auras, status, openedTabIndexes);
    }

    [TestMethod]
    public void ARequest_DoesNothingUntilProcessed()
    {
        var (navigator, _, actions, _, _, openedTabIndexes) = CreateNavigator();

        navigator.RequestOpenRecord("Actions", FireballId);

        Assert.IsEmpty(openedTabIndexes);
        Assert.IsNull(actions.SelectedRecordId);
    }

    [TestMethod]
    public void Processing_OpensTheTab_AndSelectsTheRecord()
    {
        var (navigator, _, actions, _, status, openedTabIndexes) = CreateNavigator();
        navigator.RequestOpenRecord("actions", FireballId);

        Assert.IsTrue(navigator.ProcessPendingRequest());

        CollectionAssert.AreEqual(new[] { 1 }, openedTabIndexes);
        Assert.AreEqual(FireballId, actions.SelectedRecordId);
        Assert.IsEmpty(status.Messages);
    }

    [TestMethod]
    public void ARequestIsCarriedOutOnce()
    {
        var (navigator, _, _, _, _, openedTabIndexes) = CreateNavigator();
        navigator.RequestOpenRecord("Actions", FireballId);

        navigator.ProcessPendingRequest();

        Assert.IsFalse(navigator.ProcessPendingRequest());
        Assert.HasCount(1, openedTabIndexes);
    }

    [TestMethod]
    public void TheNewestRequestWins()
    {
        var (navigator, _, actions, auras, _, _) = CreateNavigator();
        navigator.RequestOpenRecord("Actions", FireballId);
        navigator.RequestOpenRecord("Auras", PoisonId);

        navigator.ProcessPendingRequest();

        Assert.AreEqual(PoisonId, auras.SelectedRecordId);
        Assert.IsNull(actions.SelectedRecordId);
    }

    [TestMethod]
    public void ATabNotInTheSession_OpensNothing_AndSaysSo()
    {
        var (navigator, _, _, _, status, openedTabIndexes) = CreateNavigator();
        navigator.RequestOpenRecord("Maps", FireballId);

        Assert.IsFalse(navigator.ProcessPendingRequest());

        Assert.IsEmpty(openedTabIndexes);
        Assert.HasCount(1, status.Messages);
        StringAssert.Contains(status.Messages[0], "Maps");
    }

    [TestMethod]
    public void ARecordTheTabDoesNotHold_IsReported()
    {
        var (navigator, _, actions, _, status, _) = CreateNavigator();
        navigator.RequestOpenRecord("Actions", PoisonId);

        Assert.IsFalse(navigator.ProcessPendingRequest());

        Assert.IsNull(actions.SelectedRecordId);
        Assert.HasCount(1, status.Messages);
    }
}
