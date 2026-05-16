namespace DirectConnectIP.Network;

internal static class EnetTransportSettings
{
    public const int ClientHostMaxPeers = 32;
    public const int ChannelCount = 0;
    public const int UnlimitedBandwidth = 0;
    public const int ConnectData = 0;
    public const int PollIntervalMs = 100;
    public const int ConnectTimeoutMs = 10_000;
    public const int HandshakeDelayMs = 10;
    public const int PeerTimeoutLimit = 24;
    public const int PeerTimeoutMinMs = 20_000;
    public const int PeerTimeoutMaxMs = 20_000;
    public static readonly string[] HostBindAddresses = ["0.0.0.0", "::"];
}
