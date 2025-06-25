namespace UltraPong;

using Godot;

public partial class Menu : Control
{
	private static readonly UserStats UserStats = JsonFileAccess.Read<UserStats>("user://userStats.json");
	private string Room => GetNode<LineEdit>("Room").Text;
	private HolePuncher HolePuncher => GetNode<HolePuncher>("/root/HolePuncher");

	public override void _Ready()
	{
		GetNode<HSlider>("Sensitivity").Value = UserStats.Sensitivity;
		
		var nicknameNode = GetNode<LineEdit>("Nickname");
		nicknameNode.Text = UserStats.Nickname;
		nicknameNode.GrabFocus();
		nicknameNode.CaretColumn = nicknameNode.Text.Length;
		
		HolePuncher.Connect(HolePuncher.SignalName.ENetPortDiscovered, 
			new Callable(this, nameof(OnENetPortDiscovered)));
		HolePuncher.Connect(HolePuncher.SignalName.HostPortReceived, 
			new Callable(this, nameof(OnHostPortReceived)));

		if (OS.HasFeature("dedicated_server"))
			SetupRoom(true);
	}

	private void SetupRoom(bool isHost)
	{
		UserStats.Nickname = GetNode<LineEdit>("Nickname").Text;
		UserStats.Sensitivity = (float)GetNode<HSlider>("Sensitivity").Value;
		
		JsonFileAccess.Write("user://userStats.json", UserStats);
		
		HolePuncher.ConnectToServer(isHost, Room, UserStats.Nickname);
	}
	
	// Signaled through Hole Puncher node
	private void OnHostPortReceived(ushort enetPort)
	{
		var peer = new ENetMultiplayerPeer();
		
		var err = peer.CreateServer(enetPort, 4);
		if (err != Error.Ok)
		{
			GD.PrintErr($"Error creating ENet host: " + err);
			return;
		}
		
		GD.Print($"ENet server created successfully");
		
		GetTree().GetMultiplayer().SetMultiplayerPeer(peer);
		GetTree().ChangeSceneToFile("res://Main.tscn");
	}
	
	// Signaled through Hole Puncher node
	private void OnENetPortDiscovered(string hostAddress, ushort enetPort)
	{
		GD.Print($"Discovered host ENet port: {enetPort} at {hostAddress}");
		var peer = new ENetMultiplayerPeer();
		
		var err = peer.CreateClient(hostAddress, enetPort);
		if (err != Error.Ok)
		{
			GD.PrintErr($"Error connecting to ENet server {hostAddress}:{enetPort} - " + err);
			return;
		}
		
		GD.Print($"Connected to ENet server at {hostAddress}:{enetPort}");
		
		GetTree().GetMultiplayer().SetMultiplayerPeer(peer);
		GetTree().ChangeSceneToFile("res://Main.tscn");
	}

	public override void _ExitTree()
	{
		HolePuncher.Disconnect(HolePuncher.SignalName.ENetPortDiscovered, new Callable(this, nameof(OnENetPortDiscovered)));
		HolePuncher.Disconnect(HolePuncher.SignalName.HostPortReceived, new Callable(this, nameof(OnHostPortReceived)));
	}
}