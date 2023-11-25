using Godot;

public partial class GLOBAL : Node
{
	//Global Constants
	public const float PLAYER_WIDTH = 30F, PLAYER_HEIGHT = 150F, PLAYER_SPEED = 430F;
	public const int PORT = 7777;
	public static readonly Vector2[] PLAYERS_SPAWN_POINTS = {
		new(1920F/6,1080F/3),
		new(1920F/6,2*1080F/3),
		new(5*1920F/6,1080F/3),
		new(5*1920F/6,2*1080F/3)
	};
	public static readonly Vector2[] BALL_SPAWN_POINTS = {new(1920F/5, 1080F/2),new(4*1920F/5, 1080F/2)};
	public const float SENSITIVITY = 0.007F;
	public const float MAX_ROTATION = 0.28F;
	public static readonly Color[] ColorsArray = {Colors.Red, Colors.Tomato, Colors.Blue, Colors.Aqua};
}
