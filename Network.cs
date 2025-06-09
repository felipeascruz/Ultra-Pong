namespace UltraPong;

using Godot;

public partial class Network : Node
{
	private Node HolePuncher => GetNode("/root/HolePunch");
	
	public override void _Ready()
	{
	}
}