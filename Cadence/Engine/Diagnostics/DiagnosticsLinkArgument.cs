using System.Net;

namespace Engine.Diagnostics;

/// <summary>Parses and makes the "--diagnostics-link=127.0.0.1:PORT" command-line argument: where a listening IDE waits for the game's diagnostics.</summary>
/// <remarks>Loopback only: an address that isn't loopback, a missing port or a malformed value reads as no link, so a typo leaves the game unlinked rather than stopping it.</remarks>
/// <cleanupVersion>1</cleanupVersion>
public static class DiagnosticsLinkArgument
{
    private const string ArgumentPrefix = "--diagnostics-link=";

    /// <summary>The link's endpoint, or null when args has none or it is not a loopback address and port.</summary>
    public static IPEndPoint? Parse(IEnumerable<string> args)
    {
        foreach (var arg in args)
        {
            if (arg.StartsWith(ArgumentPrefix, StringComparison.OrdinalIgnoreCase))
            {
                return ParseValue(arg[ArgumentPrefix.Length..]);
            }
        }

        return null;
    }

    /// <summary>The argument that makes the game connect to endPoint.</summary>
    public static string Format(IPEndPoint endPoint) => $"{ArgumentPrefix}{endPoint}";

    private static IPEndPoint? ParseValue(string value)
    {
        if (!IPEndPoint.TryParse(value, out var endPoint) || endPoint.Port == 0 || !IPAddress.IsLoopback(endPoint.Address))
        {
            return null;
        }

        return endPoint;
    }
}
