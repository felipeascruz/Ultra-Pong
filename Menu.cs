namespace UltraPong;

using Godot;

public partial class Menu : Control
{
	private static readonly UserStats UserStats = JsonFileAccess.Read<UserStats>("user://userStats.json");
	private string Room => GetNode<LineEdit>("Room").Text;
	private Node HolePuncher => GetNode("/root/HolePunch");

	public override void _Ready()
	{
		GetNode<LineEdit>("Nickname").Text = UserStats.Nickname;
		GetNode<HSlider>("Sensitivity").Value = UserStats.Sensitivity;
		GetNode<LineEdit>("Nickname").GrabFocus();

		if (OS.HasFeature("dedicated_server"))
		{ 
			UserStats.Nickname = "Host";
			CreateRoom();
			GD.Print("Running as dedicated server");
		}
	}
	
	private void CreateRoom()
	{
		SetupRoom(true);
	}
	
	private void JoinRoom()
	{
		SetupRoom(false);
	}

	private void SetupRoom(bool isHost)
	{
		SaveUserStats();

		var nickname = UserStats.Nickname == "" ? "player" : UserStats.Nickname;
		HolePuncher.Call("start_traversal", Room, isHost, nickname, 2);
		
		HolePuncher.Connect("hole_punched", new Callable(this, nameof(EnterGame)));
	}
	
	private void EnterGame(int myPort, int hostsPort, string hostsAddress)
	{
		GD.Print($"My port: {myPort}, hosts port: {hostsPort}, hosts address: {hostsAddress}");
		
		var peer = new ENetMultiplayerPeer();

		Error err;
		if (HolePuncher.Get("is_host").AsBool())
			err = peer.CreateServer(myPort);
		else
			err = peer.CreateClient(hostsAddress, hostsPort, localPort: myPort);

		if (err != Error.Ok)
		{
			GD.PrintErr(err);
			return;
		}

		GetTree().GetMultiplayer().SetMultiplayerPeer(peer);
		
		GetTree().ChangeSceneToFile("res://Main.tscn");
	}

	private void SaveUserStats()
	{
		UserStats.Nickname = GetNode<LineEdit>("Nickname").Text;
		UserStats.Sensitivity = (float)GetNode<HSlider>("Sensitivity").Value;
		
		JsonFileAccess.Write("user://userStats.json", UserStats);
	}
}
