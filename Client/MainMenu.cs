namespace UltraPong;

using Godot;

public partial class MainMenu : Control
{
    private Button QuitButton => GetNode<Button>("Menu Schema/Quit");
    
    public override void _Ready()
    {
        QuitButton.Pressed += OnQuitPressed;
        
        GetTree().GetRoot().GetNodeOrNull<ENetManager>("ENetManager")?.QueueFree();
        
        if (OS.HasFeature("dedicated_server"))
            OnOnlinePressed();
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
    
    // Signaled through Online Button
    private void OnOnlinePressed()
    {
        GetTree().CallDeferred("change_scene_to_file", "res://Online Menu.tscn");
    }

    public override void _ExitTree()
    {
        QuitButton.Pressed -= OnQuitPressed;
    }
}