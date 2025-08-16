namespace UltraPong;

using Godot;

public partial class MainMenu : Control
{
    private Button QuitButton => GetNode<Button>("Quit");
    
    public override void _Ready()
    {
        QuitButton.Pressed += OnQuitPressed;
        
        GetTree().GetRoot().GetNodeOrNull<ENetManager>("ENetManager")?.QueueFree();
    }
    
    // Signaled through Quit Button
    private void OnQuitPressed()
    {
        GetTree().Quit();
    }
    
    // Signaled through Local Button
    private void OnLocalPressed()
    {
        GetTree().CallDeferred("change_scene_to_file", "res://Local Menu.tscn");
    }
    
    // Signaled through Local Button
    private void OnOnlinePressed()
    {
        GetTree().CallDeferred("change_scene_to_file", "res://Online Menu.tscn");
    }

    public override void _ExitTree()
    {
        QuitButton.Pressed -= OnQuitPressed;
    }
}