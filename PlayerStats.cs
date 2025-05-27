namespace UltraPong;
using Godot;

public abstract class PlayerStats
{
	public Vector2 Size { get; set; }
	public float Speed { get; set; }
	public float MaxRotation { get; set; }
	public Vector2[] SpawnPoints { get; set; }
	public Color[] ColorsArray { get; set; }
}
