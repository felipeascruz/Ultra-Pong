namespace UltraPong;

using Godot;

public partial class BallStats : Resource
{
	[Export] public float Size { get; set; } = 21F;
	[Export] public float MaxSpeed { get; set; } = 1720F;
	[Export] public Vector2[] SpawnPoints { get; set; } = { new(384F, 540F), new(1536F, 540F), new (960F, 540F)};
	
	
	public BallStats() : this(0, 0, null){}

	public BallStats(float size, float maxSpeed, Vector2[] spawnPoints)
	{
		Size = size;
		MaxSpeed = maxSpeed;
		SpawnPoints = spawnPoints;
	}
}
