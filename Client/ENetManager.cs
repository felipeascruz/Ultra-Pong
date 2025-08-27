using System;
using System.Collections.Generic;
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
    private static ENetMultiplayerPeer? _localENetPeer;

    private static readonly string SERVER_IP = "20.206.244.22";
    private const ushort SERVER_PORT = 3478;

    private bool _isHost;

    private ushort? _ownPort;

    private int? _ownENetId;


    private MessageType _punchStep = MessageType.Greet;

    private bool _receivedGo;

    // Used for sending messages
    private Timer _pingPeerTimer = new();

    // Used for receiving messages
    private Timer _listenToServerTimer = new();

    private const byte ATTEMPT_RANGE = 30;
    private const byte PORT_CASCADE_RANGE = 15;
    private const byte RESPONSE_WINDOW = 20;

    // Messages of the same type sent
    private byte _messagesSent;

    private Peer? _currentPeer;

    public override void _Ready()
    {
        _pingPeerTimer.WaitTime = 0.2d;
        _pingPeerTimer.Timeout += PingPeer;
        AddChild(_pingPeerTimer);

        _listenToServerTimer.WaitTime = 0.2d;
        _listenToServerTimer.Timeout += ListenToServer;
        AddChild(_listenToServerTimer);

        // _Process is only used for handling Peer UDP packets
        CallDeferred("set_process", false);
    }

    public async Task<bool> ConnectToIceServer()
    {
        _error = ServerENetConnection.CreateHostBound("*", 0, 1);
        if (_error != Error.Ok)
            return false;

        _serverENetPacketPeer = ServerENetConnection.ConnectToHost(SERVER_IP, SERVER_PORT);

        await WaitForENetConnection(3d, ServerENetConnection);

        if (_serverENetPacketPeer.GetState() != ENetPacketPeer.PeerState.Connected)
            return false;

        GD.Print("Server connection established successfully");

        _listenToServerTimer.Start();
        
        return true;
    }

    public void SendRegisterMessage(bool isHost, string nickname = "", string room = "")
    {
        if (_serverENetPacketPeer is null || _serverENetPacketPeer.GetState() != ENetPacketPeer.PeerState.Connected)
        {
            GD.PrintErr("Unable to send registration message. Not connected to server");
            return;
        }

        _isHost = isHost;

        nickname = nickname == "" ? "Player" : nickname;
        room = room == "" ? $"{nickname}'s room" : room;
        
        if (isHost) _ownENetId = 1;

        var ips = IP.GetLocalAddresses().AsSpan();
        foreach (var ip in ips)
        {

        }

        var data = new byte[3 + ips.Length + room.Length + nickname.Length];

        data[0] = isHost ? (byte)MessageType.SendHostRegister : (byte)MessageType.SendClientRegister;



        _serverENetPacketPeer.Send(0, data, (int)ENetPacketPeer.FlagReliable);

        GD.Print("Registration packet sent to server");
        if (isHost)
            EmitSignal(nameof(RoomRegistered));
    }

    // Signaled through ListenToServerTimer
    private void ListenToServer()
    {
        var service = ServerENetConnection.Service();
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

                HandleServerMessage(peer.GetPacket());

                break;
            default:
                GD.Print("Unknown ENet event type: " + eventType);
                break;
        }

        return;

        void HandleServerMessage(byte[] data)
        {
            var dataType = (MessageType)data[0];
            switch (dataType)
            {
                case MessageType.ReceiveRegisterFail:
                    GD.Print("Server register failed");
                    break;
                case MessageType.ReceiveRegisterSuccess:
                    if (data.Length != 3)
                    {
                        GD.PrintErr("Invalid ReceiveRegisterSuccess message length: " + data.Length);
                        return;
                    }

                    GD.Print("Server register succeeded");

                    _ownPort = ToUInt16BigEndian(data, 1);
                    break;
                case MessageType.ReceivePeerInfo:
                    _ = HandleReceivePeerInfo(data);
                    break;
                case MessageType.SendHostRegister:
                case MessageType.SendClientRegister:
                case MessageType.SendHolePunched:
                case MessageType.Greet:
                case MessageType.Confirm:
                case MessageType.Go:
                    GD.Print("Invalid server message type: " + dataType);
                    break;
                default:
                    GD.Print("Unknown server message type: " + dataType);
                    break;
            }
        }
    }

    private async Task HandleReceivePeerInfo(byte[] data)
    {
        var startTime = DateTime.Now;

        byte dataIndex = 1; // Starts at 1 because first byte is message type

        var ipsLength = data[1];
        dataIndex++;
        
        if (data.Length != 4 + (4 * ipsLength)) // Message type (1) + Ips Length (1) + Ips (4 X Ips Length) + Port (2) + Timestamp (2)
        {
            GD.PrintErr("Invalid ReceivePeerInfo message length");
            return;
        }

        GD.Print("Received peer info at " + DateTime.Now.ToString("HH:mm:ss.fff"));

        _listenToServerTimer.Stop();
        _serverENetPacketPeer?.Reset();
        ServerENetConnection.Destroy();

        GC.Collect();
        GC.WaitForPendingFinalizers();

        _punchStep = MessageType.Greet;
        _messagesSent = 0;

        if (_ownPort is null)
        {
            GD.PrintErr("Invalid own port");
            return;
        }

        _error = PeerUdp.Bind((ushort)_ownPort);
        if (_error != Error.Ok)
        {
            GD.PrintErr($"Error binding on UDP port {_ownPort}: " + _error);
            return;
        }

        GD.Print("Binding on UDP port " + _ownPort);

        _error = ServerENetConnection.CreateHostBound("*", 0, 1);
        if (_error != Error.Ok)
        {
            GD.PrintErr("Error creating Server ENet Host" + _error);
            return;
        }

        _serverENetPacketPeer = ServerENetConnection.ConnectToHost(SERVER_IP, SERVER_PORT);
        _listenToServerTimer.Start();

        var ips = new string[ipsLength];
        for (byte i = 0; i < ipsLength; i++)
        {
            ips[i] = BytesToIpv4(new ArraySegment<byte>(data, dataIndex, 4).ToArray());
            dataIndex += 4;
        }

        var port = ToUInt16BigEndian(data, dataIndex);
        dataIndex += 2;

        var peerENetId = _isHost ? new RandomNumberGenerator().RandiRange(2, int.MaxValue) : 1;

        _currentPeer = new Peer(ips, port, peerENetId);
        GD.Print("Current Peer: " + _currentPeer.MainIp + ':' + port);

        // SyncTime before starting hole punching
        var syncTimeMilliSec = ToUInt16BigEndian(data, dataIndex);

        var elapsedMs = (DateTime.Now - startTime).TotalMilliseconds;
        syncTimeMilliSec = (ushort)Math.Max(0d, syncTimeMilliSec - elapsedMs);

        // Wait for timestamp synchronization
        await Task.Delay(syncTimeMilliSec);

        _pingPeerTimer.Start();
        SetProcess(true);
        
        GD.Print("Starting hole punching at " + DateTime.Now.ToString("HH:mm:ss.fff"));
        GD.Print("Sync Time in ms: " + syncTimeMilliSec);
    }

    // _Process is used exclusively for handling Peer UDP packets
    public override void _Process(double delta)
    {
        if (!PeerUdp.IsBound() || PeerUdp.GetAvailablePacketCount() <= 0) return;

        _error = PeerUdp.GetPacketError();
        if (_error != Error.Ok)
        {
            GD.PrintErr("Error receiving peer packet: " + _error);
            return;
        }

        var data = PeerUdp.GetPacket();
        
        if (data.Length == 0 || PeerUdp.GetPacketIP() == SERVER_IP)
            return;

        var dataType = (MessageType)data[0];
        switch (dataType)
        {
            case MessageType.Greet or MessageType.Confirm:
                _messagesSent = 0;

                if (data.Length != 3)
                {
                    GD.PrintErr($"Invalid {dataType} message length");
                    return;
                }
                if (dataType >= _punchStep)
                    _punchStep = dataType + 1;

                if (_currentPeer?.MainIp is not null)
                    _currentPeer.MainIp = PeerUdp.GetPacketIP();

                var receivedPort = ToUInt16BigEndian(data, 1);
                if (_ownPort != receivedPort)
                {
                    // TODO: Implement better port mismatch treatment
                    GD.Print($"Port mismatch on {dataType}: own={_ownPort}, received={receivedPort}");

                    _ownPort = receivedPort;

                    PeerUdp.Close();

                    _error = PeerUdp.Bind((ushort)_ownPort);
                    if (_error != Error.Ok)
                        GD.PrintErr($"Error binding on UDP port {_ownPort}: " + _error);
                    else
                        GD.Print("Binding on port " + _ownPort);
                }
                break;
            case MessageType.Go:
                if (_receivedGo) return;

                if (!_isHost)
                    _ownENetId = ToInt32BigEndian(data, 1);

                GD.Print("Received Go, hole punching complete");

                _receivedGo = true;
                _punchStep = MessageType.Go;

                _ = ConnectENetPeer();
                break;
            default:
                GD.PrintErr("Unknown peer message type: " + dataType);
                break;
        }
    }

    // Signaled through Ping Peer Timer
    private void PingPeer()
    {
        if (_currentPeer is null) return;

        var data = _punchStep == MessageType.Go ? new byte[5] : new byte[3];
        
        for (byte attempt = 0; attempt <= ATTEMPT_RANGE; attempt++)
            foreach (var port in _currentPeer.PortRange)
                foreach (var ip in _currentPeer.Ips)
                {
                    data[0] = (byte)_punchStep;
                    if (_punchStep == MessageType.Go && _isHost)
                        Array.Copy(_currentPeer.ENetIdBytes, 0, data, 1, 4);
                    else
                        Array.Copy(_currentPeer.MainPortBytes, 0, data, 1, 2);

                    _error = PeerUdp.SetDestAddress(ip, port);
                    if (_error != Error.Ok)
                        continue;
                    PeerUdp.PutPacket(data);
                }


        if (_messagesSent++ <= RESPONSE_WINDOW)
            return;

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
        // _Process is only used for receiving packets from the peer
        CallDeferred("set_process", false);

        if (_localENetPeer is null)
        {
            if (_ownENetId is null)
            {
                GD.PrintErr("Own ENet ID is null");
                return;
            }

            _localENetPeer = new ENetMultiplayerPeer();
            _error = _localENetPeer.CreateMesh((int)_ownENetId);

            if (_error != Error.Ok)
                GD.PrintErr("Error creating ENet Mesh: " + _error);

            GetTree().GetMultiplayer().SetMultiplayerPeer(_localENetPeer);
        }

        if (_currentPeer is null)
        {
            GD.PrintErr("Null current peer");
            return;
        }

        if (_ownPort is null)
        {
            GD.PrintErr("Invalid own port");
            return;
        }

        _error = PeerENetConnection.CreateHostBound("*", (ushort)_ownPort, 1);
        if (_error != Error.Ok)
        {
            GD.PrintErr($"Error creating ENet Host bound on port {_ownPort}: " + _error);
            return;
        }

        if (!_isHost)
        {
            // TODO: implement proper connection waiting
            await Task.Delay(1000);

            // TODO: implement better port mismatch treatment

            var ip = _currentPeer.MainIp ?? _currentPeer.Ips[0];

            ushort port;
            if (_currentPeer.MainPortBytes is not null)
                port = ToUInt16BigEndian(_currentPeer.MainPortBytes, 0);
            else
                port = _currentPeer.PortRange[0];

            PeerENetConnection.ConnectToHost(ip, port);
        }

        await WaitForENetConnection(3d, PeerENetConnection);

        if (PeerENetConnection.GetPeers().Count == 0 ||
            PeerENetConnection.GetPeers()[0].GetState() != ENetPacketPeer.PeerState.Connected)
        {
            GD.PrintErr("No peer connected to ENet");
            return;
        }

        GD.Print("ENet connection to peer established successfully");

        _error = _localENetPeer.AddMeshPeer(ToInt32BigEndian(_currentPeer.ENetIdBytes, 0), PeerENetConnection);
        if (_error != Error.Ok)
            GD.PrintErr("Error adding ENet Mesh peer: " + _error);

        if (_serverENetPacketPeer == null)
        {
            GD.PrintErr("Server Packet Peer is null");
            return;
        }

        _error = _serverENetPacketPeer.Send(0, [(byte)MessageType.SendHolePunched],
            (int)ENetPacketPeer.FlagReliable);

        if (_error != Error.Ok)
            GD.PrintErr("Error sending hole punched message to server: " + _error);
        else
            GD.Print("Sent hole punched message to server");
    }

    public override void _ExitTree()
    {
        _serverENetPacketPeer?.PeerDisconnect();
        _localENetPeer?.Close();
        PeerUdp.Close();
        _pingPeerTimer.Stop();
    }

    private static async Task WaitForENetConnection(double timeoutSecs, ENetConnection eNetConnection)
    {
        var timeout = DateTime.Now.AddSeconds(timeoutSecs);
        while (DateTime.Now < timeout)
        {
            var service = eNetConnection.Service();
            if (service == null)
            {
                GD.PrintErr("ENet service is null");
                break;
            }

            var eventType = (ENetConnection.EventType)(long)service[0];

            switch (eventType)
            {
                case ENetConnection.EventType.Connect:
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
                    GD.PrintErr("Unknown ENet event type: " + eventType);
                    break;
            }

            if (eventType == ENetConnection.EventType.Connect) break;

            await Task.Delay(50);
        }
    }

private static ushort ToUInt16BigEndian(byte[] data, int startIndex)
    {
        if (startIndex + 2 > data.Length)
            GD.PrintErr("Not enough bytes to read UInt16");

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
            GD.PrintErr("Not enough bytes to read Int32");

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
    
    private enum MessageType : byte
    {
        // 1 byte for message type, 1 byte for Ips Length, 1 byte for room length, 1 byte for nickname length, 4 X i bytes for private ips and r + n bytes for room and nickname strings
        SendHostRegister,       
        
        // 1 byte for message type, 1 byte for Ips Length, 1 byte for room length, 1 byte for nickname length, 4 X i bytes for private ips and r + n bytes for room and nickname strings
        SendClientRegister,    
        
        // 1 byte for message type
        SendHolePunched,       
        
        // 1 byte for message type
        ReceiveRegisterFail,    
        
        // 1 byte for message type, 2 bytes for Port
        ReceiveRegisterSuccess, 
        
        // 1 byte for message type, 2 bytes for sync time, 4 bytes for public IP, 2 bytes for port
        ReceivePeerInfo,   
        
        // 1 byte for message type, 2 bytes for port
        Greet,              
        
        // 1 byte for message type, 2 bytes for port
        Confirm,        
        
        // 1 byte for message type, 4 bytes for ENetId
        Go                      
    }
    
    public class Peer
    {
        public string[] Ips { get; }

        public string? MainIp { get; set; }
        
        public byte[] ENetIdBytes { get; }
        
        public string? Nickname { get; set; }
        
        public byte[]? MainPortBytes { get; }
        
        public ushort[] PortRange { get; }
        
        public Peer(string[] ips, ushort port, int eNetId)
        {
            Ips = ips;
            if (ips.Length == 1)
                MainIp = ips[0];
            ENetIdBytes = GetBytesBigEndian(eNetId);
            
            var portList = new List<ushort>();
            for (var currentPort = (ushort)(port - PORT_CASCADE_RANGE); currentPort <= port + PORT_CASCADE_RANGE; currentPort++)
                if (currentPort >= 1024)
                    portList.Add(currentPort);
                    
            PortRange = portList.ToArray();
        }
    }
}