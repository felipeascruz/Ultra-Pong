#nullable enable
using System;
using System.Text;
using System.Threading.Tasks;
using Godot;

namespace UltraPong;

public partial class HolePuncherENet : Node
{
    [Signal]
    public delegate void RoomRegisteredEventHandler();
    
    private static readonly ENetMultiplayerPeer ServerENet = new();
    private static readonly PacketPeerUdp PeerUdp = new();
    private static readonly ENetConnection PeerENetConnection = new();
    
    private const string SERVER_IP = "20.206.244.22";
    private const ushort SERVER_PORT = 3478;

    private bool _isHost;
    
    private ushort _ownPort;
    private ushort _enetPort;

    // Used for sending messages
    private MessageTypes _punchStep = MessageTypes.Greet;
    
    // Used for receiving messages
    private bool _receivedGo;

    private Timer _pingPeerTimer = new();

    private const byte ATTEMPT_RANGE = 10;
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
        
        var error = ServerENet.CreateClient(SERVER_IP, SERVER_PORT);
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
        
        error = ServerENet.PutPacket(data);
        if (error != Error.Ok)
        {
            GD.PrintErr("Error sending server packet: " + error);
            return;
        }
        
        GD.Print("Registration packet sent to server");
    }
    
    //Process is only used for listening
    public override void _Process(double delta)
    {
        //HandleServerMessages
        if (ServerENet.GetConnectionStatus() == MultiplayerPeer.ConnectionStatus.Connected && 
            ServerENet.GetAvailablePacketCount() > 0)
        {
            var error = ServerENet.GetPacketError();
            if (error != Error.Ok)
            {
                GD.PrintErr("Error receiving server packet: " + error);
                return;
            }

            var data = ServerENet.GetPacket();
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
                    
                    GD.Print("Received own port");
                    
                    _ownPort = ToUInt16BigEndian(data, 1);
                    if (_isHost)
                        EmitSignal(SignalName.RoomRegistered);
                    
                    break;
                }
                case MessageTypes.ReceivePeerInfo:
                {
                    if (data.Length < 12) // 1 + 1 + 4 + 4 + 2
                    {
                        GD.PrintErr("Invalid ReceivePeerInfo message length");
                        break;
                    }
                    
                    GD.Print("Received peer info at " + DateTime.Now.ToString("HH:mm:ss.fff"));
                    
                    _ = StartHolePunching(data);
                    break;
                }
                default:
                    GD.PrintErr("Unknown server message type: " + dataType);
                    break;
            }
        }
        // Handle peer messages
        if (PeerUdp.IsBound() && PeerUdp.GetAvailablePacketCount() > 0)
        {
            var error = PeerUdp.GetPacketError();
            if (error != Error.Ok)
            {
                GD.PrintErr("Error receiving peer packet: " + error);
                return;
            }
            
            var data = PeerUdp.GetPacket();
            if (data.Length == 0) 
            {
                GD.PrintErr("Received empty packet");
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
                    if (_ownPort != receivedPort)
                    {
                        //TODO: Implement better port mismatch treatment
                        GD.Print($"Port mismatch on {dataType}: own={_ownPort}, received={receivedPort}");

                        _ownPort = receivedPort;

                        PeerUdp.Close();

                        error = PeerUdp.Bind(_ownPort);
                        if (error != Error.Ok)
                            GD.PrintErr($"Error binding on UDP port {_ownPort}: " + error);
                        else
                            GD.Print("Binding on port " + _ownPort);
                    }
                    break;
                case MessageTypes.Go:
                    if (_receivedGo) return;

                    GD.Print("Received Go, hole punching complete");

                    _receivedGo = true;
                    _punchStep = MessageTypes.Go;

                    _ = ConnectENet();
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
        
        ServerENet.Close();
        
        var error = PeerUdp.Bind(_ownPort);
        if (error != Error.Ok)
        {
            GD.PrintErr($"Error binding on UDP port {_ownPort}: " + error);
            return;
        }
        
        GD.Print("Binding on UDP port " + _ownPort);
        
        // Offset starts at 1 because of dataType
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
        
                await Task.Delay(900);
            }
        }))();
        
        GD.Print("Starting hole punching at " + DateTime.Now.ToString("HH:mm:ss.fff"));;
        
        _pingPeerTimer.Start();
    }
    
    // Signaled through Ping Peer Timer
    private void PingPeer()
    {
        if (_currentPeer == null) return;
        
        var data = new byte[3];
        data[0] = (byte)_punchStep;
    
        var targetPort = _currentPeer.Port;
        
        foreach (var ip in new[] { _currentPeer.PublicIp, _currentPeer.PrivateIp })
            for (byte attempt = 0; attempt <= ATTEMPT_RANGE; attempt++)
                for (var port = (ushort)(targetPort - PORT_CASCADE_RANGE); port <= targetPort + PORT_CASCADE_RANGE; port++)
                {
                    if (port < 1024) continue;
                
                    Array.Copy(GetBytesBigEndian(targetPort), 0, data, 1, 2);
                    
                    var error = PeerUdp.SetDestAddress(ip, port);
                    if (error != Error.Ok)
                        GD.PrintErr($"Error setting peer UDP destination address: {error}");
                    
                    error = PeerUdp.PutPacket(data);
                    if (error != Error.Ok)
                        GD.PrintErr($"Error putting packet: {error}");
                }

        if (_messagesSent++ <= RESPONSE_WINDOW) return;
        
        _pingPeerTimer.Stop();

        GD.Print(_receivedGo
            ? "Received go and sent back, stopping ping"
            : $"Not received {_punchStep} from peer. Stopping ping.");
    }
    
    private async Task ConnectENet()
    {
        // Wait for ping to stop
        await ((Func<Task>)(async () =>
        {
            while (!_pingPeerTimer.IsStopped())
                await Task.Delay((int)(_pingPeerTimer.WaitTime * 1000));
        }))();

        PeerUdp.Close();

        if (_currentPeer == null)
        {
            GD.PrintErr("Null current peer");
            return;
        }

        var error = PeerENetConnection.CreateHostBound("*", _ownPort, 1);
        if (error != Error.Ok)
        {
            GD.PrintErr($"Error creating ENet Host bound on port {_ownPort}: " + error);
            return;
        }

        ENetPacketPeer? enetPeer = null;
        if (!_isHost)
        {
            await Task.Delay(1000);
            PeerENetConnection.ConnectToHost(_currentPeer.PublicIp, _currentPeer.Port);
        }

        var timeout = DateTime.Now.AddSeconds(3);
        var connected = false;
        while (DateTime.Now < timeout || connected)
        {
            var service = PeerENetConnection.Service(100);
            var eventType = (ENetConnection.EventType)(long)service[0];
            
            switch (eventType)
            {
                case ENetConnection.EventType.Connect:
                    connected = true;
                    break;
                case ENetConnection.EventType.Disconnect:
                    GD.PrintErr("ENet disconnected");
                    return;
                case ENetConnection.EventType.Error:
                    GD.PrintErr("ENet error");
                    break;
                case ENetConnection.EventType.None:
                case ENetConnection.EventType.Receive:
                    break;
                default:
                    throw new ArgumentOutOfRangeException();
            }
            
            await Task.Delay(50);
        }

        if (PeerENetConnection.GetPeers().Count == 0 || PeerENetConnection.GetPeers()[0].GetState() != ENetPacketPeer.PeerState.Connected)
        {
            GD.PrintErr("No peer connected to ENet");
            return;
        }

        GD.Print("ENet connection established successfully");
    
        GetTree().GetRoot().GetNode<ENetManager>("ENetManager").ConnectToPeer(PeerENetConnection);

        ServerENet.CreateClient(SERVER_IP, SERVER_PORT);

        error = ServerENet.PutPacket([(byte)MessageTypes.SendHolePunched]);
        if (error != Error.Ok)
            GD.PrintErr("Error sending hole punched message to server: " + error);
        else
            GD.Print("Sent hole punched message to server");
    }

    public override void _ExitTree()
    {
        PeerUdp.Close();
        ServerENet.Close();
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
}