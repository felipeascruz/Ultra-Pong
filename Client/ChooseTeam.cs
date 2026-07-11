namespace UltraPong;
using Godot;

public partial class ChooseTeam : ColorRect
{
	public override void _Ready()
	{
		var playerStats = JsonFileAccess.Read<PlayerStats>("user://playerStats.json");
		
		Size = playerStats.Size;
		PivotOffset = Size / 2;
		Color = playerStats.ColorsArray[int.Parse(Name)];
		GlobalPosition = playerStats.SpawnPoints[int.Parse(Name)] - PivotOffset;

		Connect("mouse_entered",new Callable(this, nameof(MouseEntered)));
	}
	
	public override void _GuiInput(InputEvent @event)
	{
		if (@event is InputEventMouseButton { Pressed: true, ButtonIndex: MouseButton.Left } || @event.IsActionPressed("ui_accept"))
			GetNode<WorldHandler>("../../../Network/WorldHandler").SelectTeam(byte.Parse(Name),
				new Device(0, 'K'));
		else if (@event is InputEventJoypadButton { Pressed: true, ButtonIndex: JoyButton.A })
			GetNode<WorldHandler>("../../../Network/WorldHandler").SelectTeam(byte.Parse(Name),
				new Device(@event.Device, 'C'));
	}

	public override void _Process(double delta)
	{
		foreach (Player player in GetNode("../../Players").GetChildren())
			if (player.Number == int.Parse(Name))
				QueueFree();

		Scale = GetViewport().GuiGetFocusOwner().Name == Name ? new Vector2(1.1F, 1.1F) : Vector2.One;
	}

	private new void MouseEntered()
	{
		GrabFocus();
	}
}
