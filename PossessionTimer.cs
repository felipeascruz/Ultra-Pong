using System.Globalization;
using Godot;

namespace UltraPong;

public partial class PossessionTimer : Timer
{
    private Label Label => GetNode<Label>("../Time Display");
    
    public override void _PhysicsProcess(double delta)
    {
        Label.Visible = TimeLeft > 0D;
        Label.Text = TimeLeft.ToString("F",CultureInfo.InvariantCulture);
    }
    
    //Signal
    private void OnTimeout()
    {
        var ballSpawnPoints = GD.Load<BallStats>("res://BallStats.tres").SpawnPoints;
        GetNode<WorldHandler>("../../Network/WorldHandler").ResetGame(Label.Position.X < 960 ? ballSpawnPoints[1] : ballSpawnPoints[0]);
    }
}