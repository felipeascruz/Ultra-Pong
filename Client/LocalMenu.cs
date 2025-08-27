namespace UltraPong;

using Godot;

public partial class LocalMenu : Control
{
    private Button QuitButton => GetNode<Button>("Menu Schema/Quit");
    
    public override void _Ready()
    {
        GetTree().GetMultiplayer().ConnectedToServer += OnENetConnected;
        QuitButton.Pressed += OnQuitPressed;
        GetTree().GetRoot().GetNodeOrNull<ENetManager>("ENetManager")?.QueueFree();
    }
    
    // Signaled throug Create or Join Button
    private void SetupRoom(bool isHost)
    {
        var peer = new ENetMultiplayerPeer();
        Error err;

        if (isHost)
        {
            // Create server
            err = peer.CreateServer(8080, 4);

            //Get User IP through OS Environment Variable
            string ip = IP.ResolveHostname(OS.HasFeature("windows") ?
                OS.GetEnvironment("COMPUTERNAME") : OS.GetEnvironment("HOSTNAME"), (IP.Type)1);
            
            if (err != Error.Ok)
            {
                GD.PrintErr("Error creating local ENet host: " + err);
                return;
            }

            GD.Print("Server running on IP: " + ip);
            GetTree().CallDeferred("change_scene_to_file", "res://Main.tscn");
        }
        else
        {
            // Create client
            string ip = GetNode<LineEdit>("Host IP").Text ?? "localhost";
            
            err = peer.CreateClient(ip, 8080);
            
            if (err != Error.Ok)
            {
                GD.PrintErr("Error creating local ENet client: " + err);
                return;
            }
        }
        
        GetTree().GetMultiplayer().SetMultiplayerPeer(peer);
    }

    // Signaled in clients when the Tree Multiplayer Peer connects to host
    private void OnENetConnected()
    {
        GetTree().CallDeferred("change_scene_to_file", "res://Main.tscn");
    }
    
    // Signaled through Quit Button
    private void OnQuitPressed()
    {
        GetTree().CallDeferred("change_scene_to_file", "res://Main Menu.tscn");
    }
	
    public override void _ExitTree()
    {
        GetTree().GetMultiplayer().ConnectedToServer -= OnENetConnected;
        QuitButton.Pressed -= OnQuitPressed;
    }
}