namespace UltraPong;
using Godot;

public partial class PlayerStats : Resource
{
	[Export] public Vector2 Size { get; set; } = new(30F, 150F);
	[Export] public float Speed { get; set; } = 430F;
	[Export] public float MaxRotation { get; set; } = 30F;
	[Export] public float Sensitivity { get; set; } = 0.006F;
	[Export] public Vector2[] SpawnPoints { get; set; } = {new(320F,360F), new(320F,720F), new(1600F,360F), new(1600F, 720F)};
	[Export] public Color[] ColorsArray { get; set; } = {Colors.Red, Colors.Tomato, Colors.Blue, Colors.Aqua};
	
	public PlayerStats() : this(Vector2.Zero, 0,  0, 0, null, null) {}

	public PlayerStats(Vector2 size, float speed, float maxRotation, float sensitivity, Vector2[] spawnPoints, Color[] colorsArray)
	{
		Size = size;
		Speed = speed;
		MaxRotation = maxRotation;
		Sensitivity = sensitivity;
		SpawnPoints = spawnPoints;
		ColorsArray = colorsArray;
	}
}
