using System.Net;

namespace UltraPong;

using System;
using System.Collections.Generic;
using System.Linq;
using Godot;
public partial class WorldHandler : Node
{
	private readonly Vector2[] _ballSpawnPoints =JsonFileAccess.Read<BallStats>("res://ballStats.json").SpawnPoints;
	private const string World = "../../World/";
	
	public override void _Ready()
	{
		Multiplayer.Connect("server_disconnected", new Callable(this, nameof(Disconnect)));

		if (!Multiplayer.IsServer())
			return;

		if (DisplayServer.GetName() == "headless" || OS.HasFeature("dedicated_server"))
		{
			Engine.PhysicsTicksPerSecond = 30;
			Engine.MaxPhysicsStepsPerFrame = 4;
		}

		Multiplayer.Connect("peer_connected", new Callable(this, nameof(PeerEntered)));
		Multiplayer.Connect("peer_disconnected", new Callable(this, nameof(PeerExited)));
	}

	public override void _ExitTree()
	{
		Multiplayer.Disconnect("server_disconnected", new Callable(this, nameof(Disconnect)));
		Multiplayer.MultiplayerPeer.Close();
		Multiplayer.MultiplayerPeer = new OfflineMultiplayerPeer();
	}
	
	public void Disconnect()
	{
		Input.MouseMode = Input.MouseModeEnum.Visible;
		GetTree().ChangeSceneToFile("res://Main Menu.tscn");
	}

	private void PeerEntered(int id)
	{
		GD.Print(id + " entered");
		
		//Spawn previous players
		foreach (Player player in GetNode(World + "Players").GetChildren())
			RpcId(id, nameof(Spawn), player.GetNode<Label>("Nickname").Text, player.Id, player.Number, player.Device.Number, player.Device.Type);
	}

	private void PeerExited(int id)
	{
		GD.Print(id + " exited");
		foreach (Player player in GetNode(World + "Players").GetChildren())
			if (id == player.Id)
				Rpc(nameof(Despawn), player.Name);
	}
	
	//Signaled through ColorRect
	public void SelectTeam(byte playerNumber, Device device)
	{
		Rpc(nameof(Despawn), Multiplayer.GetUniqueId().ToString() + device.Type + device.Number);
		
		var nickname = JsonFileAccess.Read<UserStats>("userStats.json").Nickname;
		Rpc(nameof(Spawn), nickname, Multiplayer.GetUniqueId(), playerNumber, device.Number, device.Type);
	}
	
	[Rpc(MultiplayerApi.RpcMode.AnyPeer, CallLocal = true, TransferMode = MultiplayerPeer.TransferModeEnum.Reliable, TransferChannel = 3)]
	public async void Spawn(string nickname, int id, byte playerNumber, int deviceNumber, char deviceType)
	{
		if (Multiplayer.GetRemoteSenderId() != id && Multiplayer.GetRemoteSenderId() != 1)
			return;

		var players = GetNode(World + "Players");

		if (id == Multiplayer.GetUniqueId())
			GetNodeOrNull(World + "Choose Team").QueueFree();
		
		if (players.GetChildren().Cast<Player>().Any(node => node.Name == id.ToString() + deviceType + deviceNumber))
			await ToSignal(GetTree().CreateTimer(0.1D), "timeout");
		
		var player = GD.Load<PackedScene>("res://Player.tscn").Instantiate<Player>();
		player.GetNode<Label>("Nickname").Text = nickname.Length > 50 ? nickname[..50] : nickname;
		player.Id = id;
		player.Number = playerNumber;
		player.Device = new Device(deviceNumber, deviceType);
		
		players.AddChild(player, true);
		GD.Print($"{nickname} spawned as player {playerNumber} ");

		if (players.GetChildren().Cast<Player>().Count(node => node.Number == playerNumber) > 1)
		{
			Rpc(nameof(Despawn), player.Name);
			AddChild(GD.Load<PackedScene>("res://Choose Team.tscn").Instantiate<Control>());
		}

		if (players.GetChildren().Count >= 4 && Multiplayer.IsServer())
			StartGame();
	}

	public void DespawnWrapper(string name)
	{
		Rpc(nameof(Despawn), name);
	}
	
	[Rpc(MultiplayerApi.RpcMode.AnyPeer, CallLocal = true, TransferMode = MultiplayerPeer.TransferModeEnum.Reliable, TransferChannel = 3)]
	private void Despawn(string name)
	{
		if (Multiplayer.GetRemoteSenderId().ToString() != name[..^2] && Multiplayer.GetRemoteSenderId() != 1)
			return;
		
		var player = GetNodeOrNull(World + "Players/" + name);
		if (player == null)
			return;
		
		GD.Print(player.GetNode<Label>("Nickname").Text + " despawned");
		
		player.QueueFree();
		foreach (var state in GetNode<PlayersHandler>("../PlayersHandler").StatesBuffer)
			state.Value.Remove(name);
	}
	
	public void StartGame()
	{
		ResetGame(_ballSpawnPoints[new Random().Next(0, 2)]);
		
		Rpc(nameof(SetGame));
	}

	//Only called in server
	public void ResetGame(Vector2 ballPosition)
	{
		foreach (Player player in GetNode(World + "Players").GetChildren())
		{
			player.Position = player.SpawnPoint;
			player.Rotation = 0F;
			player.ForceUpdateTransform();
		}

		GetNode<Ball>(World + "Ball").Reset(ballPosition);
		var timeDisplay = GetNode<Label>(World + "Time Display");
		timeDisplay.Position = new Vector2(ballPosition.X < 960 ? 0 : 960,timeDisplay.Position.Y);
		
		var possessionTimer = GetNodeOrNull<PossessionTimer>(World + "Possession Timer");
		if (possessionTimer is not null)
		{
			possessionTimer.Stop();
			possessionTimer.Start();
			return;
		}

		GetNode<World>(World).AddChild(GD.Load<PackedScene>("res://PossessionTimer.tscn").Instantiate<PossessionTimer>());
	}

	[Rpc(CallLocal = true, TransferMode = MultiplayerPeer.TransferModeEnum.Reliable, TransferChannel = 3)]
	private void SetGame()
	{
		GetNode<StaticBody2D>(World + "SideBorders").SetCollisionLayerValue(3, false);
		GetNode<StaticBody2D>(World + "MidField").ProcessMode = ProcessModeEnum.Inherit;
		GetNode<Area2D>(World + "Goals").ProcessMode = ProcessModeEnum.Inherit;
		
		var score = GetNode<Control>(World + "Score");
		score.Visible = true;
		foreach (var node in score.GetChildren())
			if (node is Label label)
			{
				label.Text = "0";
				label.AddThemeColorOverride("font_color", Colors.White);
			}
	}
	
	//Signaled through Area2D Goals
	private void Goal(Node2D body)
	{
		//Counting score
		var score = GetNode<Control>(World + "Score");
		switch (body.Position.X)
		{
			case < 1920/2F:
				var right = score.GetNode<Label>("Right");
				right.Text = (int.Parse(right.Text) + 1).ToString();
				ResetGame(_ballSpawnPoints[1]);
				break;
			case > 1920/2F:
				var left = score.GetNode<Label>("Left");
				left.Text = (int.Parse(left.Text) + 1).ToString();
				ResetGame(_ballSpawnPoints[0]);
				break;
		}
		
		//Check overtime
		var teams = new List<Label>();
		foreach (var node in score.GetChildren())
			if (node is Label label)
				teams.Add(label);
		
		if (int.Parse(teams[0].Text) == 10 && int.Parse(teams[1].Text) == 10)
		{
			const double overtime = 5D;
			
			foreach (Player player in GetNode(World + "Players").GetChildren())
				player.Overtime = overtime;

			var overtimeLabel = new Label();
			overtimeLabel.Text = "OVERTIME";
			overtimeLabel.HorizontalAlignment = HorizontalAlignment.Center;
			overtimeLabel.VerticalAlignment = VerticalAlignment.Center;
			overtimeLabel.Size = new Vector2(1920F, 1080F);
			overtimeLabel.AddThemeFontSizeOverride("font_size", 150);
			
			GetNode(World).AddChild(overtimeLabel, true);
			GetTree().CreateTimer(overtime).Connect("timeout", new Callable(this, nameof(QueueFreeOvertimeLabel)));
			return;
		}
		
		if (!Multiplayer.IsServer()) return;
		
		//Check win
		if (int.Parse(teams[0].Text) >= 11 && int.Parse(teams[0].Text) >= int.Parse(teams[1].Text) + 2)
		{
			Rpc(nameof(StopGame));
			teams[0].AddThemeColorOverride("font_color", Colors.Green);
		}
		else if (int.Parse(teams[1].Text) >= 11 && int.Parse(teams[1].Text) >= int.Parse(teams[0].Text) + 2)
		{
			Rpc(nameof(StopGame));
			teams[1].AddThemeColorOverride("font_color", Colors.Green);
		}
	}

	private void QueueFreeOvertimeLabel()
	{
		GetNode(World + "Label").QueueFree();
	}
	
	[Rpc(CallLocal = true, TransferMode = MultiplayerPeer.TransferModeEnum.Reliable, TransferChannel = 3)]
	private void StopGame()
	{
		GetNode<StaticBody2D>(World + "SideBorders").SetCollisionLayerValue(3, true);
		GetNode<StaticBody2D>(World + "MidField").ProcessMode = ProcessModeEnum.Disabled;
		GetNode<Area2D>(World + "Goals").ProcessMode = ProcessModeEnum.Disabled;
		ResetGame(_ballSpawnPoints[2]);
	}	
}
