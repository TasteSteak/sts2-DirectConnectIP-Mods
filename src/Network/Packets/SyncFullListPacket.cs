using System.Collections.Generic;
using System.IO;

namespace DirectConnectIP.Network.Packets;

public class SyncFullListPacket : IModPacket
{
    public ModPacketType Type => ModPacketType.SyncFullList;
    public Dictionary<ulong, string> Players { get; } = new();

    public void Serialize(BinaryWriter writer)
    {
        writer.Write(Players.Count);
        foreach (var kvp in Players)
        {
            writer.Write(kvp.Key);
            writer.Write(kvp.Value);
        }
    }

    public void Deserialize(BinaryReader reader)
    {
        var count = reader.ReadInt32();
        Players.Clear();
        for (var i = 0; i < count; i++)
        {
            Players[reader.ReadUInt64()] = reader.ReadString();
        }
    }
}