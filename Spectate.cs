using Godot;
using System;

public partial class Spectate : Button
{
    private void OnPressed()
    {
        GetParent().QueueFree();
    }
}
