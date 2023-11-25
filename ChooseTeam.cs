using Godot;

public partial class ChooseTeam : ColorRect
{
	public override void _Ready()
	{
		Size = Global.Player.Size;
		PivotOffset = Size / 2;
		Color = Global.Player.ColorsArray[int.Parse(Name)];
		GlobalPosition = Global.Player.SpawnPoints[int.Parse(Name)] - PivotOffset;
	}
	
	public override void _GuiInput(InputEvent @event)
	{
		if (@event is InputEventMouseButton mb)
		{
			if (mb.ButtonIndex == MouseButton.Left && mb.Pressed)
			{
				GetNode<Main>("/root/World").SelectTeam(Multiplayer.GetUniqueId().ToString(), int.Parse(Name));
				GetParent().QueueFree();
			}
		}
	}

	public override void _Process(double delta)
	{
		foreach (var node in GetNode("/root/World/Players").GetChildren())
		{
			var player = (Player)node;
			if (player.Number == int.Parse(Name))
				QueueFree();
		}
	}
}
