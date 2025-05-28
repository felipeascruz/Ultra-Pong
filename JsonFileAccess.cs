using System;

namespace UltraPong;

using Godot;
using System.Text.Json;

public abstract class JsonFileAccess
{
    public static T Read<T>(string path) where T : new()
    {
        GD.Print("reading from " + path);
        using var file = FileAccess.Open(path, FileAccess.ModeFlags.WriteRead);
        
        if (file.GetAsText().Length != 0) return JsonSerializer.Deserialize<T>(file.GetAsText());
        
        var obj = new T();
        Write(path, obj);
        return obj;
    }

    public static void Write<T>(string path, T obj)
    {   
        GD.Print("writing into " + path);
        using var file = FileAccess.Open(path, FileAccess.ModeFlags.Write);
        file.StoreString(JsonSerializer.Serialize(obj, new JsonSerializerOptions { WriteIndented = true }));
    }
}