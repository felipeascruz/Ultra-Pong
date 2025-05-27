namespace UltraPong;

using Godot;

public abstract class BallStats
{
	public float Size { get; set; }
	public float MaxSpeed { get; set; }
	public Vector2[] SpawnPoints { get; set; }
}
