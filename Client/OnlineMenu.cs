using System;

namespace UltraPong;

using Godot;

public partial class OnlineMenu : Control
{
	private LineEdit RoomNode => GetNode<LineEdit>("Room");
	private LineEdit NicknameNode => GetNode<LineEdit>("Menu Schema/Nickname");

	private ENetManager _eNetManager = new() {Name = "ENetManager"};

	private Button QuitButton => GetNode<Button>("Menu Schema/Quit");

	public override async void _Ready()
	{
		try
		{
			var createButton = GetNode<Button>("Create");
			var joinButton = GetNode<Button>("Join");
		
			QuitButton.Pressed += OnQuitPressed;
			_eNetManager.RoomRegistered += OnHostRoomRegistered;
			GetTree().GetMultiplayer().ConnectedToServer += OnENetConnected;

			// Disable buttons initially
			createButton.Disabled = true;
			joinButton.Disabled = true;
	
			GetTree().GetRoot().CallDeferred("add_child", _eNetManager, true);

			var connectionSucceeded = await _eNetManager.ConnectToIceServer();
		
			if (!connectionSucceeded)
			{
				GD.PrintErr("Failed to connect to ICE server");
				return;
			}
		
			// TODO: Disable this debug feature
			if (OS.HasFeature("dedicated_server"))
				SetupRoom(true);
	
			// Re-enable buttons after connection
			createButton.Disabled = false;
			joinButton.Disabled = false;
		}
		catch (Exception e)
		{
			GD.PrintErr("Error while initializing OnlineMenu: " + e.Message);
		}
	}


	// Signaled through Create or Join Button
	private void SetupRoom(bool isHost)
	{
		_eNetManager.SendRegisterMessage(isHost, NicknameNode.Text, RoomNode.Text);
	}

	// Signaled in clients when the Tree Multiplayer Peer connects to host
	private void OnENetConnected()
	{
		GD.Print("Connected to ENet host, changing scene");
		GetTree().CallDeferred("change_scene_to_file", "res://Main.tscn");
	}
	
	// Signaled on host when the room is registered in the server
	private void OnHostRoomRegistered()
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
		GetTree().GetMultiplayer().ConnectedToServer -= OnENetConnected;
		_eNetManager.RoomRegistered -= OnHostRoomRegistered;
	}
}