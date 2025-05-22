namespace UltraPong;
using Godot;

public partial class PlayerStats
{
	public Vector2 Size { get; init; }
	public float Speed { get; init; }
	public float MaxRotation { get; init; }
	public Vector2[] SpawnPoints { get; init; }
	public Color[] ColorsArray { get; init; }
}
