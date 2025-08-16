namespace UltraPong;

using Godot;

public partial class OnlineMenu : Control
{
	private LineEdit RoomNode => GetNode<LineEdit>("Room");
	private LineEdit NicknameNode => GetNode<LineEdit>("Nickname");

	private ENetManager _eNetManager = new() {Name = "ENetManager"};

	private Button QuitButton => GetNode<Button>("Menu Schema/Quit");

	public override void _Ready()
	{
		QuitButton.Pressed += OnQuitPressed;
		_eNetManager.RoomRegistered += OnRoomRegistered;
		GetTree().GetMultiplayer().ConnectedToServer += OnEnetConnected;

		if (OS.HasFeature("dedicated_server"))
			SetupRoom(true);
		
		GetTree().GetRoot().CallDeferred("add_child", _eNetManager, true);

		_ = _eNetManager.ConnectToIceServer();
	}

	// Signaled through Create or Join Button
	private void SetupRoom(bool isHost)
	{
		if (NicknameNode.Text.Contains(ENetManager.RESERVED_CHAR))
			NicknameNode.Text = NicknameNode.Text.Replace(ENetManager.RESERVED_CHAR, '_');
		if (RoomNode.Text.Contains(ENetManager.RESERVED_CHAR))
			RoomNode.Text = RoomNode.Text.Replace(ENetManager.RESERVED_CHAR, '_');
		
		_eNetManager.SendRegisterMessage(isHost, NicknameNode.Text, RoomNode.Text);
	}

	// Signaled when the Tree Multiplayer Peer connects to host
	private void OnEnetConnected()
	{
		GD.Print("Connected to ENet host, changing scene");
		GetTree().CallDeferred("change_scene_to_file", "res://Main.tscn");
	}
	
	// Signaled on host when the room is registered in the server
	private void OnRoomRegistered()
	{
		GetTree().CallDeferred("change_scene_to_file", "res://Main.tscn");
	}
	
	// Signaled through Quit Button
	private void OnQuitPressed()
	{
		GetTree().CallDeferred("change_scene_to_file", "res://Main Menu.tscn");
	}
	
	public override void _ExitTree()
	{
		QuitButton.Pressed -= OnQuitPressed;
		GetTree().GetMultiplayer().ConnectedToServer -= OnEnetConnected;
		_eNetManager.RoomRegistered -= OnRoomRegistered;
	}
}