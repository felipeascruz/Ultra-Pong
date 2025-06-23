#nullable enable
using System;
using System.Collections.Generic;
using System.Text;
using static System.BitConverter;
using Godot;

namespace UltraPong;

public partial class HolePuncher : Node
{
    [Signal]
    public delegate void HolePunchedEventHandler(int myPort, int hostsPort, string hostsAddress);
    
    [Signal]
    public delegate void SessionRegisteredEventHandler();
    
    private const string SERVER_IP = "20.206.244.22";
    private const int SERVER_PORT = 1910;

    public bool IsHost { get; private set; }

    private static readonly StreamPeerTcp ServerTcp = new();
    private static readonly PacketPeerUdp PeerUdp = new();
    
    private int _ownPort;
    
    private Dictionary<byte[], Peer>? _peers;

    private MessageTypes _punchStep;

    private Timer _pingPeerTimer = new();

    private byte _messagesSentRange;

    private byte _messagesSent;

    public override void _Ready()
    {
        _pingPeerTimer.WaitTime = 0.1d;
        _pingPeerTimer.Connect("timeout", new Callable(this, nameof(PingPeer)));
        
        AddChild(_pingPeerTimer);
    }
    
    public void ConnectToServer(bool isHost, string? room, string? nickname, byte maxPeers = 4)
    {
        IsHost = isHost;
        _peers = new Dictionary<byte[], Peer>(maxPeers);
        
        var error = ServerTcp.ConnectToHost(SERVER_IP, SERVER_PORT);
        if (error != Error.Ok)
        {
            GD.PrintErr("Error connecting to server: " + error);
            return;
        }
        
        var roomClientBytes = Encoding.ASCII.GetBytes($"{room}:{nickname}");
        
        var data = new byte[1 + roomClientBytes.Length];
        //Store isHost on MSB of the first byte and the Message Type on the other 7 bits
        data[0] = (byte)((isHost ? 0x80 : 0x00) | (byte)MessageTypes.SendRegister);
        //Store roomClient string after the first byte
        Array.Copy(roomClientBytes, 0, data, 1, roomClientBytes.Length);
        
        error = ServerTcp.PutData(data);
        
        if (error != Error.Ok)
            GD.PrintErr("Error sending server TCP packet: " + error);
    }
    
    //Process is only used for listening
    public override void _Process(double delta)
    {
        ServerTcp.Poll();
        
        //HandleServerMessages
        if (ServerTcp.GetStatus() is StreamPeerTcp.Status.Connected && ServerTcp.GetAvailableBytes() > 0)
        {
            //Method GetData returns a Godot.Array with an error code and the data
            var dataArray = ServerTcp.GetData(ServerTcp.GetAvailableBytes());

            var error = dataArray[0].As<Error>();
            if (error != Error.Ok)
                GD.PrintErr("Error receiving server TCP packet: " + error);

            var data = dataArray[1].As<byte[]>();

            var dataType = (MessageTypes)data[0];

            if (dataType is MessageTypes.ReceiveOwnPort)
            {
                _ownPort = ToInt32(data, 1);
                error = PeerUdp.Bind(_ownPort);
                if (error != Error.Ok)
                    GD.PrintErr($"Error binding on port {_ownPort}: " + error);
            }
            else if (dataType is MessageTypes.ReceivePeerInfo)
            {
                var publicIp = new ArraySegment<byte>(data, 1, 4).ToArray();
                var privateIp = new ArraySegment<byte>(data, 5, 4).ToArray();
                var port = ToInt32(data, 9);

                _peers?.Add(publicIp, new Peer(port, privateIp));
                PeerUdp.SetDestAddress(BytesToIpv4(publicIp), port);
                _pingPeerTimer.Start();
            }
            else
                GD.PrintErr("Unknown server message type: " + dataType);
        }

        //HandlePeerMessages
        if (PeerUdp.IsBound() && PeerUdp.GetAvailablePacketCount() > 0)
        {
            var error = PeerUdp.GetPacketError();
            if (error != Error.Ok)
                GD.PrintErr("Error receiving peer UDP packet: " + error);

            var data = PeerUdp.GetPacket();

            var dataType = (MessageTypes)data[0];

            if (dataType is MessageTypes.Greet or MessageTypes.Confirm)
            {
                _ownPort = ToInt32(data, 1);
                if (_peers != null) _peers[Ipv4ToBytes(PeerUdp.GetPacketIP())].Port = PeerUdp.GetPacketPort();
                _punchStep = dataType + 1;

                if (!IsHost)
                {
                    error = PeerUdp.Bind(_ownPort);
                    if (error != Error.Ok)
                        GD.PrintErr($"Error binding on port {_ownPort}: " + error);
                }

                _messagesSent = 0;
            }
            else if (dataType is MessageTypes.Go)
                HandleGoMessage();
            else
                GD.PrintErr("Unknown peer message type: " + dataType);
        }
    }
    
    // Signaled through Ping Peer Timer
    private void PingPeer()
    {
        var data = new byte[5];
        data[0] = (byte)_punchStep;
        
        // Only the host should cascade the ports because all the players are connecting to it
        var portCascadeRange = IsHost ? 10 : 0;
        var targetPort = PeerUdp.GetPacketPort();
        for (var port = targetPort - portCascadeRange; port <= targetPort + portCascadeRange; port++)
        {
            PeerUdp.SetDestAddress(PeerUdp.GetPacketIP(), port);
            Array.Copy(GetBytes(port), 0, data, 1, 4);

            var error = PeerUdp.PutPacket(data);
            if (error != Error.Ok)
                GD.PrintErr("Error sending peer UDP packet: " + error);
        }
        
        if (_messagesSent++ >= _messagesSentRange)
        {
            _pingPeerTimer.Stop();
            GD.Print("Not received response from peer. Stopping hole punch.");
        }
    }

    private void HandleGoMessage()
    {
        _punchStep = MessageTypes.Go;
        
        var data = new[] { (byte)MessageTypes.SendHolePunched };
        var error = ServerTcp.PutData(data);
        if (error != Error.Ok)
            GD.PrintErr("Error sending server TCP packet: " + error);
        
        PeerUdp.Close();
        _pingPeerTimer.Stop();

        _punchStep = 0;
    }
    
    private enum MessageTypes : byte
    {
        SendRegister,
        SendHolePunched,
        ReceiveOwnPort,
        ReceivePeerInfo,
        Greet,
        Confirm,
        Go
    }

    private class Peer
    {
        public int Port { get; set; }
        public byte[] PrivateIp { get; set; }

        public Peer(int port, byte[] privateIp)
        {
            Port = port;
            PrivateIp = privateIp;
        }
    }

    private static byte[] Ipv4ToBytes(string ip)
    {
        var ipArray = ip.Split('.');
        var ipBytes = new byte[4];
        for (var i = 0; i < 4; i++)
            ipBytes[i] = byte.Parse(ipArray[i]);
        return ipBytes;
    }

    private static string BytesToIpv4(byte[] ip)
    {
        return $"{ip[0]}.{ip[1]}.{ip[2]}.{ip[3]}";
    }
}