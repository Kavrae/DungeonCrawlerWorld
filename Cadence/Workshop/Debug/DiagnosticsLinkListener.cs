using Engine.Diagnostics;
using System.Net;
using System.Net.Sockets;

namespace Workshop.Debug;

/// <summary>The Workshop's end of the diagnostics link: a loopback port the game Play starts connects to, and the session of the game connected last.</summary>
/// <remarks>
/// The operating system picks the port; LaunchArgument is what Play adds to the game's command line. One game at a time:
/// a connection while another is still open is closed at once. Each connection gets a fresh LiveDiagnosticsSession,
/// parsed on a receiving thread of its own; Update hands it to the UI thread once per frame, and the last one stays --
/// ended -- until the next game connects.
/// </remarks>
public sealed class DiagnosticsLinkListener : IDisposable
{
    private const int StopTimeoutMilliseconds = 2_000;

    private readonly TcpListener _listener = new(IPAddress.Loopback, 0);
    private readonly int _historyCapacity;
    private readonly Lock _connectionLock = new();
    private Thread? _acceptThread;
    private TcpClient? _connectedClient;
    private Thread? _receiverThread;
    private LiveDiagnosticsSession? _newestSession;
    private bool _isStopping;

    public DiagnosticsLinkListener(int historyCapacity = DiagnosticsFrameHistory.DefaultCapacity)
    {
        _historyCapacity = historyCapacity;
    }

    /// <summary>The port the game connects to; valid once Start has run.</summary>
    public IPEndPoint EndPoint => (IPEndPoint)_listener.LocalEndpoint;

    /// <summary>The argument that tells a game where to connect.</summary>
    public string LaunchArgument => DiagnosticsLinkArgument.Format(EndPoint);

    /// <summary>The session of the game that connected last, as of the last Update; null before any.</summary>
    public LiveDiagnosticsSession? Session { get; private set; }

    /// <summary>Opens the port and starts accepting.</summary>
    public void Start()
    {
        _listener.Start();
        _acceptThread = new Thread(AcceptConnections) { IsBackground = true, Name = "Diagnostics link listener" };
        _acceptThread.Start();
    }

    /// <summary>Takes up the newest connection's session and applies what it received since the last call. UI thread, once per frame.</summary>
    public void Update()
    {
        lock (_connectionLock)
        {
            Session = _newestSession;
        }

        Session?.ApplyPending();
    }

    public void Dispose()
    {
        Thread? receiverThread;
        lock (_connectionLock)
        {
            _isStopping = true;
            _connectedClient?.Dispose();
            receiverThread = _receiverThread;
        }

        _listener.Stop();
        _acceptThread?.Join(StopTimeoutMilliseconds);
        receiverThread?.Join(StopTimeoutMilliseconds);
    }

    private void AcceptConnections()
    {
        while (true)
        {
            TcpClient client;
            try
            {
                client = _listener.AcceptTcpClient();
            }
            catch (Exception)
            {
                return;
            }

            lock (_connectionLock)
            {
                if (_isStopping || _connectedClient is not null)
                {
                    client.Dispose();
                    continue;
                }

                var session = new LiveDiagnosticsSession(_historyCapacity);
                _connectedClient = client;
                _newestSession = session;
                _receiverThread = new Thread(() => ReceiveFrom(client, session)) { IsBackground = true, Name = "Diagnostics link receiver" };
                _receiverThread.Start();
            }
        }
    }

    private void ReceiveFrom(TcpClient client, LiveDiagnosticsSession session)
    {
        try
        {
            var reader = new DiagnosticsLinkMessageReader(client.GetStream());
            while (reader.TryRead(out var type, out var payload))
            {
                session.Receive(type, payload.Span);
            }
        }
        catch (InvalidDataException exception)
        {
            Console.Error.WriteLine($"Workshop: the diagnostics link sent something it doesn't understand, so it was closed: {exception.Message}");
        }
        catch (Exception)
        {
        }
        finally
        {
            session.MarkEnded();
            lock (_connectionLock)
            {
                client.Dispose();
                _connectedClient = null;
            }
        }
    }
}
