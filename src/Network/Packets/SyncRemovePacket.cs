using System.IO;

namespace DirectConnectIP.Network.Packets;

public class SyncRemovePacket : IModPacket
{
    public ModPacketType Type => ModPacketType.SyncRemove;
    public ulong PlayerId { get; private set; }

    public SyncRemovePacket() { }
    public SyncRemovePacket(ulong id) { PlayerId = id; }

    public void Serialize(BinaryWriter writer) => writer.Write(PlayerId);
    public void Deserialize(BinaryReader reader) => PlayerId = reader.ReadUInt64();
}