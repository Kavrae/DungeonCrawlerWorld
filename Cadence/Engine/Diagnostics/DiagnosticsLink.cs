using System.Collections.Concurrent;
using System.Diagnostics;
using System.Net;
using System.Net.Sockets;

namespace Engine.Diagnostics;

/// <summary>Streams what DiagnosticsEngine measures to an IDE listening on a loopback port: a hello, each session's gauges, a record per frame, and the reports as they are written.</summary>
/// <remarks>
/// The game connects; the IDE listens (it passes "--diagnostics-link=" to the game it starts). Everything on the
/// simulation thread is a write into a preallocated DiagnosticsFrameRing -- no allocation, no lock, no wait -- and a
/// sender thread of its own connects, then drains the ring and the queued messages to the socket every few
/// milliseconds. When the IDE falls behind, the ring overwrites the oldest records and the next record sent says how many.
/// A receiving thread reads what the IDE sends (nothing yet; the link carries both directions). Any failure -- refused,
/// the IDE closed, a write error -- closes the link with one line on the error output; the game runs on without it.
/// It reads only what the trackers already hold, so a seeded run simulates the same world with the link on or off.
/// </remarks>
/// <cleanupVersion>1</cleanupVersion>
public sealed class DiagnosticsLink : IDisposable
{
    /// <summary>Records the ring holds: ten seconds at 60 frames per second.</summary>
    public const int RingCapacity = 600;

    private const int FixedValueCount = 3;
    private const int SendIntervalMilliseconds = 8;
    private const int ConnectTimeoutMilliseconds = 5_000;
    private const int StopTimeoutMilliseconds = 2_000;
    private static readonly long PausedRecordIntervalTimestampTicks = Stopwatch.Frequency;

    private readonly IPEndPoint _endPoint;
    private readonly DiagnosticsLinkHello _hello;
    private readonly TextWriter _errorOutput;
    private readonly ConcurrentQueue<byte[]> _queuedMessages = new();
    private readonly PerformanceCounter _updateCounter = new();
    private readonly PerformanceCounter _drawCounter = new();
    private readonly Lock _clientLock = new();
    private TcpClient? _client;
    private Thread? _senderThread;
    private Thread? _receiverThread;
    private readonly ConcurrentQueue<LinkSession?> _sessionChanges = new();
    private volatile LinkSession? _session;
    private volatile bool _isClosed;
    private volatile bool _isStopping;
    private int _hasReportedClosing;
    private bool _isHostPaused;
    private long _lastFrame;
    private long _lastPausedRecordTimestamp;

    /// <param name="errorOutput">Where the one line saying the link closed goes; Console.Error in the game.</param>
    public DiagnosticsLink(IPEndPoint endPoint, DiagnosticsFeatures features, int? randomSeed, TextWriter errorOutput)
    {
        _endPoint = endPoint;
        _hello = new DiagnosticsLinkHello(DiagnosticsLinkProtocol.Version, Environment.ProcessId, randomSeed, features.ToString());
        _errorOutput = errorOutput;
    }

    /// <summary>True once the link has closed -- it failed, the IDE went away, or it was disposed.</summary>
    public bool IsClosed => _isClosed;

    /// <summary>Starts the sender thread, which connects and sends the hello first.</summary>
    public void Start()
    {
        _senderThread = new Thread(RunSender) { IsBackground = true, Name = "Diagnostics link sender" };
        _senderThread.Start();
    }

    /// <summary>A simulated session began with gauges, whose values every frame record carries in this order. Simulation thread.</summary>
    public void SessionStarted(IReadOnlyList<Gauge> gauges)
    {
        if (_isClosed)
        {
            return;
        }

        var descriptors = new DiagnosticsLinkGaugeDescriptor[gauges.Count];
        for (var gaugeIndex = 0; gaugeIndex < gauges.Count; gaugeIndex++)
        {
            var gauge = gauges[gaugeIndex];
            descriptors[gaugeIndex] = new DiagnosticsLinkGaugeDescriptor(gauge.GroupName, gauge.GaugeName, gauge.Kind, GaugeUnits.Of(gauge.GaugeName));
        }

        var session = new LinkSession(new DiagnosticsLinkSessionStart(descriptors), new DiagnosticsFrameRing(RingCapacity, FixedValueCount + gauges.Count));
        _session = session;
        _sessionChanges.Enqueue(session);
    }

    /// <summary>The simulated session ended; what it recorded is still sent. Simulation thread.</summary>
    public void SessionEnded()
    {
        if (_session is null)
        {
            return;
        }

        _session = null;
        _sessionChanges.Enqueue(null);
    }

    /// <summary>Records frame's cost and gauges' latest values, which hold one value per gauge SessionStarted named. Simulation thread; allocates nothing.</summary>
    public void RecordFrame(long frame, TimeSpan elapsed, GaugeTracker? gauges)
    {
        _lastFrame = frame;
        if (_isClosed || _session is not { } session)
        {
            return;
        }

        var values = session.Ring.BeginWrite(frame, isPaused: false);
        values[0] = elapsed.TotalMilliseconds;
        values[1] = _drawCounter.RatePerSecond;
        values[2] = _updateCounter.RatePerSecond;
        if (gauges is not null)
        {
            for (var gaugeIndex = FixedValueCount; gaugeIndex < values.Length; gaugeIndex++)
            {
                values[gaugeIndex] = gauges.GetLatestValue(gaugeIndex - FixedValueCount);
            }
        }

        session.Ring.CommitWrite();
    }

    /// <summary>Counts the host's update; while it runs no simulation, records a paused record once a second, so the IDE sees the pause and the host's rates. Host thread.</summary>
    public void HostUpdated(bool simulated)
    {
        _updateCounter.Tick();
        if (simulated)
        {
            _isHostPaused = false;
            return;
        }

        var now = Stopwatch.GetTimestamp();
        if (_isHostPaused && now - _lastPausedRecordTimestamp < PausedRecordIntervalTimestampTicks)
        {
            return;
        }

        _isHostPaused = true;
        _lastPausedRecordTimestamp = now;
        if (_isClosed || _session is not { } session)
        {
            return;
        }

        var values = session.Ring.BeginWrite(_lastFrame, isPaused: true);
        values.Clear();
        values[1] = _drawCounter.RatePerSecond;
        values[2] = _updateCounter.RatePerSecond;
        session.Ring.CommitWrite();
    }

    /// <summary>Counts the host's draw. Host thread.</summary>
    public void HostDrew() => _drawCounter.Tick();

    /// <summary>Sends a periodic report: the JSON latest.json holds.</summary>
    public void SendPeriodicReport(string reportJson) => Queue(DiagnosticsLinkMessageType.PeriodicReport, reportJson);

    /// <summary>Sends the startup report: the JSON its file holds.</summary>
    public void SendStartupReport(string reportJson) => Queue(DiagnosticsLinkMessageType.StartupReport, reportJson);

    /// <summary>Sends where a closed benchmark range's reports were written.</summary>
    public void SendBenchmarkCompleted(DiagnosticsLinkBenchmarkCompletion completion)
    {
        if (!_isClosed)
        {
            _queuedMessages.Enqueue(DiagnosticsLinkProtocol.JsonMessage(DiagnosticsLinkMessageType.BenchmarkCompleted, completion));
        }
    }

    /// <summary>Sends what is still queued, then closes the link and waits briefly for its threads.</summary>
    public void Dispose()
    {
        _isStopping = true;
        _senderThread?.Join(StopTimeoutMilliseconds);
        CloseClient();
        _receiverThread?.Join(StopTimeoutMilliseconds);
        _isClosed = true;
    }

    private void Queue(DiagnosticsLinkMessageType type, string json)
    {
        if (!_isClosed)
        {
            _queuedMessages.Enqueue(DiagnosticsLinkProtocol.JsonMessage(type, json));
        }
    }

    private void RunSender()
    {
        NetworkStream stream;
        try
        {
            var client = new TcpClient(_endPoint.AddressFamily) { NoDelay = true };
            lock (_clientLock)
            {
                _client = client;
            }

            if (!client.ConnectAsync(_endPoint).Wait(ConnectTimeoutMilliseconds))
            {
                Close($"no answer from {_endPoint} within {ConnectTimeoutMilliseconds / 1000} s");
                return;
            }

            stream = client.GetStream();
        }
        catch (Exception exception)
        {
            Close($"couldn't connect to {_endPoint} ({exception.GetBaseException().Message})");
            return;
        }

        _receiverThread = new Thread(() => RunReceiver(stream)) { IsBackground = true, Name = "Diagnostics link receiver" };
        _receiverThread.Start();

        try
        {
            using var output = new BufferedStream(stream, 64 * 1024);
            var sender = new LinkSender(output);
            output.Write(DiagnosticsLinkProtocol.JsonMessage(DiagnosticsLinkMessageType.Hello, _hello));
            output.Flush();

            while (!_isClosed)
            {
                var isLastPass = _isStopping;
                sender.SendSessions(_sessionChanges);
                while (_queuedMessages.TryDequeue(out var message))
                {
                    output.Write(message);
                }

                output.Flush();
                if (isLastPass)
                {
                    return;
                }

                Thread.Sleep(SendIntervalMilliseconds);
            }
        }
        catch (Exception exception)
        {
            Close($"sending failed ({exception.GetBaseException().Message})");
        }
    }

    private void RunReceiver(NetworkStream stream)
    {
        var reader = new DiagnosticsLinkMessageReader(stream);
        try
        {
            while (reader.TryRead(out _, out _))
            {
            }

            Close("the IDE closed it");
        }
        catch (Exception exception)
        {
            Close($"receiving failed ({exception.GetBaseException().Message})");
        }
    }

    /// <summary>Closes the link; the first close that isn't Dispose's writes its one line.</summary>
    private void Close(string reason)
    {
        if (!_isStopping && Interlocked.Exchange(ref _hasReportedClosing, 1) == 0)
        {
            _errorOutput.WriteLine($"[Diagnostics] The diagnostics link to {_endPoint} closed: {reason}. The game runs on without it.");
        }

        _isClosed = true;
        CloseClient();
    }

    private void CloseClient()
    {
        lock (_clientLock)
        {
            _client?.Dispose();
        }
    }

    /// <summary>One session's gauges and the ring its frames go through.</summary>
    private sealed record LinkSession(DiagnosticsLinkSessionStart Start, DiagnosticsFrameRing Ring);

    /// <summary>The sender thread's state: the session it is sending and the buffers it encodes records with.</summary>
    private sealed class LinkSender(Stream output)
    {
        private readonly byte[] _header = new byte[DiagnosticsLinkProtocol.HeaderByteCount];
        private LinkSession? _sendingSession;
        private double[] _values = [];
        private byte[] _record = [];

        /// <summary>Takes each session change in order -- finishing the session being sent before announcing the next -- then sends the current session's frames.</summary>
        public void SendSessions(ConcurrentQueue<LinkSession?> sessionChanges)
        {
            while (sessionChanges.TryDequeue(out var next))
            {
                if (_sendingSession is { } previous)
                {
                    SendFrames(previous);
                    output.Write(DiagnosticsLinkProtocol.EmptyMessage(DiagnosticsLinkMessageType.SessionEnded));
                }

                _sendingSession = next;
                if (next is not null)
                {
                    output.Write(DiagnosticsLinkProtocol.JsonMessage(DiagnosticsLinkMessageType.SessionStarted, next.Start));
                    _values = new double[next.Ring.ValueCount];
                    _record = new byte[DiagnosticsFrameRecord.ByteCountFor(next.Ring.ValueCount - FixedValueCount)];
                }
            }

            if (_sendingSession is { } session)
            {
                SendFrames(session);
            }
        }

        private void SendFrames(LinkSession session)
        {
            while (session.Ring.TryRead(_values, out var frame, out var isPaused, out var droppedBefore))
            {
                var record = new DiagnosticsFrameRecord(frame, _values[0], _values[1], _values[2], isPaused, droppedBefore);
                var byteCount = DiagnosticsFrameRecord.Write(record, _values.AsSpan(FixedValueCount), _record);
                DiagnosticsLinkProtocol.WriteHeader(_header, DiagnosticsLinkMessageType.Frame, byteCount);
                output.Write(_header);
                output.Write(_record, 0, byteCount);
            }
        }
    }
}
