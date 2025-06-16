#nullable enable
using System;
using System.Collections.Generic;
using System.Text;
using static System.BitConverter;
using Godot;

namespace UltraPong;

public partial class HolePuncher : Node
{
    private const string SERVER_IP = "20.206.244.22";
    private const int SERVER_PORT = 1910;

    private static readonly StreamPeerTcp ServerTcp = new();
    private static readonly PacketPeerUdp PeerUdp = new();
    
    private int _ownPort;
    
    private const int MAX_PEERS = 4;
    private readonly Dictionary<byte[], Peer> _peers = new(MAX_PEERS);

    private MessageTypes _punchStep;

    private Timer _pingPeerTimer = new();

    private byte _messagesSent;

    public override void _Ready()
    {
        SetProcess(false);
        
        _pingPeerTimer.WaitTime = 0.01D;
        _pingPeerTimer.Connect("timeout", new Callable(this, nameof(PingPeer)));
        _pingPeerTimer.SetName("Ping Peer Timer");
        
        AddChild(_pingPeerTimer, true);
    }
    
    //Process is only used for listening
    public override void _Process(double delta)
    {
        
        //HandleServerMessages
        if (ServerTcp.GetStatus() is StreamPeerTcp.Status.Connected && ServerTcp.GetAvailableBytes() > 0)
        {
            //Method GetData returns a Godot.Array with the error code and the data
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
                byte[] publicIp = new ArraySegment<byte>(data, 1, 4).ToArray();
                byte[] privateIp = new ArraySegment<byte>(data, 5, 4).ToArray();
                int port = ToInt32(data, 9);

                _peers.Add(publicIp, new Peer(port, privateIp));
                PeerUdp.SetDestAddress(BytesToIp(publicIp), port);
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
                _peers[IpToBytes(PeerUdp.GetPacketIP())].Port = PeerUdp.GetPacketPort();
                _punchStep = dataType + 1;
                
                error = PeerUdp.Bind(_ownPort);
                if (error != Error.Ok)
                    GD.PrintErr($"Error binding on port {_ownPort}: " + error);

                _messagesSent = 0;
            }
            else if (dataType is MessageTypes.Go)
                PeerUdp.Close();
            else
                GD.PrintErr("Unknown peer message type: " + dataType);
        }
    }

    //Signaled through Ping Peer Timer
    private void PingPeer()
    {
        var data = new byte[5];
        data[0] = (byte)_punchStep;
        Array.Copy(GetBytes(PeerUdp.GetPacketPort()),0,data, 1, 4);
        
        var error = PeerUdp.PutPacket(data);
        if (error != Error.Ok)
            GD.PrintErr("Error sending peer UDP packet: " + error);

        _messagesSent++;
    }

    public void ConnectToServer(bool isHost, string? room, string? nickname)
    {
        var error = ServerTcp.ConnectToHost(SERVER_IP, SERVER_PORT);
        ServerTcp.SetNoDelay(true);
        if (error != Error.Ok)
        {
            GD.PrintErr("Error connecting to server: " + error);
            return;
        }

        var roomClient = $"{room}:{nickname}";
        var roomClientBytes = Encoding.ASCII.GetBytes(roomClient);
        
        var data = new byte[1 + roomClientBytes.Length];
        //Store isHost on LSB of the first byte and the Message Type on the other 7 bits
        data[0] = (byte)((byte)MessageTypes.SendRegister << 1 | (isHost ? 1 : 0));
        //Store roomClient string after the first byte
        Array.Copy(roomClientBytes, 0, data, 1, roomClientBytes.Length);
        
        error = ServerTcp.PutData(data);
        
        if (error != Error.Ok)
            GD.PrintErr("Error sending server TCP packet: " + error);
        else
            SetProcess(true);
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

    private static byte[] IpToBytes(string ip)
    {
        var ipArray = ip.Split('.');
        var ipBytes = new byte[4];
        for (var i = 0; i < 4; i++)
            ipBytes[i] = byte.Parse(ipArray[i]);
        return ipBytes;
    }

    private static string BytesToIp(byte[] ip)
    {
        return $"{ip[0]}.{ip[1]}.{ip[2]}.{ip[3]}";
    }
}