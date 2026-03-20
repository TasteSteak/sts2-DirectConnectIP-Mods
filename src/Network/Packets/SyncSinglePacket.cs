using System.IO;

namespace DirectConnectIP.Network.Packets;

public class SyncSinglePacket : IModPacket
{
    public ModPacketType Type => ModPacketType.SyncSingle;
    public ulong PlayerId { get; private set; }
    public string PlayerName { get; private set; }

    public SyncSinglePacket() { }
    public SyncSinglePacket(ulong id, string name) { PlayerId = id; PlayerName = name; }

    public void Serialize(BinaryWriter writer)
    {
        writer.Write(PlayerId);
        writer.Write(PlayerName);
    }
    public void Deserialize(BinaryReader reader)
    {
        PlayerId = reader.ReadUInt64();
        PlayerName = reader.ReadString();
    }
}