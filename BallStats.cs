namespace UltraPong;

using Godot;
using System;
using System.Text.Json;
using System.Text.Json.Serialization;

public class BallStats
{
	public float Size { get; set; } = 21F;
	public float MaxSpeed { get; set; } = 1720F;
	public Vector2[] SpawnPoints { get; set; } = { new(384F, 540F), new(1536F, 540F), new(960F, 540F) };
}
