using Godot;

public partial class Menu : Control
{
	private void JoinRoom()
	{
		string ip = GetNode<LineEdit>("Room").Text ?? "localhost";
		
		var peer = new ENetMultiplayerPeer();

		var err = peer.CreateClient(ip, GLOBAL.PORT);
		Multiplayer.MultiplayerPeer = peer;
		
		if (err != Error.Ok || Multiplayer.IsServer())
			return;
		GetTree().ChangeSceneToFile("res://Main.tscn");
	}
	
	private void CreateRoom()
	{
		var peer = new ENetMultiplayerPeer();

		var err = peer.CreateServer(GLOBAL.PORT, 4);
		Multiplayer.MultiplayerPeer = peer;
		
		if (err != Error.Ok)
			return;

		//Get User IP through OS Environment Variable
		string ip = IP.ResolveHostname(OS.HasFeature("windows") ?
			OS.GetEnvironment("COMPUTERNAME") : OS.GetEnvironment("HOSTNAME"), (IP.Type)1);

		GD.Print("Server running on IP: " + ip);

		GetTree().ChangeSceneToFile("res://Main.tscn");
	}
}
