using System;

namespace UltraPong;

using Godot;
using System.Text.Json;

public abstract class JsonFileAccess
{
    public static T Read<T>(string path) where T : new()
    {
        if (!FileAccess.FileExists(path)) return CreateNewFile(path + " does not exist.");
        
        using var file = FileAccess.Open(path, FileAccess.ModeFlags.Read);
        var content = file.GetAsText();

        if (string.IsNullOrWhiteSpace(content)) return CreateNewFile(path + " is empty.");
        
        try
        {
            return JsonSerializer.Deserialize<T>(content) ?? throw new InvalidOperationException();
        }
        catch (JsonException ex)
        {
            return CreateNewFile($"Failed to deserialize {path}: {ex.Message}.");
        }

        T CreateNewFile(string message)
        {
            GD.Print(message + " Creating new file.");
            var obj = new T();
            Write(path, obj);
            return obj;
        }
    }

    public static void Write<T>(string path, T obj)
    {
        try
        {
            using var file = FileAccess.Open(path, FileAccess.ModeFlags.Write);
            file.StoreString(JsonSerializer.Serialize(obj, new JsonSerializerOptions { WriteIndented = true }));
        }
        catch (Exception e)
        {
            GD.PrintErr($"Error writing to file: {path}. {e.Message}");
        }
 
    }
}