using Godot;

public partial class Menu : Control
{
	private void JoinRoom()
	{
		string ip = GetNode<LineEdit>("Room").Text ?? "localhost";
		
		var peer = new ENetMultiplayerPeer();

		var err = peer.CreateClient(ip, Global.Port);
		Multiplayer.MultiplayerPeer = peer;

		if (err != Error.Ok)
			Multiplayer.MultiplayerPeer.Close();
		else
		{
			Multiplayer.Connect("connected_to_server", new Callable(this, nameof(Connected)));
			Multiplayer.Connect("connection_failed", new Callable(this, nameof(Disconnected)));
		}
	}
	
	private void Disconnected()
	{
		Multiplayer.MultiplayerPeer.Close();
	}
	
	private void Connected()
	{
		GetTree().ChangeSceneToFile("res://Main.tscn");
	}
	
	private void CreateRoom()
	{
		var peer = new ENetMultiplayerPeer();

		var err = peer.CreateServer(Global.Port, 4);
		Multiplayer.MultiplayerPeer = peer;

		if (err != Error.Ok)
		{
			Multiplayer.MultiplayerPeer.Close();
			return;
		}

		//Get User IP through OS Environment Variable
		string ip = IP.ResolveHostname(OS.HasFeature("windows") ?
			OS.GetEnvironment("COMPUTERNAME") : OS.GetEnvironment("HOSTNAME"), (IP.Type)1);

		GD.Print("Server running on IP: " + ip);

		GetTree().ChangeSceneToFile("res://Main.tscn");
	}
}
