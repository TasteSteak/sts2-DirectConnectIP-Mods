namespace DirectConnectIP.Network.Packets;

public enum ModPacketType : byte
{
    SyncClientName = 0,
    SyncFullList = 1,
    SyncSingle = 2,
    SyncRemove = 3
}