using System;

namespace UltraPong;

using Godot;
using System.Text.Json;

public abstract class JsonFileAccess
{
    public static T Read<T>(string path) where T : new()
    {
        using var file = FileAccess.Open(path, FileAccess.ModeFlags.WriteRead);
        
        if (file.GetLength() != 0) {GD.Print("File exists");return JsonSerializer.Deserialize<T>(file.GetAsText());}
        
        var obj = new T();
        Write(path, obj);
        return obj;
    }

    public static void Write<T>(string path, T obj)
    {   
        using var file = FileAccess.Open(path, FileAccess.ModeFlags.Write);
        file.StoreString(JsonSerializer.Serialize(obj, new JsonSerializerOptions { WriteIndented = true }));
    }
}