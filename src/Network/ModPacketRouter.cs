using System.IO;
using DirectConnectIP.Network.Packets;

namespace DirectConnectIP.Network;

public static class ModPacketRouter
{
    private static readonly byte[] MagicHeader = "DCIP"u8.ToArray();

    public static bool IsModPacket(byte[] data)
    {
        return data.Length >= 5 &&
               data[0] == MagicHeader[0] && data[1] == MagicHeader[1] &&
               data[2] == MagicHeader[2] && data[3] == MagicHeader[3];
    }

    public static byte[] Serialize(IModPacket packet)
    {
        using var ms = new MemoryStream();
        using var writer = new BinaryWriter(ms);
            
        writer.Write(MagicHeader);
        writer.Write((byte)packet.Type);
        packet.Serialize(writer);
            
        return ms.ToArray();
    }

    public static IModPacket Deserialize(byte[] data)
    {
        using var ms = new MemoryStream(data);
        using var reader = new BinaryReader(ms);
            
        reader.ReadBytes(4);
        var type = (ModPacketType)reader.ReadByte();

        IModPacket packet = type switch
        {
            ModPacketType.SyncClientName => new SyncClientNamePacket(),
            ModPacketType.SyncFullList => new SyncFullListPacket(),
            ModPacketType.SyncSingle => new SyncSinglePacket(),
            ModPacketType.SyncRemove => new SyncRemovePacket(),
            _ => null
        };

        packet?.Deserialize(reader);
        return packet;
    }
}