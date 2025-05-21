using System.Text.Json;

namespace UltraPong;
using Godot;

public partial class Menu : Control
{
	private int _port = 1910;
	private static UserStats UserStats => JsonSerializer.Deserialize<UserStats>(GD.Load<string>("res://userStats.json"));
	private static PlayerStats PlayerStats => GD.Load<PlayerStats>("res://PlayerStats.res");

	public override void _Ready()
	{
		GetNode<LineEdit>("Username").Text = UserStats.Nickname;
		GetNode<HSlider>("Sensitivity").Value = PlayerStats.Sensitivity * 100D;
		GetNode<LineEdit>("Username").GrabFocus();
	}
	
	private void JoinRoom()
	{
		SaveStats();
		
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
		SaveStats();
		
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

	private void SaveStats()
	{
		UserStats.Nickname = GetNode<LineEdit>("Username").Text;
		using var saveFile = FileAccess.Open("user://userStats.json", FileAccess.ModeFlags.Write);
		saveFile.StoreString(JsonSerializer.Serialize(UserStats));
		
		PlayerStats.Sensitivity = (float)GetNode<HSlider>("Sensitivity").Value/100F;
		ResourceSaver.Save(PlayerStats, "res://PlayerStats.res");
	}
}
