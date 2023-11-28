using Godot;

public partial class Global : Node
{
	public const int Port = 7777;
	internal abstract class Player
	{
		public static readonly Vector2 Size = new(30F, 150F);
		public const float Speed = 430F;
		public static readonly Vector2[] SpawnPoints = {
			new(1920F/6,1080F/3),
			new(1920F/6,2*1080F/3),
			new(5*1920F/6,1080F/3),
			new(5*1920F/6,2*1080F/3)
		};
		public static readonly Color[] ColorsArray = {Colors.Red, Colors.Tomato, Colors.Blue, Colors.Aqua};
		public const float Sensitivity = 0.007F;
		public const float MaxRotation = 0.28F;
	}

	internal abstract class Ball
	{
		public static readonly Vector2[] SpawnPoints =
		{
			new(1920F/5, 1080F/2),
			new(4*1920F/5, 1080F/2)
		};
	}
}