namespace UltraPong;
using Godot;

public partial class World : Node2D
{
	public override void _Input(InputEvent @event)
	{
		var worldHandler = GetNode<WorldHandler>("../Network/WorldHandler");
		
		if (@event.IsActionPressed("Restart Game") && Multiplayer.IsServer())
			worldHandler.StartGame();
		else if (@event.IsActionPressed("Options"))
		{
			if (GetNodeOrNull("Choose Team") != null) return;
			
			AddChild(GD.Load<PackedScene>("res://Choose Team.tscn").Instantiate<Control>());
			if (@event is InputEventKey)
				Input.MouseMode = Input.MouseModeEnum.Visible;
		}
		else if (@event.IsActionPressed("Show Labels"))
			foreach (Player player in GetNode("Players").GetChildren())
			{
				var nickname = player.GetNode<Label>("Nickname");
				nickname.Visible = !nickname.Visible;
			}
	}
}
