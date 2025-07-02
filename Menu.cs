namespace UltraPong;

using Godot;

public partial class Menu : Control
{
	private static readonly UserStats UserStats = JsonFileAccess.Read<UserStats>("user://userStats.json");
	private LineEdit RoomNode => GetNode<LineEdit>("Room");
	private LineEdit NicknameNode => GetNode<LineEdit>("Nickname");
	private ENetManager _eNetManager = new();
	private MultiplayerApi TreeMultiplayer => GetTree().GetMultiplayer();

	public override void _Ready()
	{
		GetNode<HSlider>("Sensitivity").Value = UserStats.Sensitivity;
		
		NicknameNode.Text = UserStats.Nickname;
		NicknameNode.GrabFocus();
		NicknameNode.CaretColumn = NicknameNode.Text.Length;
		
		_eNetManager.RoomRegistered += OnRoomRegistered;
		TreeMultiplayer.ConnectedToServer += OnEnetConnected;

		if (OS.HasFeature("dedicated_server"))
			SetupRoom(true);
	}

	private void SetupRoom(bool isHost)
	{
		if (NicknameNode.Text.Contains(ENetManager.RESERVED_CHAR))
			NicknameNode.Text = NicknameNode.Text.Replace(ENetManager.RESERVED_CHAR, '_');
		if (RoomNode.Text.Contains(ENetManager.RESERVED_CHAR))
			RoomNode.Text = RoomNode.Text.Replace(ENetManager.RESERVED_CHAR, '_');
		
		UserStats.Nickname = NicknameNode.Text;
		UserStats.Sensitivity = (float)GetNode<HSlider>("Sensitivity").Value;
		
		JsonFileAccess.Write("user://userStats.json", UserStats);
		
		GetTree().GetRoot().CallDeferred("add_child", _eNetManager, true);
		
		_eNetManager.ConnectToICEServer(isHost, UserStats.Nickname, RoomNode.Text);
	}

	// Signaled when the Tree Multiplayer Peer connects to host
	private void OnEnetConnected()
	{
		GD.Print("Connected to ENet host, changing scene");
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
		_eNetManager.RoomRegistered -= OnRoomRegistered;
	}
}