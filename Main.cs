using System;
using Godot;
using Godot.Collections;

public partial class Main : Node2D
{
	public override void _Ready()
	{
		Multiplayer.Connect("server_disconnected", new Callable(this, nameof(Disconnect)));
		if (!Multiplayer.IsServer())
			return;
		
		Multiplayer.Connect("peer_connected", new Callable(this, nameof(Spawn)));
		Multiplayer.Connect("peer_disconnected", new Callable(this, nameof(Despawn)));
		
		if (!OS.HasFeature("dedicated_server"))
			Spawn(1);
	}

	public override void _ExitTree()
	{
		Multiplayer.Disconnect("server_disconnected", new Callable(this, nameof(Disconnect)));
		
		if (!Multiplayer.IsServer())
			return;
			
		Multiplayer.Disconnect("peer_connected", new Callable(this, nameof(Spawn)));
		Multiplayer.Disconnect("peer_disconnected", new Callable(this, nameof(Despawn)));
	}
	
	public void Disconnect()
	{
			Multiplayer.MultiplayerPeer.Close();
			Input.MouseMode = Input.MouseModeEnum.Visible;
			GetTree().ChangeSceneToFile("res://Main Menu.tscn");
	}
	
	private void Spawn(int id)
	{
		var peerlength = Multiplayer.GetPeers().Length;
		if (peerlength >= 4)
			return;

		var player = (Player)GD.Load<PackedScene>("res://Player.tscn").Instantiate<CharacterBody2D>();
		player.SpawnPoint = GLOBAL.PLAYERS_SPAWN_POINTS[Multiplayer.GetPeers().Length];
		player.Name = id.ToString();
		
		player.InitialColor = GLOBAL.ColorsArray[peerlength];
		
		GetNode("Players").AddChild(player, true);
		GD.Print(id + " spawned");
		
		if (peerlength == 3)
			StartGame();
	}
	
	private void Despawn(int id)
	{
		var players = GetNode<Node>("Players");
		if (!players.HasNode(id.ToString()))
			return;
		
		players.GetNode(id.ToString()).QueueFree();
		
		GD.Print(id + " despawned");
	}
	
	private void StartGame()
	{
		ResetGame(GLOBAL.BALL_SPAWN_POINTS[new Random().Next(0, 2)]);

		Rpc(nameof(SetGame));
	}

	private void ResetGame(Vector2 ballPosition)
	{
		foreach (Node node in GetNode("Players").GetChildren())
		{
			var player = (Player)node;
			player.SetPhysicsProcess(false);
			player.Position = player.SpawnPoint;
			player.Rotation = 0F;
		}

		GetNode<Ball>("Ball").Reset(ballPosition);
		foreach (Node node in GetNode("Players").GetChildren())
		{
			var player = (Player)node;
			player.SetPhysicsProcess(true);
		}
	}

	[Rpc(CallLocal = true, TransferMode = MultiplayerPeer.TransferModeEnum.Reliable, TransferChannel = 2)]
	private void SetGame()
	{
		GetNode<StaticBody2D>("SideBorders").SetCollisionLayerValue(3, false);
		GetNode<StaticBody2D>("MidField").ProcessMode = ProcessModeEnum.Inherit;
		GetNode<Area2D>("Goals").ProcessMode = ProcessModeEnum.Inherit;
		
		var score = GetNode<Control>("Score");
		score.Visible = true;
		foreach (var node in score.GetChildren())
			if (node is Label label)
				label.Text = "0";
	}
	
	//Signaled through Area2D Goals
	private void Goal(Node2D body)
	{
		if (!Multiplayer.IsServer()) return;
		
		//Counting score
		var score = GetNode<Control>("Score");
		switch (body.Position.X)
		{
			case < 1920/2F:
				var right = score.GetNode<Label>("Right");
				right.Text = (int.Parse(right.Text) + 1).ToString();
				ResetGame(GLOBAL.BALL_SPAWN_POINTS[1]);
				break;
			case > 1920/2F:
				var left = score.GetNode<Label>("Left");
				left.Text = (int.Parse(left.Text) + 1).ToString();
				ResetGame(GLOBAL.BALL_SPAWN_POINTS[0]);
				break;
		}
		
		//Check overtime
		var teams = new Array<Label>();
		foreach (var node in score.GetChildren())
			if (node is Label label)
				teams.Add(label);

		if (int.Parse(teams[0].Text) == 10 && int.Parse(teams[1].Text) == 10)
			return;
		
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
	
	[Rpc(CallLocal = true, TransferMode = MultiplayerPeer.TransferModeEnum.Reliable, TransferChannel = 2)]
	private void StopGame()
	{
		GetNode<StaticBody2D>("SideBorders").SetCollisionLayerValue(3, true);
		GetNode<StaticBody2D>("MidField").ProcessMode = ProcessModeEnum.Disabled;
		GetNode<Area2D>("Goals").ProcessMode = ProcessModeEnum.Disabled;
	}
	
	public override void _Input(InputEvent @event)
	{
		if (@event.IsActionPressed("ui_cancel"))
		{
			Multiplayer.MultiplayerPeer.Close();
			Input.MouseMode = Input.MouseModeEnum.Visible;
			GetTree().ChangeSceneToFile("res://Main Menu.tscn");
		}	
		
		if (@event.IsActionPressed("Restart Game") && Multiplayer.IsServer())
			StartGame();
	}
}
