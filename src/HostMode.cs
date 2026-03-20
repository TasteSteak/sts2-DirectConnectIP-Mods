namespace DirectConnectIP
{
    public enum HostMode
    {
        Steam,
        ENet
    }

    public static class HostModeSettings
    {
        public static HostMode CurrentMode { get; set; } = HostMode.Steam;
        public const int MaxDirectPlayers = 16;
    }
}