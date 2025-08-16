namespace UltraPong;
using Godot;

public partial class Leave : Button
{
    private void OnPressed()
    {
        GetNode<WorldHandler>("../../../Network/WorldHandler").Disconnect();
    }
}
