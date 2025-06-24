#nullable enable
using System;
using System.Text;
using static System.BitConverter;
using Godot;

namespace UltraPong;

public partial class HolePuncher : Node
{
    [Signal]
    public delegate void HolePunchedEventHandler(int myPort);
    
    [Signal]
    public delegate void ENetPortDiscoveredEventHandler(int enetPort, string hostsAddress);
    
    [Signal]
    public delegate void HostPortReceivedEventHandler(int ownPort, int enetPort);
    
    [Signal]
    public delegate void SessionRegisteredEventHandler();
    
    private const string SERVER_IP = "20.206.244.22";
    private const int SERVER_PORT = 1910;

    private bool _isHost;

    private static readonly StreamPeerTcp ServerTcp = new();
    private static readonly PacketPeerUdp PeerUdp = new();
    
    private int _ownPort;
    private int _enetPort;

    private MessageTypes _punchStep;

    private Timer _pingPeerTimer = new();
    private Timer _portDiscoveryTimer = new();

    private const byte PORT_CASCADE_RANGE = 10;
    private const byte RESPONSE_WINDOW = 5;

    private byte _messagesSent;
    private bool _enetPortNegotiated;

    public override void _Ready()
    {
        _pingPeerTimer.WaitTime = 0.1d;
        _pingPeerTimer.Connect("timeout", new Callable(this, nameof(PingPeer)));
        
        _portDiscoveryTimer.WaitTime = 0.2d;
        _portDiscoveryTimer.Connect("timeout", new Callable(this, nameof(DiscoverENetPort)));
        
        AddChild(_pingPeerTimer);
        AddChild(_portDiscoveryTimer);
    }
    
    public void ConnectToServer(bool isHost, string? room = null, string? nickname = null)
    {
        _isHost = isHost;
        nickname = nickname is "" or null ? "Player" : nickname;
        room = room is "" or null ? $"{nickname}'s room" : room;
        
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
    
    private static int FindAvailableENetPort(int startPort)
    {
        var testPeer = new ENetMultiplayerPeer();
        
        for (int port = startPort - 50; port <= 50; port++)
        {
            if (port > 65535) break;
            
            var err = testPeer.CreateServer(port, 1);
            if (err == Error.Ok)
            {
                testPeer.Close();
                GD.Print($"Found available ENet port: {port}");
                return port;
            }
        }
        
        testPeer.Close();
        return startPort + 100; // Fallback
    }
    
    //Process is only used for listening
    public override void _Process(double delta)
    {
        ServerTcp.Poll();
        
        //HandleServerMessages
        if (ServerTcp.GetAvailableBytes() > 0)
        {
            var dataArray = ServerTcp.GetData(ServerTcp.GetAvailableBytes());

            var error = dataArray[0].As<Error>();
            if (error != Error.Ok)
                GD.PrintErr("Error receiving server TCP packet: " + error);

            var data = dataArray[1].As<byte[]>();
            var dataType = (MessageTypes)data[0];

            switch (dataType)
            {
                case MessageTypes.ReceiveOwnPort:
                {
                    _ownPort = ToInt32(data, 1);
                    error = PeerUdp.Bind(_ownPort);
                    if (error != Error.Ok)
                        GD.PrintErr($"Error binding on port {_ownPort}: " + error);
                
                    if (_isHost)
                    {
                        _enetPort = FindAvailableENetPort(_ownPort);
                        GD.Print($"Host received port {_ownPort} and will use ENet port: {_enetPort}");
                        
                        EmitSignal(SignalName.HostPortReceived, _enetPort);
                    }

                    break;
                }
                case MessageTypes.ReceivePeerInfo:
                {
                    var publicIp = new ArraySegment<byte>(data, 1, 4).ToArray();
                    var privateIp = new ArraySegment<byte>(data, 5, 4).ToArray();
                    var port = ToInt32(data, 9);
                    
                    PeerUdp.SetDestAddress(BytesToIpv4(publicIp), port);
                    _pingPeerTimer.Start();
                    break;
                }
                default:
                    GD.PrintErr("Unknown server message type: " + dataType);
                    break;
            }
        }

        //HandlePeerMessages
        if (PeerUdp.GetAvailablePacketCount() > 0)
        {
            var error = PeerUdp.GetPacketError();
            if (error != Error.Ok)
                GD.PrintErr("Error receiving peer UDP packet: " + error);

            var data = PeerUdp.GetPacket();
            var dataType = (MessageTypes)data[0];

            switch (dataType)
            {
                case MessageTypes.Greet or MessageTypes.Confirm:
                {
                    _ownPort = ToInt32(data, 1);
                    _punchStep = dataType + 1;

                    if (!_isHost)
                    {
                        error = PeerUdp.Bind(_ownPort);
                        if (error != Error.Ok)
                            GD.PrintErr($"Error binding on port {_ownPort}: " + error);
                    }

                    _messagesSent = 0;
                    break;
                }
                case MessageTypes.Go:
                    HandleGoMessage();
                    break;
                case MessageTypes.ENetPortInfo:
                    HandleENetPortInfo(data);
                    break;
                case MessageTypes.RequestENetPort:
                    HandleENetPortRequest();
                    break;
                default:
                    GD.PrintErr("Unknown peer message type: " + dataType);
                    break;
            }
        }
    }
    
    private void PingPeer()
    {
        var data = new byte[5];
        data[0] = (byte)_punchStep;
        
        var targetPort = PeerUdp.GetPacketPort();
        for (var port = targetPort - PORT_CASCADE_RANGE; port <= targetPort + PORT_CASCADE_RANGE; port++)
        {
            PeerUdp.SetDestAddress(PeerUdp.GetPacketIP(), port);
            Array.Copy(GetBytes(port), 0, data, 1, 4);

            var error = PeerUdp.PutPacket(data);
            if (error != Error.Ok)
                GD.PrintErr("Error sending peer UDP packet: " + error);
        }
        
        if (_messagesSent++ >= RESPONSE_WINDOW)
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
        
        _punchStep = 0;
        _pingPeerTimer.Stop();
        
        if (_isHost)
            _portDiscoveryTimer.Start();
        else
            RequestENetPort();
        
        EmitSignal(SignalName.HolePunched, _ownPort);
    }
    
    private void DiscoverENetPort()
    {
        if (!_isHost || _enetPortNegotiated) 
        {
            _portDiscoveryTimer.Stop();
            return;
        }
        
        var data = new byte[5];
        data[0] = (byte)MessageTypes.ENetPortInfo;
        Array.Copy(GetBytes(_enetPort), 0, data, 1, 4);
        
        var error = PeerUdp.PutPacket(data);
        if (error != Error.Ok)
            GD.PrintErr("Error sending ENet port info: " + error);
        else
            GD.Print($"Sent ENet port info: {_enetPort}");
    }
    
    private void RequestENetPort()
    {
        var data = new[] { (byte)MessageTypes.RequestENetPort };
        var error = PeerUdp.PutPacket(data);
        if (error != Error.Ok)
            GD.PrintErr("Error requesting ENet port: " + error);
        else
            GD.Print("Requested ENet port from host");
    }
    
    private void HandleENetPortRequest()
    {
        if (!_isHost) return;
        
        var data = new byte[5];
        data[0] = (byte)MessageTypes.ENetPortInfo;
        Array.Copy(GetBytes(_enetPort), 0, data, 1, 4);
        
        var error = PeerUdp.PutPacket(data);
        if (error != Error.Ok)
            GD.PrintErr("Error sending ENet port response: " + error);
        else
            GD.Print($"Responded with ENet port: {_enetPort}");
    }
    
    private void HandleENetPortInfo(byte[] data)
    {
        if (_isHost) return;
        
        var enetPort = ToInt32(data, 1);
        var hostAddress = PeerUdp.GetPacketIP();
        
        GD.Print($"Received ENet port from host: {enetPort} at {hostAddress}");
        
        _enetPortNegotiated = true;
        EmitSignal(SignalName.ENetPortDiscovered, enetPort, hostAddress);
    }
    
    public int GetENetPort() => _enetPort;

    public override void _ExitTree()
    {
        PeerUdp.Close();
        ServerTcp.DisconnectFromHost();
        _pingPeerTimer.Stop();
        _portDiscoveryTimer.Stop();
    }
    
    private enum MessageTypes : byte
    {
        SendRegister,
        SendHolePunched,
        ReceiveOwnPort,
        ReceivePeerInfo,
        Greet,
        Confirm,
        Go,
        ENetPortInfo,
        RequestENetPort
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