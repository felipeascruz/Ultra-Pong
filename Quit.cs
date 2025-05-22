using Godot;

namespace UltraPong;

public partial class Quit : Button
{
    private void OnPressed()
    {
        GetTree().Quit();
    }
}