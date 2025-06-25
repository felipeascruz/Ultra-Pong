#nullable enable
using System;
using System.Diagnostics;
using System.Text;
using System.Threading.Tasks;
using Godot;

namespace UltraPong;

public partial class HolePuncher : Node
{
    [Signal]
    public delegate void ENetPortDiscoveredEventHandler(ushort enetPort, string hostsAddress);
    
    [Signal]
    public delegate void HostPortReceivedEventHandler(ushort ownPort, ushort enetPort);
    
    [Signal]
    public delegate void SessionRegisteredEventHandler();
    
    private static readonly StreamPeerTcp ServerTcp = new();
    private static readonly PacketPeerUdp PeerUdp = new();
    
    private const string SERVER_IP = "20.206.244.22";
    private const ushort SERVER_PORT = 1910;

    private bool _isHost;
    
    private ushort _ownPort;
    private ushort _enetPort;

    private MessageTypes _punchStep = MessageTypes.Greet;

    private Timer _pingPeerTimer = new();

    private const byte PORT_CASCADE_RANGE = 10;
    private const byte RESPONSE_WINDOW = 5;

    private byte _messagesSent;
    
    private Peer? _currentPeer;
    
    public override void _Ready()
    {
        _pingPeerTimer.WaitTime = 0.1d;
        _pingPeerTimer.Connect("timeout", new Callable(this, nameof(PingPeer)));
        
        AddChild(_pingPeerTimer);
    }
    
    public async void ConnectToServer(bool isHost, string? room = null, string? nickname = null)
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
        
        var roomClientBytes = Encoding.UTF8.GetBytes($"{room}:{nickname}");
        
        var data = new byte[1 + roomClientBytes.Length];
        //Store isHost on MSB of the first byte and the Message Type on the other 7 bits
        data[0] = (byte)((isHost ? 0x80 : 0x00) | (byte)MessageTypes.SendRegister);
        //Store roomClient string after the first byte
        Array.Copy(roomClientBytes, 0, data, 1, roomClientBytes.Length);
        
        await WaitServerConnection();
        
        error = ServerTcp.PutData(data);
        if (error != Error.Ok)
            GD.PrintErr("Error sending server TCP packet: " + error);
    }

    private static async Task WaitServerConnection()
    {
        while (ServerTcp.GetStatus() is not StreamPeerTcp.Status.Connected)
        {
            if (ServerTcp.GetStatus() is StreamPeerTcp.Status.Error)
                throw new Exception("Server connection failed");
            await Task.Delay(50);
        }
        
        await Task.Delay(100);
        
        GD.Print("Server connection established!");
    }
    
    private static ushort FindAvailableENetPort(ushort startPort)
    {
        var testPeer = new ENetMultiplayerPeer();
        
        for (int port = startPort - 50; port <= startPort + 50 && port <= 65535; port++)
        {
            if (port < 1024) continue;
            
            var err = testPeer.CreateServer(port, 1);
            if (err != Error.Ok) continue;
            
            testPeer.Close();
            GD.Print($"Found available ENet port: {port}");
            return (ushort)port;
        }
        
        testPeer.Close();
        return (ushort)(startPort + 100); // Fallback
    }
    
    //Process is only used for listening
    public override void _Process(double delta)
    {
        ServerTcp.Poll();
        
        //HandleServerMessages
        if (ServerTcp.GetStatus() == StreamPeerTcp.Status.Connected && ServerTcp.GetAvailableBytes() > 0)
        {
            var dataArray = ServerTcp.GetData(ServerTcp.GetAvailableBytes());

            var error = dataArray[0].As<Error>();
            if (error != Error.Ok)
                GD.PrintErr("Error receiving server TCP packet: " + error);

            var data = dataArray[1].As<byte[]>();
            if (data.Length == 0) return;
            
            var dataType = (MessageTypes)data[0];

            switch (dataType)
            {
                case MessageTypes.ReceiveOwnPort:
                {
                    if (data.Length < 3) // 1 + 2
                    {
                        GD.PrintErr("Invalid ReceiveOwnPort message length");
                        break;
                    }
                    
                    _ownPort = ToUInt16BigEndian(data, 1);
                    error = PeerUdp.Bind(_ownPort);
                    if (error != Error.Ok)
                        GD.PrintErr($"Error binding on port {_ownPort}: " + error);
                    else
                        GD.Print("Binding on port " + _ownPort);;
                
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
                    if (data.Length < 11) // 1 + 4 + 4 + 2
                    {
                        GD.PrintErr("Invalid ReceivePeerInfo message length");
                        break;
                    }

                    byte offset = 1;
                    var publicIp = new ArraySegment<byte>(data, offset, 4).ToArray();
                    offset += 4;
                    
                    var privateIp = new ArraySegment<byte>(data, offset, 4).ToArray();
                    offset += 4;
                    
                    var port = ToUInt16BigEndian(data, offset);
                    offset += 2;
                    
                    var nickname = Encoding.UTF8.GetString(data, offset, data.Length - offset);
                    
                    GD.Print($"Received peer info: {BytesToIpv4(publicIp)}:{port}");

                    _currentPeer = new Peer(BytesToIpv4(publicIp),  BytesToIpv4(privateIp),  port, nickname);
                    _pingPeerTimer.Start();
                    break;
                }
                default:
                    GD.PrintErr("Unknown server message type: " + dataType);
                    break;
            }
        }

        //HandlePeerMessages
        if (PeerUdp.IsBound() && PeerUdp.GetAvailablePacketCount() > 0)
        {
            GD.Print("Received message from peer");
            
            var error = PeerUdp.GetPacketError();
            if (error != Error.Ok)
                GD.PrintErr("Error receiving peer UDP packet: " + error);

            var data = PeerUdp.GetPacket();
            if (data.Length == 0)
            {
                GD.PrintErr("Empty UDP packet received");
                return;
            }
            
            var dataType = (MessageTypes)data[0];

            switch (dataType)
            {
                case MessageTypes.Greet or MessageTypes.Confirm:
                    if (data.Length < 3)
                    {
                        GD.PrintErr($"Invalid {dataType} message length");
                        break;
                    }
                    
                    _ownPort = ToUInt16BigEndian(data, 1);
                    _punchStep = dataType + 1;
                    
                    if (PeerUdp.IsBound()) PeerUdp.Close();
                    
                    error = PeerUdp.Bind(_ownPort);
                    if (error != Error.Ok)
                        GD.PrintErr($"Error binding on port {_ownPort}: " + error);
                    else
                        GD.Print("Binding on port " + _ownPort);

                    _messagesSent = 0;
                    break;
                case MessageTypes.Go:
                    _pingPeerTimer.Stop();
                    if (!_isHost && data.Length >= 3)
                    {
                        var hostEnetPort = ToUInt16BigEndian(data, 1);
                        var hostAddress = PeerUdp.GetPacketIP();
                        
                        GD.Print($"Received ENet port from host in Go message: {hostEnetPort} at {hostAddress}");
                        EmitSignal(SignalName.ENetPortDiscovered, hostAddress, hostEnetPort);
                        return;
                    }
        
                    var responseData = new[] { (byte)MessageTypes.SendHolePunched };
                    error = ServerTcp.PutData(responseData);
                    if (error != Error.Ok)
                        GD.PrintErr("Error sending server TCP packet: " + error);
                    _punchStep = MessageTypes.Greet;
                    break;
                default:
                    GD.PrintErr("Unknown peer message type: " + dataType);
                    break;
            }
        }
    }
    
    private void PingPeer()
    {
        if (_currentPeer == null) return;
        
        var data = new byte[3];
        data[0] = (byte)_punchStep;
    
        var targetPort = _currentPeer.Port;
    
        if (_isHost && _punchStep == MessageTypes.Go)
        {
            Array.Copy(GetBytesBigEndian(_enetPort), 0, data, 1, 2);
            GD.Print($"Host sending Go message with ENet port: {_enetPort}");
        }
        else
            Array.Copy(GetBytesBigEndian(targetPort), 0, data, 1, 2);
    
        foreach (var ip in new[] { _currentPeer.PublicIp, _currentPeer.PrivateIp })
        {
            for (var port = targetPort - PORT_CASCADE_RANGE; port <= targetPort + PORT_CASCADE_RANGE; port++)
            {
                if (port is < 1024 or > 65535) continue;
            
                PeerUdp.SetDestAddress(ip, port);
            
                GD.Print($"Sending ping to peer on {ip}:{port} (step: {_punchStep})");

                var error = PeerUdp.PutPacket(data);
                if (error != Error.Ok)
                    GD.PrintErr($"Error sending peer UDP packet to {ip}:{port}: " + error);
            }
        }
    
        if (_messagesSent++ >= RESPONSE_WINDOW)
        {
            _pingPeerTimer.Stop();
            GD.Print("Not received response from peer. Stopping hole punch.");
        }
    }


    public override void _ExitTree()
    {
        PeerUdp.Close();
        ServerTcp.DisconnectFromHost();
        _pingPeerTimer.Stop();
    }

    private static ushort ToUInt16BigEndian(byte[] data, int startIndex)
    {
        if (startIndex + 2 > data.Length)
            throw new ArgumentOutOfRangeException(nameof(startIndex), "Not enough bytes to read UInt16");

        if (!BitConverter.IsLittleEndian) return BitConverter.ToUInt16(data, startIndex);
        
        var bytes = new byte[2];
        Array.Copy(data, startIndex, bytes, 0, 2);
        Array.Reverse(bytes);
        return BitConverter.ToUInt16(bytes, 0);
    }

    private static byte[] GetBytesBigEndian(ushort value)
    {
        var bytes = BitConverter.GetBytes(value);
        if (BitConverter.IsLittleEndian)
            Array.Reverse(bytes);
        return bytes;
    }
    
    private enum MessageTypes : byte
    {
        // 1 bit for IsHost, 7 bits for message type and other bytes for room:client string
        SendRegister,
        
        // 1 byte for message type
        SendHolePunched,
        
        // 1 byte for message type and 2 bytes for port
        ReceiveOwnPort,
        
        // 1 byte for message type, 4 bytes for public IP, 4 bytes for private IP and 2 bytes for port
        ReceivePeerInfo,
        
        // 1 byte for message type, 2 bytes for port
        Greet,
        
        // 1 byte for message type, 2 bytes for port
        Confirm,
        
        // 1 byte for message type, 2 bytes for ENet port (only when sent by host)
        Go
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

    private class Peer
    {
        public string PublicIp { get; set; }
        public string PrivateIp { get; set; }
        public ushort Port { get; set; }
        public string Nickname { get; set; }
        
        public Peer(string publicIp, string privateIp, ushort port, string nickname)
        {
            PublicIp = publicIp;
            PrivateIp = privateIp;
            Port = port;
            Nickname = nickname;
        }
    }
}