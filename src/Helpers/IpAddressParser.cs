using System;
using System.Net;
using System.Net.Sockets;

namespace DirectConnectIP.Helpers;

internal static class IpAddressParser
{
    public static bool TryParse(string input, out string host, out ushort port)
    {
        host = string.Empty;
        port = 33771;
        if (string.IsNullOrWhiteSpace(input)) return false;

        var raw = input.Trim();
        if (raw.StartsWith('['))
        {
            var end = raw.IndexOf(']');
            if (end <= 0) return false;
            
            if (raw.Length > end + 1 && raw[end + 1] == ':')
            {
                if (!ushort.TryParse(raw.AsSpan(end + 2), out port)) return false;
            }
            host = raw[1..end];
        }
        else
        {
            var lastColon = raw.LastIndexOf(':');
            if (lastColon > 0 && raw.IndexOf(':') == lastColon)
            {
                if (!ushort.TryParse(raw.AsSpan(lastColon + 1), out port)) return false;
                host = raw[..lastColon];
            }
            else 
            {
                host = raw;
            }
        }

        return IsValidHost(host) && port > 0;
    }

    private static bool IsValidHost(string host)
    {
        if (host.Equals("localhost", StringComparison.OrdinalIgnoreCase)) return true;

        if (!IPAddress.TryParse(host, out var ip)) return Uri.CheckHostName(host) == UriHostNameType.Dns && host.Contains('.');
        if (ip.AddressFamily == AddressFamily.InterNetwork)
        {
            return host.AsSpan().Count('.') == 3;
        }
        return true;

    }
}