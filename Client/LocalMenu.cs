namespace UltraPong;

using Godot;

public partial class LocalMenu : Control
{
    public override void _Ready()
    {
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

            GD.Print("Server running on IP: " + ip);
        }
        else
        {
            // Create client
            string ip = GetNode<LineEdit>("Host IP").Text ?? "localhost";
            
            err = peer.CreateClient(ip, 8080);
        }
        
        if (err != Error.Ok)
        {
            GD.PrintErr("Error creating local ENet connection: " + err);
            return;
        }
        
        GetTree().GetMultiplayer().SetMultiplayerPeer(peer);

        GetTree().CallDeferred("change_scene_to_file", "res://Main.tscn");
    }
}