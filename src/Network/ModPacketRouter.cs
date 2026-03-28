using System;
using System.IO;
using DirectConnectIP.Network.Packets;
using MegaCrit.Sts2.Core.Logging;

namespace DirectConnectIP.Network;

public static class ModPacketRouter
{
    public const int Channel = 15; 
    private static readonly byte[] MagicHeader = "DCIP"u8.ToArray();
    private const byte ProtocolVersion = 1;

    public static bool IsModPacket(byte[] data)
    {
        return data.Length >= 6 &&
               data[0] == MagicHeader[0] && 
               data[1] == MagicHeader[1] &&
               data[2] == MagicHeader[2] && 
               data[3] == MagicHeader[3] &&
               data[4] == ProtocolVersion;
    }

    public static byte[] Serialize(IModPacket packet)
    {
        using var ms = new MemoryStream();
        using var writer = new BinaryWriter(ms);
            
        writer.Write(MagicHeader);
        writer.Write(ProtocolVersion);
        writer.Write((byte)packet.Type);
        packet.Serialize(writer);
            
        return ms.ToArray();
    }

    public static IModPacket Deserialize(byte[] data)
    {
        ModPacketType? parsedType = null;
        try
        {
            using var ms = new MemoryStream(data);
            using var reader = new BinaryReader(ms);
                
            reader.ReadBytes(4);
            reader.ReadByte();
            
            var typeByte = reader.ReadByte();
            parsedType = (ModPacketType)typeByte;

            IModPacket packet = parsedType switch
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
        catch (Exception ex)
        {
            var packetTypeStr = parsedType.HasValue ? parsedType.Value.ToString() : "解析包类型前已崩溃";
            LogNetworkCrash(ex, data, "模组数据包反序列化失败", Channel, packetTypeStr);

            return null;
        }
    }
    
    public static void LogNetworkCrash(Exception ex, byte[] rawData, string contextMessage, int? channel = null, string packetTypeInfo = null)
    {
        var dataLength = rawData?.Length ?? 0;
        var hexDump = "null";
        
        if (rawData is { Length: > 0 })
        {
            var extractLength = Math.Min(rawData.Length, 64);
            hexDump = BitConverter.ToString(rawData, 0, extractLength);
            if (rawData.Length > 64) hexDump += "...(截断)";
        }
            
        var channelInfo = channel.HasValue ? $" (Channel: {channel.Value})" : "";
        var packetTypeLine = !string.IsNullOrEmpty(packetTypeInfo) ? $" 尝试解析的包类型: {packetTypeInfo}\n" : "";

        Log.Error(
            $"[DirectConnectIP] 网络数据包处理发生致命异常！\n" +
            $"--------------------------------------------------\n" +
            $"Error Context: {contextMessage}\n" +
            $"DataStream: {channelInfo}\n" +
            packetTypeLine +
            $"DataPack Length: {dataLength} bytes\n" +
            $"(Hex): {hexDump}\n" +
            "--------------------------------------------------\n" +
            $"StackTrace:\n{ex}\n" +
            "--------------------------------------------------");
    }
}