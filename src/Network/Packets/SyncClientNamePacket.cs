using System.IO;

namespace DirectConnectIP.Network.Packets;

public class SyncClientNamePacket : IModPacket
{
    public ModPacketType Type => ModPacketType.SyncClientName;
    public string PlayerName { get; private set; }

    public SyncClientNamePacket() { }
    public SyncClientNamePacket(string name) { PlayerName = name; }

    public void Serialize(BinaryWriter writer) => writer.Write(PlayerName);
    public void Deserialize(BinaryReader reader) => PlayerName = reader.ReadString();
}