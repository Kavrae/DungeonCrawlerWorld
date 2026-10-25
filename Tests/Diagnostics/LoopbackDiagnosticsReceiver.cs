using System.Collections.Concurrent;
using System.Net;
using System.Net.Sockets;
using Engine.Diagnostics;

namespace Tests.Diagnostics;

/// <summary>A test's end of a diagnostics link: listens on a loopback port, accepts one game, and keeps every message it sends.</summary>
internal sealed class LoopbackDiagnosticsReceiver : IDisposable
{
    private static readonly TimeSpan WaitTimeout = TimeSpan.FromSeconds(10);

    private readonly TcpListener _listener = new(IPAddress.Loopback, 0);
    private readonly ConcurrentQueue<(DiagnosticsLinkMessageType Type, byte[] Payload)> _messages = new();
    private readonly Thread _thread;
    private TcpClient? _client;
    private volatile bool _hasEnded;

    public LoopbackDiagnosticsReceiver()
    {
        _listener.Start();
        EndPoint = (IPEndPoint)_listener.LocalEndpoint;
        _thread = new Thread(Receive) { IsBackground = true };
        _thread.Start();
    }

    public IPEndPoint EndPoint { get; }

    public bool HasEnded => _hasEnded;

    public IReadOnlyList<(DiagnosticsLinkMessageType Type, byte[] Payload)> Messages => [.. _messages];

    /// <summary>Waits until a message of type has arrived and returns the first one.</summary>
    public byte[] WaitFor(DiagnosticsLinkMessageType type)
    {
        var deadline = DateTime.UtcNow + WaitTimeout;
        while (DateTime.UtcNow < deadline)
        {
            foreach (var message in _messages)
            {
                if (message.Type == type)
                {
                    return message.Payload;
                }
            }

            Thread.Sleep(5);
        }

        throw new AssertFailedException($"No {type} message arrived within {WaitTimeout.TotalSeconds} s.");
    }

    /// <summary>Waits until count messages of type have arrived.</summary>
    public void WaitForCount(DiagnosticsLinkMessageType type, int count)
    {
        var deadline = DateTime.UtcNow + WaitTimeout;
        while (_messages.Count(message => message.Type == type) < count)
        {
            if (DateTime.UtcNow > deadline)
            {
                throw new AssertFailedException($"Fewer than {count} {type} messages arrived within {WaitTimeout.TotalSeconds} s.");
            }

            Thread.Sleep(5);
        }
    }

    public void Dispose()
    {
        _listener.Stop();
        _client?.Dispose();
        _thread.Join(WaitTimeout);
    }

    private void Receive()
    {
        try
        {
            _client = _listener.AcceptTcpClient();
            var reader = new DiagnosticsLinkMessageReader(_client.GetStream());
            while (reader.TryRead(out var type, out var payload))
            {
                _messages.Enqueue((type, payload.ToArray()));
            }
        }
        catch (Exception exception) when (exception is SocketException or IOException or ObjectDisposedException or InvalidOperationException)
        {
        }
        finally
        {
            _hasEnded = true;
        }
    }
}
