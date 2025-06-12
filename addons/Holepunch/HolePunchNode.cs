using Godot;
using System.Collections.Generic;
using System.Text;

public partial class HolePunchNode : Node
{
    // Signals - equivalent to GDScript signals
    [Signal]
    public delegate void HolePunchedEventHandler(int myPort, int hostsPort, string hostsAddress);
    
    [Signal]
    public delegate void SessionRegisteredEventHandler();

    private PacketPeerUdp serverUdp;
    private PacketPeerUdp peerUdp;

    // Set the rendezvous address to the IP address of your third party server
    private string rendezvousAddress = "20.206.244.22";
    // Set the rendezvous port to the port of your third party server
    private int rendezvousPort = 1910;
    // This is the range of ports you will search if you hear no response from the first port tried
    private int portCascadeRange = 10;
    // The amount of messages of the same type you will send before cascading or giving up
    private int responseWindow = 10;

    private bool foundServer = false;
    private bool receivedPeerInfo = false;
    private bool receivedPeerGreet = false;
    private bool receivedPeerConfirm = false;
    private bool receivedPeerGo = false;

    public bool isHost = false;

    private int ownPort;
    private Dictionary<string, Dictionary<string, Variant>> peer = new Dictionary<string, Dictionary<string, Variant>>();
    private string hostAddress = "";
    private int hostPort = 0;
    private string clientName;
    private Timer pTimer;
    private string sessionId;

    private int portsTried = 0;
    private int greetsSent = 0;
    private int gosSent = 0;

    // Constants
    private const string REGISTER_SESSION = "rs:";
    private const string REGISTER_CLIENT = "rc:";
    private const string EXCHANGE_PEERS = "ep:";
    private const string CHECKOUT_CLIENT = "cc:";
    private const string PEER_GREET = "greet";
    private const string PEER_CONFIRM = "confirm";
    private const string PEER_GO = "go";
    private const string SERVER_OK = "ok";
    private const string SERVER_INFO = "peers";

    private const int MAX_PLAYER_COUNT = 4;

    public override void _Ready()
    {
        serverUdp = new PacketPeerUdp();
        peerUdp = new PacketPeerUdp();
        
        pTimer = new Timer();
        GetNode("/root/").CallDeferred("add_child", pTimer);
        pTimer.Timeout += OnPingPeer;
        pTimer.WaitTime = 0.1F;
    }

    public override void _Process(double delta)
    {
        // Handle peer UDP packets
        if (peerUdp.GetAvailablePacketCount() > 0)
        {
            byte[] arrayBytes = peerUdp.GetPacket();
            string packetString = Encoding.ASCII.GetString(arrayBytes);
            
            GD.Print($"Received from peer: {packetString}");
            if (!receivedPeerGreet)
            {
                if (packetString.StartsWith(PEER_GREET))
                {
                    string[] m = packetString.Split(':');
                    HandleGreetMessage(m[1], int.Parse(m[2]), int.Parse(m[3]));
                }
            }

            if (!receivedPeerConfirm)
            {
                if (packetString.StartsWith(PEER_CONFIRM))
                {
                    GD.Print("Received peer confirm");
                    string[] m = packetString.Split(':');
                    HandleConfirmMessage(m[2], m[1], m[4], m[3]);
                }
            }

            if (!receivedPeerGo)
            {
                if (packetString.StartsWith(PEER_GO))
                {
                    string[] m = packetString.Split(':');
                    HandleGoMessage(m[1]);
                }
            }
        }

        // Handle server UDP packets
        if (serverUdp.GetAvailablePacketCount() > 0)
        {
            byte[] arrayBytes = serverUdp.GetPacket();
            string packetString = Encoding.ASCII.GetString(arrayBytes);
            GD.Print($"Received from server: {packetString}");
            
            if (packetString.StartsWith(SERVER_OK))
            {
                string[] m = packetString.Split(':');
                ownPort = int.Parse(m[1]);
                EmitSignal(SignalName.SessionRegistered);
                if (isHost)
                {
                    if (!foundServer)
                    {
                        SendClientToServer();
                    }
                }
                foundServer = true;
            }

            if (!receivedPeerInfo)
            {
                if (packetString.StartsWith(SERVER_INFO))
                {
                    serverUdp.Close();
                    string[] m = packetString.Split(':');
                    peer[m[1]] = new Dictionary<string, Variant>
                    {
                        {"port", m[3]},
                        {"address", m[2]}
                    };
                    receivedPeerInfo = true;
                    StartPeerContact();
                }
            }
        }
    }

    private void HandleGreetMessage(string peerName, int peerPort, int myPort)
    {
        if (ownPort != myPort)
        {
            GD.Print($"Port mismatch: own={ownPort}, received={myPort}. Rebinding...");
            ownPort = myPort;
            peerUdp.Close();
        
            var err = peerUdp.Bind(ownPort);
            if (err != Error.Ok)
            {
                GD.PrintErr($"Failed to rebind peer UDP to port {ownPort}: {err}");
                return;
            }
            GD.Print($"Successfully rebound to port {ownPort}");
        }
        receivedPeerGreet = true;
    }

    private void HandleConfirmMessage(string peerName, string peerPortStr, string myPortStr, string isHostStr)
    {
        int peerPort = int.Parse(peerPortStr);
        int myPort = int.Parse(myPortStr);
        bool peerIsHost = isHostStr.ToLower() == "true";

        if (peer.ContainsKey(peerName) && peer[peerName]["port"].AsInt32() != peerPort)
        {
            peer[peerName]["port"] = peerPort;
        }

        if (peer.ContainsKey(peerName))
        {
            peer[peerName]["is_host"] = peerIsHost;
            if (peerIsHost)
            {
                hostAddress = peer[peerName]["address"].AsString();
                hostPort = peer[peerName]["port"].AsInt32();
            }
        }
        receivedPeerConfirm = true;
    }

    private void HandleGoMessage(string peerName)
    {
        receivedPeerGo = true;
        GD.Print("Hole punch complete - emitting signal");
        EmitSignal(SignalName.HolePunched, ownPort, hostPort, hostAddress);
        peerUdp.Close();
        pTimer.Stop();
        SetProcess(false);
    }

    private void CascadePeer(string address, int peerPort)
    {
        for (int i = peerPort - portCascadeRange; i < peerPort + portCascadeRange; i++)
        {
            peerUdp.SetDestAddress(address, i);
            string message = $"greet:{clientName}:{ownPort}:{i}";
            byte[] buffer = Encoding.UTF8.GetBytes(message);
            peerUdp.PutPacket(buffer);
            portsTried += 1;
        }
    }

    private void OnPingPeer()
    {
        if (!receivedPeerConfirm && greetsSent < responseWindow)
        {
            foreach (string p in peer.Keys)
            {
                peerUdp.SetDestAddress(peer[p]["address"].AsString(), peer[p]["port"].AsInt32());
                string message = $"greet:{clientName}:{ownPort}:{peer[p]["port"]}";
                byte[] buffer = Encoding.UTF8.GetBytes(message);
                peerUdp.PutPacket(buffer);
                greetsSent++;
                if (greetsSent == responseWindow)
                {
                    GD.Print("Receiving no confirm. Starting port cascade");
                    // if the other player hasn't responded, we should try more ports
                }
            }
        }

        if (!receivedPeerConfirm && greetsSent == responseWindow)
        {
            foreach (string p in peer.Keys)
            {
                CascadePeer(peer[p]["address"].AsString(), peer[p]["port"].AsInt32());
            }
            greetsSent += 1;
        }

        if (receivedPeerGreet && !receivedPeerGo)
        {
            foreach (string p in peer.Keys)
            {
                GD.Print("Sending confirm to peer on port " + peerUdp.GetPacketPort());
                //peerUdp.SetDestAddress(peer[p]["address"].AsString(), peer[p]["port"].AsInt32());
                peerUdp.SetDestAddress(peer[p]["address"].AsString(), peerUdp.GetPacketPort());
                string message = $"confirm:{ownPort}:{clientName}:{isHost}:{peer[p]["port"]}";
                byte[] buffer = Encoding.UTF8.GetBytes(message);
                peerUdp.PutPacket(buffer);
            }
        }

        if (receivedPeerConfirm)
        {
            foreach (string p in peer.Keys)
            {
                GD.Print("Sending go to peer on port " + peerUdp.GetPacketPort());
                //peerUdp.SetDestAddress(peer[p]["address"].AsString(), peer[p]["port"].AsInt32());
                peerUdp.SetDestAddress(peer[p]["address"].AsString(), peerUdp.GetPacketPort());
                string message = $"go:{clientName}";
                byte[] buffer = Encoding.UTF8.GetBytes(message);
                peerUdp.PutPacket(buffer);
            }
            gosSent += 1;

            if (gosSent >= responseWindow) // the other player has confirmed and is probably waiting
            {
                GD.Print("Hole punch complete - emitting signal without go");
                EmitSignal(SignalName.HolePunched, ownPort, hostPort, hostAddress);
                pTimer.Stop();
                SetProcess(false);
            }
        }
    }

    private void StartPeerContact()
    {
        byte[] goodbyeBuffer = Encoding.UTF8.GetBytes("goodbye");
        var err = serverUdp.PutPacket(goodbyeBuffer);
        serverUdp.Close();
        
        if (peerUdp.IsBound())
        {
            peerUdp.Close();
        }
        
        err = peerUdp.Bind(ownPort, "*");
        if (err != Error.Ok)
        {
            GD.Print($"Error binding on: {ownPort} Error: {err}");
        }
        pTimer.Start();
    }

    // This function can be called to the server if you want to end the holepunch before the server closes the session
    public void FinalizePeers(string id)
    {
        string message = EXCHANGE_PEERS + id;
        byte[] buffer = Encoding.UTF8.GetBytes(message);
        serverUdp.SetDestAddress(rendezvousAddress, rendezvousPort);
        serverUdp.PutPacket(buffer);
    }

    // Remove a client from the server
    public void Checkout()
    {
        string message = CHECKOUT_CLIENT + clientName;
        byte[] buffer = Encoding.UTF8.GetBytes(message);
        serverUdp.SetDestAddress(rendezvousAddress, rendezvousPort);
        serverUdp.PutPacket(buffer);
    }

    // Call this function when you want to start the holepunch process
    public void StartTraversal(string id, bool isPlayerHost, string playerName, int maxPlayers = MAX_PLAYER_COUNT)
    {
        if (serverUdp.IsBound())
        {
            serverUdp.Close();
        }

        var err = serverUdp.Bind(rendezvousPort, "*");
        if (err != Error.Ok)
        {
            GD.Print($"Error binding on: {rendezvousAddress}:{rendezvousPort}");
        }
        else
        {
            GD.Print($"Binding to: {rendezvousAddress}:{rendezvousPort}");
        }
        
        isHost = isPlayerHost;
        clientName = playerName;
        foundServer = false;
        receivedPeerInfo = false;
        receivedPeerGreet = false;
        receivedPeerConfirm = false;
        receivedPeerGo = false;
        peer.Clear();

        portsTried = 0;
        greetsSent = 0;
        gosSent = 0;
        
        if (string.IsNullOrEmpty(id))
        {
            id = "1";
        }
        sessionId = id;

        if (isHost)
        {
            string message = REGISTER_SESSION + sessionId + ":" + maxPlayers.ToString();
            byte[] buffer = Encoding.UTF8.GetBytes(message);
            serverUdp.Close();
            serverUdp.SetDestAddress(rendezvousAddress, rendezvousPort);
            serverUdp.PutPacket(buffer);
        }
        else
        {
            SendClientToServer();
        }
    }

    // Register a client with the server
    private async void SendClientToServer()
    {
        await ToSignal(GetTree().CreateTimer(2.0), SceneTreeTimer.SignalName.Timeout);
        string message = REGISTER_CLIENT + clientName + ":" + sessionId;
        byte[] buffer = Encoding.UTF8.GetBytes(message);
        serverUdp.Close();
        serverUdp.SetDestAddress(rendezvousAddress, rendezvousPort);
        serverUdp.PutPacket(buffer);
    }

    public void CleanupSockets()
    {
        GD.Print("Forcing socket cleanup...");
        if (peerUdp.IsBound())
        {
            peerUdp.Close();
        }
        if (serverUdp.IsBound())
        {
            serverUdp.Close();
        }
    }

    public override void _ExitTree()
    {
        serverUdp?.Close();
        peerUdp?.Close();
    }
}