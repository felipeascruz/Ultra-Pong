using Godot;

namespace UltraPong;

public struct Device(int number, char type)
{
    public int Number { get; set; } = number;

    public char Type { get; set; } = type;

    public override string ToString()
    {
        return Type.ToString() + Number;
    }
}