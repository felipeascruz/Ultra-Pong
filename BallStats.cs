namespace UltraPong;

using Godot;
using System;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Collections.Generic;

[JsonConverter(typeof(BallStatsJsonConverter))]
public class BallStats
{
	public float Size { get; set; } = 21F;
	public float MaxSpeed { get; set; } = 1720F;
	public Vector2[] SpawnPoints { get; set; } = { new(384F, 540F), new(1536F, 540F), new(960F, 540F) };

	public class BallStatsJsonConverter : JsonConverter<BallStats>
	{
		public override BallStats Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
		{
			using var doc = JsonDocument.ParseValue(ref reader);
			var root = doc.RootElement;
    
			var spawnPointsArray = root.GetProperty("SpawnPoints");
			var spawnPoints = new List<Vector2>();
			foreach (var point in spawnPointsArray.EnumerateArray())
			{
				float x = point.GetProperty("X").GetSingle();
				float y = point.GetProperty("Y").GetSingle();
				spawnPoints.Add(new Vector2(x, y));
			}

			return new BallStats
			{
				Size = root.GetProperty("Size").GetSingle(),
				MaxSpeed = root.GetProperty("MaxSpeed").GetSingle(),
				SpawnPoints = spawnPoints.ToArray()
			};

		}

		public override void Write(Utf8JsonWriter writer, BallStats value, JsonSerializerOptions options)
		{
			writer.WriteStartObject();

			writer.WriteNumber("Size", value.Size);

			writer.WriteNumber("MaxSpeed", value.MaxSpeed);

			writer.WritePropertyName("SpawnPoints");
			writer.WriteStartArray();
			foreach (var spawnPoint in value.SpawnPoints)
			{
				writer.WriteStartObject();
				writer.WriteNumber("X", spawnPoint.X);
				writer.WriteNumber("Y", spawnPoint.Y);
				writer.WriteEndObject();
			}
			writer.WriteEndArray();
        
			writer.WriteEndObject();
		}
	}

}
