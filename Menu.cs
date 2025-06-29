namespace UltraPong;

using Godot;

public partial class Menu : Control
{
	private static readonly UserStats UserStats = JsonFileAccess.Read<UserStats>("user://userStats.json");
	private LineEdit RoomNode => GetNode<LineEdit>("Room");
	private LineEdit NicknameNode => GetNode<LineEdit>("Nickname");
	private HolePuncher HolePuncher => GetNode<HolePuncher>("/root/HolePuncher");
	private MultiplayerApi TreeMultiplayer => GetTree().GetMultiplayer();

	public override void _Ready()
	{
		GetNode<HSlider>("Sensitivity").Value = UserStats.Sensitivity;
		
		NicknameNode.Text = UserStats.Nickname;
		NicknameNode.GrabFocus();
		NicknameNode.CaretColumn = NicknameNode.Text.Length;
		
		HolePuncher.RoomRegistered += OnRoomRegistered;
		TreeMultiplayer.ConnectedToServer += OnEnetConnected;

		if (OS.HasFeature("dedicated_server"))
			SetupRoom(true);
	}

	private void SetupRoom(bool isHost)
	{
		if (NicknameNode.Text.Contains(HolePuncher.RESERVED_CHAR))
			NicknameNode.Text = NicknameNode.Text.Replace(HolePuncher.RESERVED_CHAR, '_');
		if (RoomNode.Text.Contains(HolePuncher.RESERVED_CHAR))
			RoomNode.Text = RoomNode.Text.Replace(HolePuncher.RESERVED_CHAR, '_');
		
		UserStats.Nickname = NicknameNode.Text;
		UserStats.Sensitivity = (float)GetNode<HSlider>("Sensitivity").Value;
		
		JsonFileAccess.Write("user://userStats.json", UserStats);
		
		GetTree().GetRoot().CallDeferred("add_child", new ENetManager(isHost){Name = "ENetManager"}, true);
		
		HolePuncher.ConnectToServer(isHost, UserStats.Nickname, RoomNode.Text);
	}

	// Signaled when the Tree Multiplayer Peer connects
	private void OnEnetConnected()
	{
		GD.Print("Connected to ENet server, changing scene");
		GetTree().ChangeSceneToFile("res://Main.tscn");
	}
	
	// Signaled on host when the room is registered in the server
	private void OnRoomRegistered()
	{
		GetTree().CallDeferred("change_scene_to_file", "res://Main.tscn");
	}

	public override void _ExitTree()
	{
		TreeMultiplayer.ConnectedToServer -= OnEnetConnected;
		HolePuncher.RoomRegistered -= OnRoomRegistered;
	}
}