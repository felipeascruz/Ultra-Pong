namespace UltraPong;

using Godot;

public partial class Menu : Control
{
	private int _port = 1910;
	private static readonly UserStats UserStats = JsonFileAccess.Read<UserStats>("user://userStats.json");

	public override void _Ready()
	{
		GetNode<LineEdit>("Nickname").Text = UserStats.Nickname;
		GetNode<HSlider>("Sensitivity").Value = UserStats.Sensitivity * 100D;
		GetNode<LineEdit>("Nickname").GrabFocus();
	}
	
	private void JoinRoom()
	{
		SaveUserStats();
		
		string ip = GetNode<LineEdit>("Room").Text;
		
		var peer = new ENetMultiplayerPeer();

		var err = peer.CreateClient(ip, _port);

		if (err != Error.Ok)
		{
			GD.PrintErr(err);
			return;
		}
		
		Multiplayer.MultiplayerPeer = peer;

		var connectedCallable = new Callable(this, nameof(Connected));
		var disconnectedCallable = new Callable(this, nameof(Disconnected));
		
		if (Multiplayer.IsConnected("connected_to_server", connectedCallable) && Multiplayer.IsConnected("connection_failed", disconnectedCallable))
		{
			GD.Print("disconnected");
			Multiplayer.Disconnect("connected_to_server", connectedCallable);
			Multiplayer.Disconnect("connection_failed", disconnectedCallable);
		}
		else
		{
			Multiplayer.Connect("connected_to_server", connectedCallable);
			Multiplayer.Connect("connection_failed", disconnectedCallable);
		}
	}
	
	private void Disconnected()
	{
		GD.Print("Unable to connect to server");
		Multiplayer.MultiplayerPeer.Close();
	}
	
	private void Connected()
	{
		GetTree().ChangeSceneToFile("res://Main.tscn");
	}
	
	private void CreateRoom()
	{
		SaveUserStats();
		
		var peer = new ENetMultiplayerPeer();
		
		if (peer.CreateServer(_port) != Error.Ok)
			return;
		
		Multiplayer.MultiplayerPeer = peer;
		
		//Get User IP through OS Environment Variable
		string ip = IP.ResolveHostname(OS.HasFeature("windows") ?
			OS.GetEnvironment("COMPUTERNAME") : OS.GetEnvironment("HOSTNAME"), (IP.Type)1);

		GD.Print("Server running on IP: " + ip);

		GetTree().ChangeSceneToFile("res://Main.tscn");
	}

	private void SaveUserStats()
	{
		UserStats.Nickname = GetNode<LineEdit>("Nickname").Text;
		UserStats.Sensitivity = (float)GetNode<HSlider>("Sensitivity").Value/100F;
		
		JsonFileAccess.Write("user://userStats.json", UserStats);
	}
}
