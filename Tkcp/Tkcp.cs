using System;
using System.Diagnostics;
using System.Net;
using System.Net.Sockets;

using System.Threading;


namespace Tkcp
{
    public enum ConnectResult
    {
        kSucceed = 0,
        kFailed = 1,
        kAlreadyConnect = 2,
        kUnkown = 3,
    }

   
    public delegate void ConnectEventHandler(ConnectResult result);
    public delegate void DisconnectEventHandler();
    public delegate void MsgEventHandler(byte[] message, int offset, int msgLen);
    public class TkcpClient
    {
        enum State
        {
            kTcpConnecting,
            kTcpConnected,
            kUdpConnectSynSend,
            kConnected,
            kDisconnecting,
            KDisconnected,
        }

        State state_;

        string tcpIp_;
        Int32 tcpPort_;
        TcpClient tcpClient_ = null;

        string udpIp_;
        Int32 udpPort_;
        IPEndPoint udpRemoteEndPoint_ = null;
        UdpClient udpclient_ = null;

        UInt32 conv_;
        KCP kcp_ = null;
        Thread thread_ = null;
        volatile bool run_ = false;
        volatile bool connect = false;

        TcpPacketHead tcpPacketHead_ = new TcpPacketHead();
        int receivedBytes_ = 0;
        byte[] tcpReceiveBuffer_ = new byte[65536];
        byte[] tcpSendBuffer_ = new byte[65536];


        UdpPacketHead udpPacketHead_ = new UdpPacketHead();
        byte[] udpSendBuffer_ = new byte[1500];
        bool udpAvailable_ = false;




        public event ConnectEventHandler ConnectEvent;
        public event DisconnectEventHandler DisconnectEvent;
        public event MsgEventHandler MsgEvent;

        SwitchQueue<Action> actionQueue_ = new SwitchQueue<Action>();

        const Int64 kTicksPerMillisecond = 10 * 1000;
        const Int64 kMillisecondPerSecond = 1000;
        UInt32 nextKcpupdateTime = 0;
        UInt32 trySendConnectSynTimes_ = 0;
        UInt32 nextTrySendConnectSyncTime_ = 0;
        UInt32 now_ = 0;
        UInt32 ticksToMillisecond(Int64 ticks)
        {
            return (UInt32)((ticks / kTicksPerMillisecond) & 0xffffffff);
        }

        public TkcpClient()
        {
            setState(State.kTcpConnecting);
        }

        public void Connect(string ip, Int32 port)
        {
            Debug.Assert(tcpClient_ == null);
            Debug.Assert(run_ == false);
            Debug.Assert(thread_ == null);
            if (connect)
            {
                onConnect(ConnectResult.kAlreadyConnect);
                return;
            }

            connect = true;
            run_ = true;
            tcpIp_ = ip;
            tcpPort_ = port;
            tcpClient_ = new TcpClient();
            thread_ = new Thread(run);
            thread_.Start();
        }

        public bool Connected()
        {
            return state_ == State.kConnected;
        }

        public bool Disconnected()
        {
            return state_ == State.KDisconnected;
        }
        public void Close()
        {
            state_ = State.kDisconnecting;
            actionQueue_.Push(() =>
            {
                Debug.Assert(state_ == State.kDisconnecting);
                state_ = State.KDisconnected;
                run_ = false;
                tcpClient_.Close();
                tcpClient_ = null;
                udpRemoteEndPoint_ = null;
                if (udpAvailable_)
                {
                    kcp_ = null;
                    udpclient_.Close();
                    udpclient_ = null;
                }
                udpAvailable_ = false;
                actionQueue_.Clear();
            });
        }



        void onConnect(ConnectResult result)
        {
            if (ConnectEvent != null)
            {
                ConnectEvent(result);
            }
        }

        void onDisconnect()
        {
            if (DisconnectEvent != null)
            {
                DisconnectEvent();
            }
        }

        void onMsg(byte[] message, int offset, int msgLen)
        {
            if (MsgEvent != null)
            {
                MsgEvent(message, offset, msgLen);
            }
        }

        void setState(State state)
        {
            state_ = state;
        }

        void run()
        {
            tcpConnect();
            while (run_)
            {
                now_ = ticksToMillisecond(DateTime.Now.Ticks);
                processAction();
                receiveTcpData();
                receiveUdpData();
                update();
                Thread.Sleep(1);
            }


        }

        public void Send(byte[] data)
        {
            Send(data, 0, data.Length);
        }

        public void Send(byte[] data, int offset, int count)
        {
            if (state_ != State.kConnected)
            {
                return;
            }

            var sendbuf = new byte[count];
            Array.Copy(data, offset, sendbuf, 0, count);

            actionQueue_.Push(() =>
            {
                if (state_ != State.kConnected)
                {
                    return;
                }

                if (udpAvailable_)
                {
                    sendKcpMsg(sendbuf);
                }
                else
                {
                    sendTcpMsg(sendbuf);
                }
            });
        }

        void sendKcpMsg(byte[] msg)
        {
            kcp_.Send(msg);
            kcp_.flush();
        }

        void sendTcpMsg(byte[] msg)
        {
            sendTcpPacket(TcpPacketId.kData, msg);
        }

        void update()
        {
            if (state_ == State.kUdpConnectSynSend)
            {
                trySendConnectSyn();
            }

            if (udpAvailable_)
            {
                updateKcp();

            }
        }

        void updateKcp()
        {
            Debug.Assert(kcp_ != null);

            if (now_ > nextKcpupdateTime)
            {
                kcp_.Update(now_);
                nextKcpupdateTime = kcp_.Check(now_);
            }
        }
        void tcpConnect()
        {
            Debug.Assert(state_ == State.kTcpConnecting);
            if (run_)
            {
                bool connectSuccess = true;
                try
                {
                    tcpClient_.Connect(tcpIp_, tcpPort_);
                }
                catch (Exception)
                {
                    connectSuccess = false;
                }

                if (!connectSuccess)
                {
                    Console.WriteLine("tcp connect failed");
                    setState(State.KDisconnected);
                    onConnect(ConnectResult.kFailed);
                    run_ = false;
                    return;
                }

                setState(State.kTcpConnected);
            }
        }

        private void processAction()
        {
            actionQueue_.Switch();
            while (!actionQueue_.Empty())
            {
                var action = actionQueue_.Pop();
                action();
            }
        }

        private void receiveUdpData()
        {
            if (udpclient_ != null)
            {
                Debug.Assert(state_ == State.kTcpConnected ||
                             state_ == State.kUdpConnectSynSend ||
                             state_ == State.kConnected);

                if (state_ != State.kTcpConnected && 
                    state_ != State.kUdpConnectSynSend && 
                    state_ != State.kConnected)
                {
                    return;
                }

                while (udpclient_.Available > 0)
                {
                    var receiveBytes = udpclient_.Receive(ref udpRemoteEndPoint_);
                    udpPacketHead_.UnSerialize(receiveBytes, 0, UdpPacketHead.kPacketHeadLength);
                    onUdpMsg(udpPacketHead_.PacketId, receiveBytes,
                        UdpPacketHead.kPacketHeadLength, receiveBytes.Length - UdpPacketHead.kPacketHeadLength);
                }

            }
        }

        void onUdpMsg(UdpPakcetId packetId, byte[] receiveBytes, int offset, int count)
        {
            switch(packetId)
            {
                case UdpPakcetId.kData:
                    onUdpData(receiveBytes, offset, count);
                    break;
                case UdpPakcetId.kPingReply:
                    onPingReply();
                    break;
                case UdpPakcetId.kConnectSynAck:
                    onConnectSyncAck();
                    break;

            }
        }

        private void onConnectSyncAck()
        {
            if (state_ != State.kUdpConnectSynSend)
            {
                return;
            }

            kcp_ = new KCP(conv_, (buffer, count) =>
            {
                sendUdpPacket(UdpPakcetId.kData, buffer, 0, count);
            });

            kcp_.NoDelay(1, 30, 2, 1);
            kcp_.WndSize(128, 128);
            kcp_.SetMtu(576 - 64 - UdpPacketHead.kPacketHeadLength);
            kcp_.SetRXMinrto(10);
            setState(State.kConnected);
            udpAvailable_ = true;
            Console.WriteLine("udp is available");
            onConnect(ConnectResult.kSucceed);
        }

        private void onPingReply()
        {
            throw new NotImplementedException();
        }

        private void onUdpData(byte[] data, int offset, int count)
        {
            if (state_ != State.kConnected)
            {
                return;
            }

            kcp_.Input(data, offset, count);
            for (int size = kcp_.PeekSize(); size > 0; size = kcp_.PeekSize())
            {
                var receiveMessage = new byte[size];
                kcp_.Recv(receiveMessage);
                onMsg(receiveMessage, 0, size);
            }
        }

        private void receiveTcpData()
        {
            if (tcpClient_ == null)
            {
                return;
            }

            Debug.Assert(state_ == State.kTcpConnected ||
                         state_ == State.kUdpConnectSynSend ||
                         state_ == State.kConnected);

            if (state_ != State.kTcpConnected &&
               state_ != State.kUdpConnectSynSend &&
               state_ != State.kConnected)
            {
                return;
            }

            if (!tcpClient_.Connected)
            {
                setState(State.KDisconnected);
                run_ = false;
                onDisconnect();
                return;
            }

            try
            {
                var stream = tcpClient_.GetStream();
                if (stream.DataAvailable)
                {
                    int remain = tcpReceiveBuffer_.Length - receivedBytes_;
                    int nr = stream.Read(tcpReceiveBuffer_, receivedBytes_, remain);
                    receivedBytes_ += nr;

                    int offset = 0;
                    while (receivedBytes_ >= TcpPacketHead.kPacketHeadLength)
                    {
                        tcpPacketHead_.UnSerialize(tcpReceiveBuffer_, offset, offset + TcpPacketHead.kPacketHeadLength);
                        if (receivedBytes_ >= tcpPacketHead_.Len)
                        {
                            onTcpMsg(tcpPacketHead_.PacketId, tcpReceiveBuffer_,
                                offset + TcpPacketHead.kPacketHeadLength, tcpPacketHead_.Len - TcpPacketHead.kPacketHeadLength);
                            receivedBytes_ -= tcpPacketHead_.Len;
                            offset += tcpPacketHead_.Len;
                        }
                        else
                        {
                            break;
                        }
                    }

                    if (receivedBytes_ > 0)
                    {
                        Array.Copy(tcpReceiveBuffer_, offset, tcpReceiveBuffer_, 0, receivedBytes_);
                    }
                }
            }
            catch (Exception msg)
            {
                Console.WriteLine("Exception type {0} data {1} stacktrace {2}", msg.GetType().Name, msg.Data, msg.StackTrace);
                
            }
        }

        void onTcpMsg(TcpPacketId packetId, byte[] buffer, int offset, int count)
        {
            switch (packetId)
            {
                case TcpPacketId.kData:
                    onTcpData(buffer, offset, count);
                    break;
                case TcpPacketId.kUdpConnectionInfo:
                    onUdpConnectionInfo(buffer, offset, count);
                    break;
            }
        }

        void onTcpData(byte[] buffer, int offset, int count)
        {
            onMsg(buffer, offset, count);
        }

        void onUdpConnectionInfo(byte[] buffer, int offset, int count)
        {
            UdpConnectionInfo connetionInfo = new UdpConnectionInfo();
            connetionInfo.UnSerialize(buffer, offset, count);
            conv_ = connetionInfo.Conv;
            udpIp_ = connetionInfo.Ip;
            udpPort_ = connetionInfo.Port;
            udpRemoteEndPoint_ = new IPEndPoint(IPAddress.Parse(udpIp_), udpPort_);
            udpclient_ = new UdpClient();
            udpclient_.Connect(udpIp_, udpPort_);

            Console.WriteLine("conntectionInfo conv {0} udpip {1} udpport {2}", conv_, udpIp_, udpPort_);

            setState(State.kUdpConnectSynSend);
            trySendConnectSyn();
        }


        /// <summary>
        /// 
        /// </summary>
        void trySendConnectSyn()
        {
            if (now_ > nextTrySendConnectSyncTime_ && trySendConnectSynTimes_ < 15)
            {
                trySendConnectSynTimes_++;
                nextTrySendConnectSyncTime_ = now_ + 1000; //+1 second;

                Console.WriteLine("trySendConnectSynTimes {0}", trySendConnectSynTimes_);

                sendUdpPacket(UdpPakcetId.kConnectSyn, null);
                udpclient_.Send(udpSendBuffer_, UdpPacketHead.kPacketHeadLength);
            }
            else if (trySendConnectSynTimes_ >= 15)
            {
                Console.WriteLine("udp not available use tcp");
                udpclient_.Close();
                udpclient_ = null;
                sendTcpPacket(TcpPacketId.kUseTcp, null);
                setState(State.kConnected);
                onConnect(ConnectResult.kSucceed);
            }
        }
        void sendTcpPacket(TcpPacketId packetId, byte[] data)
        {
            sendTcpPacket(packetId, data, 0, data == null ? 0 : data.Length);
        }

        void sendTcpPacket(TcpPacketId packetId, byte[] data, int offset, int count)
        {
            tcpPacketHead_.Len = (UInt16)(TcpPacketHead.kPacketHeadLength + count);
            tcpPacketHead_.PacketId = packetId;
            tcpPacketHead_.Serialize(tcpSendBuffer_, 0, TcpPacketHead.kPacketHeadLength);
            if (data != null)
            {
                Array.Copy(data, offset, tcpSendBuffer_, TcpPacketHead.kPacketHeadLength, count);
            }

            var stream = tcpClient_.GetStream();
            stream.Write(tcpSendBuffer_, 0, tcpPacketHead_.Len);
        }
        void sendUdpPacket(UdpPakcetId packetId, byte[] data)
        {
            sendUdpPacket(packetId, data, 0, data == null ? 0 : data.Length);
        }

        void sendUdpPacket(UdpPakcetId packetId, byte[] data, int offset, int count)
        {
            int sendLen = 0;
            udpPacketHead_.Conv = conv_;
            udpPacketHead_.PacketId = packetId;
            udpPacketHead_.Serialize(udpSendBuffer_, 0, UdpPacketHead.kPacketHeadLength);
            sendLen += UdpPacketHead.kPacketHeadLength;
            if (data != null)
            {
                Array.Copy(data, offset, udpSendBuffer_, UdpPacketHead.kPacketHeadLength, count);
                sendLen += count;
            }

            udpclient_.Send(udpSendBuffer_, sendLen);
        }

    }
}
