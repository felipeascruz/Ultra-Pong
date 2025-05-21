using Godot;
using System;

public partial class Quit : Button
{
    private void OnPressed()
    {
        GetTree().Quit();
    }
}
