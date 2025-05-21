using Godot;

namespace UltraPong;

public struct Device
{
    public Device(int number, char type)
    {
        Type = type;
        Number = number;
    }

    public int Number { get; set; }
    
    public char Type { get; set; }

    public override string ToString()
    {
        return Type.ToString() + Number;
    }
}