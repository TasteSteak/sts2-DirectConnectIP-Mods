using System.IO;

namespace DirectConnectIP.Network.Packets;

public interface IModPacket
{
    ModPacketType Type { get; }
    void Serialize(BinaryWriter writer);
    void Deserialize(BinaryReader reader);
}