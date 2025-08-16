namespace UltraPong;
using Godot;

public partial class Spectate : Button
{
    public override void _Ready()
    {
        GrabFocus();
    }

    public override void _GuiInput(InputEvent @event)
    {
        var name = Multiplayer.GetUniqueId().ToString();
        if (@event is InputEventMouseButton { Pressed: true, ButtonIndex: MouseButton.Left } ||
            @event.IsActionPressed("ui_accept"))
            name += 'K';
        else if (@event is InputEventJoypadButton { Pressed: true, ButtonIndex: JoyButton.A })
            name += 'C';
        else
            return;
        name += @event.Device;
        
        GetNode<WorldHandler>("../../../Network/WorldHandler").DespawnWrapper(name);
        
        GetParent().QueueFree();
    }
}
