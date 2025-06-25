#nullable enable
using System;
using System.Text;
using System.Threading.Tasks;
using Godot;

namespace UltraPong;

public partial class HolePuncher : Node
{
    [Signal]
    public delegate void ENetPortDiscoveredEventHandler(string hostAddress, ushort hostPort);
    
    [Signal]
    public delegate void HostPortReceivedEventHandler(ushort enetPort);
    
    [Signal]
    public delegate void SessionRegisteredEventHandler();
    
    private static readonly PacketPeerUdp ServerUdp = new();
    private static readonly PacketPeerUdp PeerUdp = new();
    
    private const string SERVER_IP = "20.206.244.22";
    private const ushort SERVER_PORT = 1910;

    private bool _isHost;
    
    private ushort _ownPort;
    private ushort _enetPort;

    // Used for sending messages
    private MessageTypes _punchStep = MessageTypes.Greet;
    
    // Used for receiving messages
    private bool _receivedGo;

    private Timer _pingPeerTimer = new();

    private const byte PORT_CASCADE_RANGE = 10;
    private const byte RESPONSE_WINDOW = 10;

    // Messages of the same type sent
    private byte _messagesSent;
    
    private Peer? _currentPeer;

    // Char used in packet protocol
    public const char RESERVED_CHAR = ':';
    
    public override void _Ready()
    {
        _pingPeerTimer.WaitTime = 0.2d;
        _pingPeerTimer.Connect("timeout", new Callable(this, nameof(PingPeer)));
        
        AddChild(_pingPeerTimer);
    }
    
    public void ConnectToServer(bool isHost, string nickname = "", string room = "")
    {
        _isHost = isHost;
        
        nickname = nickname.Replace(RESERVED_CHAR, '_');
        room = room.Replace(RESERVED_CHAR, '_');
        
        nickname = nickname == "" ? "Player" : nickname;
        room = room == "" ? $"{nickname}'s room" : room;
        
        var error = ServerUdp.ConnectToHost(SERVER_IP, SERVER_PORT);
        if (error != Error.Ok)
        {
            GD.PrintErr("Error connecting to server: " + error);
            return;
        }
        
        var roomClientBytes = Encoding.UTF8.GetBytes($"{room}{RESERVED_CHAR}{nickname}");
        
        var data = new byte[1 + roomClientBytes.Length];
        //Store isHost on MSB of the first byte and the Message Type on the other 7 bits
        data[0] = (byte)((isHost ? 0x80 : 0x00) | (byte)MessageTypes.SendRegister);
        //Store roomClient string after the first byte
        Array.Copy(roomClientBytes, 0, data, 1, roomClientBytes.Length);
        
        // Envia dados via UDP
        error = ServerUdp.PutPacket(data);
        if (error != Error.Ok)
        {
            GD.PrintErr("Error sending server packet: " + error);
            return;
        }
        
        GD.Print("Registration packet sent to server");
    }
    
    
    private static ushort FindAvailableENetPort(ushort startPort)
    {
        var testPeer = new ENetMultiplayerPeer();
        
        for (var port = (ushort)(startPort - 50); port <= startPort + 50; port++)
        {
            if (port < 1024) continue;
            
            var err = testPeer.CreateServer(port, 1);
            if (err != Error.Ok) continue;
            
            testPeer.Close();
            GD.Print($"Found available ENet port: {port}");
            return port;
        }
        
        testPeer.Close();
        return (ushort)(startPort + 100); // Fallback
    }
    
    //Process is only used for listening
    public override void _Process(double delta)
    {
        //HandleServerMessages
        if (ServerUdp.IsBound() && ServerUdp.GetAvailablePacketCount() > 0)
        {
            var error = ServerUdp.GetPacketError();
            if (error != Error.Ok)
            {
                GD.PrintErr("Error receiving server packet: " + error);
                return;
            }

            var data = ServerUdp.GetPacket();
            if (data.Length == 0) return;
            
            var dataType = (MessageTypes)data[0];

            switch (dataType)
            {
                case MessageTypes.ReceiveOwnPort:
                {
                    if (data.Length < 3) // 1 + 2
                    {
                        GD.PrintErr("Invalid ReceiveOwnPort message length");
                        return;
                    }
                    
                    _ownPort = ToUInt16BigEndian(data, 1);
                    
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
                    if (data.Length < 12) // 1 + 1 + 4 + 4 + 2
                    {
                        GD.PrintErr("Invalid ReceivePeerInfo message length");
                        break;
                    }
                    
                    _ = StartHolePunching(data);
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
            var error = PeerUdp.GetPacketError();
            if (error != Error.Ok)
                GD.PrintErr("Error receiving peer UDP packet: " + error);

            var data = PeerUdp.GetPacket();
            if (data.Length == 0)
            {
                GD.PrintErr("Received empty UDP packet");
                return;
            }
            
            var dataType = (MessageTypes)data[0];
            
            switch (dataType)
            {
                case MessageTypes.Greet or MessageTypes.Confirm:
                    _messagesSent = 0;
                    
                    if (data.Length < 3)
                    {
                        GD.PrintErr($"Invalid {dataType} message length");
                        break;
                    }
                    
                    _punchStep = dataType + 1;
                    
                    var receivedPort = ToUInt16BigEndian(data, 1);
                    if (_ownPort != receivedPort) {
                        
                        GD.Print($"Port mismatch: own={_ownPort}, received={receivedPort}");
                        
                        _ownPort = receivedPort;
                        
                        PeerUdp.Close();
                        
                        error = PeerUdp.Bind(_ownPort);
                        if (error != Error.Ok)
                            GD.PrintErr($"Error binding on port {_ownPort}: " + error);
                        else
                            GD.Print("Binding on port " + _ownPort);
                    }
                    break;
                case MessageTypes.Go:
                    if (_receivedGo) return;
                    
                    _receivedGo = true;
                    _punchStep = MessageTypes.Go;
                    
                    if (!_isHost && data.Length >= 3)
                    {
                        var hostEnetPort = ToUInt16BigEndian(data, 1);
                        var hostAddress = PeerUdp.GetPacketIP();
                        
                        GD.Print($"Received ENet port from host in Go message: {hostEnetPort} at {hostAddress}");
                        EmitSignal(SignalName.ENetPortDiscovered, hostAddress, hostEnetPort);
                        return;
                    }
                    
                    ServerUdp.SetDestAddress(SERVER_IP, SERVER_PORT);
                    
                    error = ServerUdp.PutPacket(new []{ (byte)MessageTypes.SendHolePunched });
                    if (error != Error.Ok)
                        GD.PrintErr("Error sending hole punched message to server: " + error);
                    else
                        GD.Print("Sent hole punched message to server");
                    break;
                default:
                    GD.PrintErr("Unknown peer message type: " + dataType);
                    break;
            }
        }
    }

    private async Task StartHolePunching(byte[] data)
    {
        _punchStep = MessageTypes.Greet;
        _messagesSent = 0;
        
        ServerUdp.Close();
        
        var error = PeerUdp.Bind(_ownPort);
        if (error != Error.Ok)
        {
            GD.PrintErr($"Error binding on port {_ownPort}: " + error);
            return;
        }
                    
        GD.Print("Binding on port " + _ownPort);
        
        byte offset = 1;
        // Timestamp to start hole punching
        var timestampSec = data[offset];
        offset += 1;
        
        var publicIp = new ArraySegment<byte>(data, offset, 4).ToArray();
        offset += 4;
                    
        var privateIp = new ArraySegment<byte>(data, offset, 4).ToArray();
        offset += 4;
                    
        var port = ToUInt16BigEndian(data, offset);
        offset += 2;
                    
        var nickname = Encoding.UTF8.GetString(data, offset, data.Length - offset);
                    
        GD.Print($"Received peer info: {BytesToIpv4(publicIp)}:{port}");

        _currentPeer = new Peer(BytesToIpv4(publicIp),  BytesToIpv4(privateIp),  port, nickname);
        
        // Wait for timestamp synchronization
        await ((Func<Task>)(async () =>
        {
            while (true)
            {
                var now = DateTime.Now;
                var currentSeconds = (byte)now.Second;
        
                if (currentSeconds == timestampSec)
                {
                    var millisecondsToNextSecond = 1000 - now.Millisecond;
                    if (millisecondsToNextSecond < 1000)
                        await Task.Delay(millisecondsToNextSecond);
                    break;
                }
        
                await Task.Delay(100);
            }
        }))();
        
        _pingPeerTimer.Start();
    }
    
    // Signaled through Ping Peer Timer
    private void PingPeer()
    {
        if (_currentPeer == null) return;
        
        var data = new byte[3];
        data[0] = (byte)_punchStep;
    
        var targetPort = _currentPeer.Port;
    
        if (_isHost && _punchStep == MessageTypes.Go)
            Array.Copy(GetBytesBigEndian(_enetPort), 0, data, 1, 2);
        else
            Array.Copy(GetBytesBigEndian(targetPort), 0, data, 1, 2);
        
        foreach (var ip in new[] { _currentPeer.PublicIp, _currentPeer.PrivateIp })
            for (var port = (ushort)(targetPort - PORT_CASCADE_RANGE); port <= targetPort + PORT_CASCADE_RANGE; port++)
            {
                if (port < 1024) continue;
            
                var error = PeerUdp.SetDestAddress(ip, port);
                if (error != Error.Ok)
                {
                    GD.PrintErr($"Error setting peer address to {ip}:{port}: " + error);
                    continue;
                }

                error = PeerUdp.PutPacket(data);
                if (error != Error.Ok)
                    GD.PrintErr($"Error sending peer packet to {ip}:{port}: " + error);
            }

        if (_messagesSent++ < RESPONSE_WINDOW) return;
        
        _pingPeerTimer.Stop();

        if (_receivedGo)
        {
            GD.Print("Received go and sent back, disposing Hole Puncher");
            Dispose();
        }
        else
            GD.Print($"Not received {_punchStep + 1} from peer. Stopping hole punch.");
    }


    public override void _ExitTree()
    {
        PeerUdp.Close();
        ServerUdp.Close();
        _pingPeerTimer.Stop();
    }

    private static ushort ToUInt16BigEndian(byte[] data, int startIndex)
    {
        if (startIndex + 2 > data.Length)
            throw new ArgumentOutOfRangeException(nameof(startIndex), "Not enough bytes to read UInt16");

        if (!BitConverter.IsLittleEndian) 
            return BitConverter.ToUInt16(data, startIndex);
        
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
        
        // 1 byte for message type and 2 bytes for port = 3
        ReceiveOwnPort,
        
        // 1 byte for message type, 1 byte for timestamp, 4 bytes for public IP, 4 bytes for private IP and 2 bytes for port
        ReceivePeerInfo,
        
        // 1 byte for message type, 2 bytes for port
        Greet,
        
        // 1 byte for message type, 2 bytes for port
        Confirm,
        
        // 1 byte for message type, 2 bytes for ENet port (only when sent by host)
        Go
    }

    private static byte[]? Ipv4ToBytes(string ip)
    {
        var ipArray = ip.Split('.');
        if (ipArray.Length != 4)
        {
            GD.PrintErr("Invalid IP address format");
            return null;
        }
        
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