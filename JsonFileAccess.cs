namespace UltraPong;

using Godot;
using static System.Text.Json.JsonSerializer;

public abstract class JsonFileAccess
{
    public static T Read<T>(string path)
    {
        using var file = FileAccess.Open(path, FileAccess.ModeFlags.Read);
        return Deserialize<T>(file.GetAsText());
    }

    public static void Write<T>(string path, T obj)
    {
        using var file = FileAccess.Open(path, FileAccess.ModeFlags.Write);
        file.StoreString(Serialize(obj));
    }
}