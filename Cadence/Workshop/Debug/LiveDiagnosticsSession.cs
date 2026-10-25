using Engine.Diagnostics;

namespace Workshop.Debug;

/// <summary>What the Workshop keeps of one game connected over the diagnostics link: its hello, its gauges, a long frame history, its latest reports, and whether it is still connected.</summary>
/// <remarks>
/// Two threads: the link's receiving thread parses each message (Receive) into an inbox, and the UI thread moves the
/// inbox into what it reads once per IDE frame (ApplyPending), so nothing the UI reads changes under it. Everything but
/// Receive and MarkEnded is the UI thread's. The session outlives the game: once it ends its data stays, marked Ended.
/// </remarks>
public sealed class LiveDiagnosticsSession(int historyCapacity = DiagnosticsFrameHistory.DefaultCapacity)
{
    private readonly Lock _inboxLock = new();
    private Inbox _receivingInbox = new();
    private Inbox _applyingInbox = new();
    private double[] _receivedGaugeValues = new double[64];

    public DiagnosticsConnectionState State { get; private set; } = DiagnosticsConnectionState.Connected;

    /// <summary>The game's hello; null until it arrives.</summary>
    public DiagnosticsLinkHello? Hello { get; private set; }

    /// <summary>The current simulated session's gauges, in the order History numbers them (after its update milliseconds).</summary>
    public IReadOnlyList<DiagnosticsLinkGaugeDescriptor> Gauges { get; private set; } = [];

    /// <summary>The current simulated session's frames; null until a session starts.</summary>
    public DiagnosticsFrameHistory? History { get; private set; }

    /// <summary>True from a simulated session's start until its end.</summary>
    public bool IsSimulating { get; private set; }

    /// <summary>The last simulated frame the game reported.</summary>
    public long LatestFrame { get; private set; }

    /// <summary>The game's draws per second, as of its latest record.</summary>
    public double DrawRate { get; private set; }

    /// <summary>The game's updates per second, as of its latest record.</summary>
    public double UpdateRate { get; private set; }

    /// <summary>True while the game's host runs no simulation frames (paused, or a menu holding it).</summary>
    public bool IsPaused { get; private set; }

    /// <summary>Frame records the game dropped because the Workshop fell behind.</summary>
    public long DroppedFrameCount { get; private set; }

    /// <summary>The latest periodic report -- what latest.json holds -- read on the receiving thread.</summary>
    public DiagnosticsReport? LatestPeriodicReport { get; private set; }

    /// <summary>The periodic report before the latest, what its changes are measured from.</summary>
    public DiagnosticsReport? PreviousPeriodicReport { get; private set; }

    /// <summary>Changes every time a periodic report arrives.</summary>
    public int PeriodicReportVersion { get; private set; }

    /// <summary>The startup report, once the game's frame pacing settled.</summary>
    public DiagnosticsStartupReport? StartupReport { get; private set; }

    /// <summary>The files the latest closed benchmark range wrote.</summary>
    public DiagnosticsLinkBenchmarkCompletion? LatestBenchmarkCompletion { get; private set; }

    /// <summary>The report file a recorded session was read from; null for a connected game.</summary>
    public string? Source { get; private set; }

    /// <summary>True when the frame costs' values are milliseconds per frame (a benchmark's), not per second.</summary>
    public bool FrameCostsArePerFrame { get; private set; }

    /// <summary>A session read from a report file rather than a game -- what the Reports section opens into the same views as Live. It is ended from the start and receives nothing.</summary>
    /// <param name="hello">Its features say what the file holds, so a view of something it lacks says so.</param>
    /// <param name="frameCostsArePerFrame">periodicReport's frame costs are milliseconds per frame.</param>
    public static LiveDiagnosticsSession Recorded(
        string source,
        DiagnosticsLinkHello hello,
        IReadOnlyList<DiagnosticsLinkGaugeDescriptor> gauges,
        DiagnosticsFrameHistory? history,
        DiagnosticsReport? periodicReport,
        DiagnosticsStartupReport? startupReport,
        bool frameCostsArePerFrame = false) => new()
        {
            Source = source,
            State = DiagnosticsConnectionState.Ended,
            Hello = hello,
            Gauges = gauges,
            History = history,
            LatestFrame = history?.LatestFrame ?? 0,
            LatestPeriodicReport = periodicReport,
            PeriodicReportVersion = periodicReport is null ? 0 : 1,
            StartupReport = startupReport,
            FrameCostsArePerFrame = frameCostsArePerFrame,
        };

    /// <summary>Changes every time StatusText does.</summary>
    public int StatusVersion { get; private set; }

    /// <summary>The connection for the status line: connected (with the game's process id once known), ended, and frames dropped.</summary>
    public string StatusText
    {
        get
        {
            var connection = State == DiagnosticsConnectionState.Ended
                ? "ended"
                : Hello is { } hello ? $"connected (pid {hello.ProcessId})" : "connected";
            return DroppedFrameCount > 0
                ? $"Diagnostics: {connection}, dropped {DroppedFrameCount:N0} frames"
                : $"Diagnostics: {connection}";
        }
    }

    /// <summary>Parses one message off the link into the inbox; a type it doesn't know is skipped. Receiving thread.</summary>
    /// <exception cref="InvalidDataException">The payload is not what its type says.</exception>
    public void Receive(DiagnosticsLinkMessageType type, ReadOnlySpan<byte> payload)
    {
        switch (type)
        {
            case DiagnosticsLinkMessageType.Frame:
                ReceiveFrame(payload);
                return;
            case DiagnosticsLinkMessageType.Hello:
                AddToInbox(type, DiagnosticsLinkProtocol.ReadJson<DiagnosticsLinkHello>(payload));
                return;
            case DiagnosticsLinkMessageType.SessionStarted:
                AddToInbox(type, DiagnosticsLinkProtocol.ReadJson<DiagnosticsLinkSessionStart>(payload));
                return;
            case DiagnosticsLinkMessageType.PeriodicReport:
                AddToInbox(type, DiagnosticsReportReader.ReadPeriodicReport(payload));
                return;
            case DiagnosticsLinkMessageType.StartupReport:
                AddToInbox(type, DiagnosticsReportReader.ReadStartupReport(payload));
                return;
            case DiagnosticsLinkMessageType.BenchmarkCompleted:
                AddToInbox(type, DiagnosticsLinkProtocol.ReadJson<DiagnosticsLinkBenchmarkCompletion>(payload));
                return;
            case DiagnosticsLinkMessageType.SessionEnded:
                AddToInbox(type, null);
                return;
        }
    }

    /// <summary>The link closed. Receiving thread.</summary>
    public void MarkEnded()
    {
        lock (_inboxLock)
        {
            _receivingInbox.HasEnded = true;
        }
    }

    /// <summary>Applies everything received since the last call, in order. UI thread, once per frame.</summary>
    public void ApplyPending()
    {
        lock (_inboxLock)
        {
            (_receivingInbox, _applyingInbox) = (_applyingInbox, _receivingInbox);
        }

        var inbox = _applyingInbox;
        foreach (var item in inbox.Items)
        {
            Apply(item, inbox.FrameValues);
        }

        if (inbox.HasEnded && State != DiagnosticsConnectionState.Ended)
        {
            State = DiagnosticsConnectionState.Ended;
            IsSimulating = false;
            StatusVersion++;
        }

        inbox.Clear();
    }

    private void ReceiveFrame(ReadOnlySpan<byte> payload)
    {
        var gaugeCount = DiagnosticsFrameRecord.Read(payload, out var record, _receivedGaugeValues);
        if (gaugeCount > _receivedGaugeValues.Length)
        {
            _receivedGaugeValues = new double[gaugeCount];
            DiagnosticsFrameRecord.Read(payload, out record, _receivedGaugeValues);
        }

        lock (_inboxLock)
        {
            var valuesStart = _receivingInbox.FrameValues.Count;
            _receivingInbox.FrameValues.AddRange(_receivedGaugeValues.AsSpan(0, gaugeCount));
            _receivingInbox.Items.Add(new InboxItem(DiagnosticsLinkMessageType.Frame, null, record, valuesStart, gaugeCount));
        }
    }

    private void AddToInbox(DiagnosticsLinkMessageType type, object? message)
    {
        lock (_inboxLock)
        {
            _receivingInbox.Items.Add(new InboxItem(type, message, default, 0, 0));
        }
    }

    private void Apply(in InboxItem item, List<double> frameValues)
    {
        switch (item.Type)
        {
            case DiagnosticsLinkMessageType.Frame:
                ApplyFrame(item.Record, System.Runtime.InteropServices.CollectionsMarshal.AsSpan(frameValues).Slice(item.ValuesStart, item.ValueCount));
                return;
            case DiagnosticsLinkMessageType.Hello:
                Hello = (DiagnosticsLinkHello)item.Message!;
                StatusVersion++;
                return;
            case DiagnosticsLinkMessageType.SessionStarted:
                Gauges = ((DiagnosticsLinkSessionStart)item.Message!).Gauges;
                History = new DiagnosticsFrameHistory(Gauges.Count, historyCapacity);
                IsSimulating = true;
                return;
            case DiagnosticsLinkMessageType.PeriodicReport:
                PreviousPeriodicReport = LatestPeriodicReport;
                LatestPeriodicReport = (DiagnosticsReport)item.Message!;
                PeriodicReportVersion++;
                return;
            case DiagnosticsLinkMessageType.StartupReport:
                StartupReport = (DiagnosticsStartupReport)item.Message!;
                return;
            case DiagnosticsLinkMessageType.BenchmarkCompleted:
                LatestBenchmarkCompletion = (DiagnosticsLinkBenchmarkCompletion)item.Message!;
                return;
            case DiagnosticsLinkMessageType.SessionEnded:
                IsSimulating = false;
                return;
        }
    }

    private void ApplyFrame(in DiagnosticsFrameRecord record, ReadOnlySpan<double> gaugeValues)
    {
        if (record.DroppedBefore > 0)
        {
            DroppedFrameCount += record.DroppedBefore;
            StatusVersion++;
        }

        DrawRate = record.DrawRate;
        UpdateRate = record.UpdateRate;
        IsPaused = record.IsPaused;
        if (record.IsPaused || History is not { } history)
        {
            return;
        }

        LatestFrame = record.Frame;
        history.Record(record.Frame, record.UpdateMilliseconds, gaugeValues);
    }

    /// <summary>One received message waiting for the UI thread; a frame's gauge values are its span of the inbox's FrameValues.</summary>
    private readonly record struct InboxItem(DiagnosticsLinkMessageType Type, object? Message, DiagnosticsFrameRecord Record, int ValuesStart, int ValueCount);

    /// <summary>What arrived between two ApplyPending calls; two are swapped, so receiving never waits on applying.</summary>
    private sealed class Inbox
    {
        public List<InboxItem> Items { get; } = [];

        public List<double> FrameValues { get; } = [];

        public bool HasEnded { get; set; }

        public void Clear()
        {
            Items.Clear();
            FrameValues.Clear();
            HasEnded = false;
        }
    }
}
