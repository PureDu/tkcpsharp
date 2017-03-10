using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Text;
using System.Threading.Tasks;

namespace Tkcp
{
    enum TcpPacketId
    {
        kUdpConnectionInfo = 1,
        kData = 2,
        kUseTcp = 3,
        kPingRequest = 4,
        kPingReply = 5,
    }
    class TcpPacketHead
    {
        public readonly static  int kPacketHeadLength = 3;
        public  UInt16 Len;
        public TcpPacketId PacketId;

        public virtual Int32 Serialize(byte[] buf, int offset, int count)
        {
            MemoryStream memoryStream = new MemoryStream(buf, offset, count);
            BinaryWriter binaryWriter = new BinaryWriter(memoryStream);
            binaryWriter.Write(IPAddress.HostToNetworkOrder((Int16)Len));
            binaryWriter.Write((byte)PacketId);
            return (Int32)memoryStream.Position;
        }

        public virtual Int32 UnSerialize(byte[] buf, int offset, int count)
        {
            MemoryStream memoryStream = new MemoryStream(buf, offset, count);
            BinaryReader binaryReader = new BinaryReader(memoryStream);
            Len = (UInt16)IPAddress.NetworkToHostOrder(binaryReader.ReadInt16());
            PacketId = (TcpPacketId)binaryReader.ReadByte();
            return (Int32)memoryStream.Position;
        }
    }

    class UdpConnectionInfo
    {
        public UInt32 Conv;
        public string Ip;
        public UInt16 Port;
        public Int32 Serialize(byte[] buf, int offset, int count)
        {
            MemoryStream memoryStream = new MemoryStream(buf, offset, count);
            BinaryWriter binaryWriter = new BinaryWriter(memoryStream);
            binaryWriter.Write(IPAddress.HostToNetworkOrder((Int32)Conv));
            var ipBytes = Encoding.Default.GetBytes(Ip);
            binaryWriter.Write(IPAddress.HostToNetworkOrder((Int16)ipBytes.Length));
            binaryWriter.Write(ipBytes);
            binaryWriter.Write(IPAddress.HostToNetworkOrder((Int16)Port));
            return (Int32)memoryStream.Position;
        }

        public  Int32 UnSerialize(byte[] buf, int offset, int count)
        {
            MemoryStream memoryStream = new MemoryStream(buf, offset, count);
            BinaryReader binaryReader = new BinaryReader(memoryStream);
            Conv = (UInt32)IPAddress.NetworkToHostOrder(binaryReader.ReadInt32());
            var ipLen = (UInt16)IPAddress.NetworkToHostOrder(binaryReader.ReadInt16());
            var ipBytes = binaryReader.ReadBytes(ipLen);
            Ip = Encoding.Default.GetString(ipBytes);
            Port = (UInt16)IPAddress.NetworkToHostOrder(binaryReader.ReadInt16());
            return (Int32)memoryStream.Position;
        }
    }
    
    enum UdpPakcetId
    {
        kConnectSyn = 100,
        kConnectSynAck = 101,
        kPingRequest = 102,
        kPingReply = 103,
        kData = 104,
    }
    class UdpPacketHead
    {
        public readonly static int kPacketHeadLength = 5;

        public UInt32 Conv;
        public UdpPakcetId PacketId;
        public virtual Int32 Serialize(byte[] buf, int offset, int count)
        {
            MemoryStream memoryStream = new MemoryStream(buf, offset, count);
            BinaryWriter binaryWriter = new BinaryWriter(memoryStream);
            binaryWriter.Write(IPAddress.HostToNetworkOrder((Int32)Conv));
            binaryWriter.Write((byte)PacketId);
            return (Int32)memoryStream.Position;
        }

        public virtual Int32 UnSerialize(byte[] buf, int offset, int count)
        {
            MemoryStream memoryStream = new MemoryStream(buf, offset, count);
            BinaryReader binaryReader = new BinaryReader(memoryStream);
            Conv = (UInt32)IPAddress.NetworkToHostOrder(binaryReader.ReadUInt32());
            PacketId = (UdpPakcetId)binaryReader.ReadByte();
            return (Int32)memoryStream.Position;
        }
    }
}
