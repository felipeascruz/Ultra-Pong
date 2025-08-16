#nullable enable
using System;
using System.Text;
using System.Threading.Tasks;
using Godot;

namespace UltraPong;

public partial class ENetManager : Node
{
    // Important concepts:
    // Peer: Player
    // Server: The STUN/TURN public server
    // Host: The peer that is authoritative over the game state
    // Client: All non-host peers
    
    // TODO: implement private IP tries before hole punching
    
    [Signal]
    public delegate void RoomRegisteredEventHandler();

    private static Error _error = Error.Ok;

    private static ENetPacketPeer? _serverENetPacketPeer;
    private static readonly ENetConnection ServerENetConnection = new();
    private static readonly PacketPeerUdp PeerUdp = new();
    private static readonly ENetConnection PeerENetConnection = new();
    private static readonly ENetMultiplayerPeer LocalENetPeer = new();
    
    // This bool is not ideal, but the only way I could manage the _Process
    private bool _isServerConnected;
    
    private static readonly string SERVER_IP = "20.206.244.22";
    private const ushort SERVER_PORT = 3478;

    private bool _isHost;
    
    private ushort _ownPort;

    private int? _ownENetId;

    // Used for sending messages
    private MessageTypes _punchStep = MessageTypes.Greet;
    
    // Used for receiving messages
    private bool _receivedGo;

    private Timer _pingPeerTimer = new();

    private const byte ATTEMPT_RANGE = 20;
    private const byte PORT_CASCADE_RANGE = 10;
    private const byte RESPONSE_WINDOW = 10;

    // Messages of the same type sent
    private byte _messagesSent;
    
    private Peer? _currentPeer;

    // Char used in packet protocol
    public const char RESERVED_CHAR = ':';
    
    public override void _Ready()
    {
        if (_isHost) _ownENetId = 1;
        
        _pingPeerTimer.WaitTime = 0.2d;
        _pingPeerTimer.Connect("timeout", new Callable(this, nameof(PingPeer)));
        
        AddChild(_pingPeerTimer);
    }
    
    public async Task ConnectToIceServer()
    {
        _error = ServerENetConnection.CreateHostBound("*", 0, 1);
        if (_error != Error.Ok)
        {
            GD.PrintErr("Error creating Server ENet Host" + _error);
            return;
        }
        _ownPort = (ushort)ServerENetConnection.GetLocalPort();
        
        _serverENetPacketPeer = ServerENetConnection.ConnectToHost(SERVER_IP, SERVER_PORT);

        var timeout = DateTime.Now.AddSeconds(3);
        while (DateTime.Now < timeout)
        {
            var service = ServerENetConnection.Service(100);
            if (service == null)
            {
                GD.PrintErr("Server ENet service is null");
                continue;
            }

            var eventType = (ENetConnection.EventType)(long)service[0];
            
            switch (eventType)
            {
                case ENetConnection.EventType.Connect:
                    continue;
                case ENetConnection.EventType.Disconnect:
                    GD.PrintErr("Server ENet disconnected");
                    return;
                case ENetConnection.EventType.Error:
                    GD.PrintErr("Server ENet error");
                    break;
                case ENetConnection.EventType.None:
                case ENetConnection.EventType.Receive:
                    break;
                default:
                    throw new ArgumentOutOfRangeException();
            }
            
            await Task.Delay(50);
        }

        if (_serverENetPacketPeer.GetState() != ENetPacketPeer.PeerState.Connected)
        {
            GD.PrintErr("No peer connected to ENet");
            return;
        }

        GD.Print("Server connection established successfully");
        
        _isServerConnected = true;
    }
    
    public void SendRegisterMessage(bool isHost, string nickname = "", string room = "")
    {
        if (_serverENetPacketPeer is null || _serverENetPacketPeer.GetState() != ENetPacketPeer.PeerState.Connected)
        {
            GD.PrintErr("Unable to send registration message. Not connected to server");
            return;
        }
        
        _isHost = isHost;
        
        nickname = nickname.Replace(RESERVED_CHAR, '_');
        room = room.Replace(RESERVED_CHAR, '_');
        
        nickname = nickname == "" ? "Player" : nickname;
        room = room == "" ? $"{nickname}'s room" : room;
        
        var roomClientBytes = Encoding.UTF8.GetBytes($"{room}{RESERVED_CHAR}{nickname}");
        
        var data = new byte[1 + roomClientBytes.Length];
        
        data[0] = isHost ? (byte)MessageTypes.SendHostRegister : (byte)MessageTypes.SendClientRegister;

        // Store roomClient string after first byte
        Array.Copy(roomClientBytes, 0, data, 1, roomClientBytes.Length);
        
        _serverENetPacketPeer.Send(0, data, (int)ENetPacketPeer.FlagReliable);
        
        GD.Print("Registration packet sent to server");
    }
    
    //Process is only used for listening
    public override void _Process(double delta)
    {
        //HandleServerMessages
        if (_isServerConnected)
        {
            var service = ServerENetConnection.Service(100);
            if (service == null)
            {
                GD.PrintErr("ENet service is null");
                return;
            }

            var eventType = (ENetConnection.EventType)(long)service[0];

            switch (eventType)
            {
                case ENetConnection.EventType.Connect:
                    break;
                case ENetConnection.EventType.Disconnect:
                    GD.PrintErr("Server ENet disconnected");
                    return;
                case ENetConnection.EventType.Error:
                    GD.PrintErr("Server ENet error");
                    break;
                case ENetConnection.EventType.None:
                    break;
                case ENetConnection.EventType.Receive:
                    var peer = (ENetPacketPeer)service[1];

                    _error = peer.GetPacketError();
                    if (_error != Error.Ok)
                    {
                        GD.PrintErr("Error receiving server packet: " + _error);
                        return;
                    }

                    _ = HandleReceivePeerInfo(peer.GetPacket());
                    break;
                default:
                    throw new ArgumentOutOfRangeException();
            }

            // Handle peer messages
            if (PeerUdp.IsBound() && PeerUdp.GetAvailablePacketCount() > 0)
            {
                _error = PeerUdp.GetPacketError();
                if (_error != Error.Ok)
                {
                    GD.PrintErr("Error receiving peer packet: " + _error);
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

                        if (data.Length != 3)
                        {
                            GD.PrintErr($"Invalid {dataType} message length");
                            break;
                        }

                        _punchStep = dataType + 1;

                        var receivedPort = ToUInt16BigEndian(data, 1);
                        if (_ownPort != receivedPort)
                        {
                            // TODO: Implement better port mismatch treatment
                            GD.Print($"Port mismatch on {dataType}: own={_ownPort}, received={receivedPort}");

                            _ownPort = receivedPort;

                            PeerUdp.Close();

                            _error = PeerUdp.Bind(_ownPort);
                            if (_error != Error.Ok)
                                GD.PrintErr($"Error binding on UDP port {_ownPort}: " + _error);
                            else
                                GD.Print("Binding on port " + _ownPort);
                        }

                        break;
                    case MessageTypes.Go:
                        if (_receivedGo) return;
                        
                        if (!_isHost)
                            _ownENetId = ToInt32BigEndian(data, 1);
                        
                        GD.Print("Received Go, hole punching complete");
                        
                        _receivedGo = true;
                        _punchStep = MessageTypes.Go;

                        _ = ConnectENetPeer();
                        break;
                    default:
                        GD.PrintErr("Unknown peer message type: " + dataType);
                        break;
                }
            }
        }
    }

    private async Task HandleReceivePeerInfo(byte[] data)
    {
        if (data.Length != 8) // Timestamp (2) + Public IP (4) + Port (2) = 8
        {
            GD.PrintErr("Invalid ReceivePeerInfo message length");
            return;
        }

        GD.Print("Received peer info at " + DateTime.Now.ToString("HH:mm:ss.fff"));
        
        _isServerConnected = false;
        _serverENetPacketPeer?.Reset();
        
        _punchStep = MessageTypes.Greet;
        _messagesSent = 0;
        
        _error = PeerUdp.Bind(_ownPort);
        if (_error != Error.Ok)
        {
            GD.PrintErr($"Error binding on UDP port {PeerUdp.GetLocalPort()}: " + _error);
            return;
        }
        GD.Print("Binding on UDP port " + PeerUdp.GetLocalPort());
        
        _error = ServerENetConnection.CreateHostBound("*", 0, 1);
        if (_error != Error.Ok)
        {
            GD.PrintErr("Error creating Server ENet Host" + _error);
            return;
        }
        
        _serverENetPacketPeer = ServerENetConnection.ConnectToHost(SERVER_IP, SERVER_PORT);
        _isServerConnected = true;
        
        byte offset = 1;
        
        // Timestamp to start hole punching
        var timestampMilliSec = ToUInt16BigEndian(data, offset);
        offset += 2;
        
        var publicIp = new ArraySegment<byte>(data, offset, 4).ToArray();
        offset += 4;
                    
        var port = ToUInt16BigEndian(data, offset);

        var eNetId = _isHost ? new RandomNumberGenerator().RandiRange(2, int.MaxValue) : 1;

        _currentPeer = new Peer(BytesToIpv4(publicIp),  port, eNetId);
        GD.Print("Current Peer: " + _currentPeer.PublicIp + ':' + _currentPeer.Port);
        
        
        // Wait for timestamp synchronization
        await Task.Delay(timestampMilliSec);
        
        GD.Print("Starting hole punching at " + DateTime.Now.ToString("HH:mm:ss.fff"));
        
        _pingPeerTimer.Start();
    }
    
    // Signaled through Ping Peer Timer
    private void PingPeer()
    {
        if (_currentPeer == null) return;
        
        var data = new byte[3];
        data[0] = (byte)_punchStep;

        var targetPort = _currentPeer.Port;
        
        for (byte attempt = 0; attempt <= ATTEMPT_RANGE; attempt++)
        {
            if (_punchStep == MessageTypes.Go)
            {
                if (_isHost)
                    Array.Copy(GetBytesBigEndian(_currentPeer.ENetId), 0, data, 1, 4);
                
                _error = PeerUdp.PutPacket(data);
                if (_error != Error.Ok)
                    GD.PrintErr($"Error putting Go packet: {_error}");
            }
            else 
                for (var port = (ushort)(targetPort - PORT_CASCADE_RANGE); port <= targetPort + PORT_CASCADE_RANGE; port++)
                {
                    if (port < 1024) continue;
                
                    Array.Copy(GetBytesBigEndian(targetPort), 0, data, 1, 2);
                    
                    _error = PeerUdp.SetDestAddress(_currentPeer.PublicIp, port);
                    if (_error != Error.Ok)
                        GD.PrintErr($"Error setting peer UDP destination address: {_error}");
                    
                    _error = PeerUdp.PutPacket(data);
                    if (_error != Error.Ok)
                        GD.PrintErr($"Error putting packet: {_error}");
                }
        }
            

        if (_messagesSent++ <= RESPONSE_WINDOW) return;
        
        _pingPeerTimer.Stop();

        GD.Print(_receivedGo
            ? "Received go and sent back, stopping ping"
            : $"Not received {_punchStep} from peer. Stopping ping.");
    }

    private async Task ConnectENetPeer()
    {
        // Wait for ping to stop
        while (!_pingPeerTimer.IsStopped())
            await Task.Delay((int)(_pingPeerTimer.WaitTime * 1000));

        PeerUdp.Close();

        if (LocalENetPeer.Host is null)
        {
            if (_ownENetId == null)
            {
                GD.PrintErr("Own ENet ID is null");
                return;
            }
            
            _error = LocalENetPeer.CreateMesh((int)_ownENetId);

            if (_error != Error.Ok)
                GD.PrintErr("Error creating ENet Mesh: " + _error);

            GetTree().GetMultiplayer().SetMultiplayerPeer(LocalENetPeer);
        }

        if (_currentPeer == null)
        {
            GD.PrintErr("Null current peer");
            return;
        }

        _error = PeerENetConnection.CreateHostBound("*", _ownPort, 1);
        if (_error != Error.Ok)
        {
            GD.PrintErr($"Error creating ENet Host bound on port {_ownPort}: " + _error);
            return;
        }
        
        if (!_isHost)
        {
            // TODO: implement proper connection waiting
            await Task.Delay(1000);
            PeerENetConnection.ConnectToHost(_currentPeer.PublicIp, _currentPeer.Port);
        }

        var timeout = DateTime.Now.AddSeconds(3);
        while (DateTime.Now < timeout)
        {
            var service = PeerENetConnection.Service(100);
            if (service == null)
            {
                GD.PrintErr("ENet service is null");
                continue;
            }

            var eventType = (ENetConnection.EventType)(long)service[0];
            
            switch (eventType)
            {
                case ENetConnection.EventType.Connect:
                    continue;
                case ENetConnection.EventType.Disconnect:
                    GD.PrintErr("ENet peer disconnected");
                    return;
                case ENetConnection.EventType.Error:
                    GD.PrintErr("ENet peer Error");
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

        GD.Print("ENet connection to peer established successfully");
        
        _error = LocalENetPeer.AddMeshPeer(_currentPeer.ENetId, PeerENetConnection);
        if (_error != Error.Ok)
            GD.PrintErr("Error adding ENet Mesh peer: " + _error);

        if (_serverENetPacketPeer == null)
        {
            GD.PrintErr("Server Packet Peer is null");
            return;
        }

        _error = _serverENetPacketPeer.Send(0, [(byte)MessageTypes.SendHolePunched],
                (int)ENetPacketPeer.FlagReliable);
        
        if (_error != Error.Ok)
            GD.PrintErr("Error sending hole punched message to server: " + _error);
        else
            GD.Print("Sent hole punched message to server");
    }

    public override void _ExitTree()
    {
        LocalENetPeer.Close();
        PeerUdp.Close();
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
    
    private static int ToInt32BigEndian(byte[] data, int startIndex)
    {
        if (startIndex + 4 > data.Length)
            throw new ArgumentOutOfRangeException(nameof(startIndex), "Not enough bytes to read Int32");

        if (!BitConverter.IsLittleEndian) 
            return BitConverter.ToInt32(data, startIndex);
        
        var bytes = new byte[4];
        Array.Copy(data, startIndex, bytes, 0, 4);
        Array.Reverse(bytes);
        
        return BitConverter.ToInt32(bytes, 0);
    }

    private static byte[] GetBytesBigEndian<T>(T value) where T : struct
    {
        var bytes = value switch
        {
            byte v => [v],
            short v => BitConverter.GetBytes(v),
            ushort v => BitConverter.GetBytes(v),
            int v => BitConverter.GetBytes(v),
            uint v => BitConverter.GetBytes(v),
            long v => BitConverter.GetBytes(v),
            ulong v => BitConverter.GetBytes(v),
            _ => throw new ArgumentException($"Not supported type: {typeof(T)}")
        };

        if (BitConverter.IsLittleEndian && bytes.Length > 1)
            Array.Reverse(bytes);

        return bytes;
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
    
    private enum MessageTypes : byte
    {
        ReceivePeerInfo,    // 1 byte for the message type, 4 bytes for public IP, 2 bytes for port
        SendHostRegister,   // 1 byte for the message type, other bytes for room and client strings
        SendClientRegister, // 1 byte for the message type, other bytes for room and client strings
        SendHolePunched,    // 1 byte for the message type
        Greet,              // 1 byte for the message type, 2 bytes for port
        Confirm,            // 1 byte for the message type, 2 bytes for port
        Go                  // 1 byte for the message type, 4 bytes for ENetId
    }
    
    public class Peer(string publicIp, ushort port, int eNetId)
    {
        public string PublicIp { get;} = publicIp;
        public ushort Port { get;} = port;
        public int ENetId { get;} = eNetId;
        
        public string? Nickname { get; set; }
    }
}