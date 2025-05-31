namespace UltraPong;

using Godot;
using static Godot.Colors;
using System;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Collections.Generic;

[JsonConverter(typeof(PlayerStatsJsonConverter))]
public class PlayerStats
{
    public Vector2 Size { get; init; } = new(30F, 150F);
    public float Speed { get; init; } = 430F;
    public float MaxRotation { get; init; } = 30F;
    public Vector2[] SpawnPoints { get; init; } = {new (320F, 360F), new(320F, 720F), new(1600F, 360F), new(1600F, 720F) };
    public Color[] ColorsArray { get; set; } =  {Red, Tomato, Blue, Aqua};
}

public class PlayerStatsJsonConverter : JsonConverter<PlayerStats>
{
    public override PlayerStats Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        using var doc = JsonDocument.ParseValue(ref reader);
        var root = doc.RootElement;

        var spawnPointsArray = root.GetProperty("SpawnPoints");
        var spawnPoints = new List<Vector2>();
        foreach (var element in spawnPointsArray.EnumerateArray())
        {
            float x = element.GetProperty("X").GetSingle();
            float y = element.GetProperty("Y").GetSingle();
            spawnPoints.Add(new Vector2(x, y));
        }
        
        var colorsArray = root.GetProperty("ColorsArray");
        var colors = new List<Color>();
        foreach (var element in colorsArray.EnumerateArray())
            colors.Add(element.Deserialize<Color>(options));
        
        var size = root.GetProperty("Size");
        return new PlayerStats
        {
            Size = new Vector2(size.GetProperty("X").GetSingle(), size.GetProperty("Y").GetSingle()),
            Speed = root.GetProperty("Speed").GetSingle(),
            MaxRotation = root.GetProperty("MaxRotation").GetSingle(),
            SpawnPoints = spawnPoints.ToArray(),
            ColorsArray = colors.ToArray()
        };
    }

    public override void Write(Utf8JsonWriter writer, PlayerStats value, JsonSerializerOptions options)
    {
        writer.WriteStartObject();

        writer.WriteStartObject("Size");
        writer.WriteNumber("X", value.Size.X);
        writer.WriteNumber("Y", value.Size.Y);
        writer.WriteEndObject();

        writer.WriteNumber("Speed", value.Speed);

        writer.WriteNumber("MaxRotation", value.MaxRotation);

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

        writer.WritePropertyName("ColorsArray");
        JsonSerializer.Serialize(writer, value.ColorsArray, options);
        
        writer.WriteEndObject();
    }
}