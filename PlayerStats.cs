namespace UltraPong;

using Godot;
using static Godot.Colors;

public class PlayerStats
{
    public Vector2 Size { get; set; } = new(30F, 150F);
    public float Speed { get; set; } = 430F;
    public float MaxRotation { get; set; } = 30F;
    public Vector2[] SpawnPoints { get; set; } = {new (320F, 360F), new(320F, 720F), new(1600F, 360F), new(1600F, 720F) };
    public Color[] ColorsArray { get; set; } =  {Red, Tomato, Blue, Aqua};
}