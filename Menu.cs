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

		if (OS.HasFeature("dedicated_server"))
		{ 
			UserStats.Nickname = "Host";
			SetupRoom(true);
			GD.Print("Running as dedicated server");
		}
	}

	//Called thorugh button signals
	private void SetupRoom(bool isHost)
	{
		UserStats.Nickname = GetNode<LineEdit>("Nickname").Text;
		UserStats.Sensitivity = (float)GetNode<HSlider>("Sensitivity").Value;
		
		JsonFileAccess.Write("user://userStats.json", UserStats);

		var nickname = UserStats.Nickname == "" ? "Player" : UserStats.Nickname;
		HolePuncher.ConnectToServer(isHost, Room, nickname);
		
		HolePuncher.Connect(HolePuncher.SignalName.SessionRegistered, new Callable(this, nameof(EnterGame)));
		HolePuncher.Connect(HolePuncher.SignalName.HolePunched, new Callable(this, nameof(EnterGame)));
	}
	
	//Called through 'hole punched' signal
	private void EnterGame(int myPort, int hostsPort, string hostsAddress)
	{
		GD.Print($"My port: {myPort}, hosts port: {hostsPort}, hosts address: {hostsAddress}");
		
		var peer = new ENetMultiplayerPeer();

		Error err;
		if (HolePuncher.IsHost)
			err = peer.CreateServer(myPort, 4);
		else
			err = peer.CreateClient(hostsAddress, hostsPort, localPort: myPort);

		if (err != Error.Ok)
		{
			GD.PrintErr("Error creating multiplayer peer: " + err);
			return;
		}
		
		GD.Print("Created multiplayer peer successfully");

		GetTree().GetMultiplayer().SetMultiplayerPeer(peer);
		
		GetTree().ChangeSceneToFile("res://Main.tscn");
	}
	
	public override void _ExitTree()
	{
		HolePuncher.Disconnect(HolePuncher.SignalName.SessionRegistered, 
			new Callable(this, nameof(EnterGame)));
		HolePuncher.Disconnect(HolePuncher.SignalName.HolePunched, 
			new Callable(this, nameof(EnterGame)));
	}
}
