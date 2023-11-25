using Godot;
using static GLOBAL;

public partial class Player : CharacterBody2D
{
	private float _speed = PLAYER_SPEED;
	public Vector2 SpawnPoint { get; set; }

	private float 
		_width = PLAYER_WIDTH,
		_height = PLAYER_HEIGHT;

	[Export]
	public Color InitialColor { get; set; }
	
	public override void _EnterTree()
	{
		GetNode("InputSynchronizer").SetMultiplayerAuthority(int.Parse(Name));

		Position = SpawnPoint;
	}
	
	public override void _Ready()
	{
		var hitBox = (RectangleShape2D)GetNode<CollisionShape2D>("Collision").Shape;
		hitBox.Size = new Vector2(_width, _height);
		
		var rectangle = GetNode<ColorRect>("Rectangle");
		rectangle.Size = new Vector2(_width, _height);
		rectangle.PivotOffset = rectangle.Size / 2;
		rectangle.Position = -rectangle.Size / 2;
		rectangle.Color = InitialColor;
	}

	public override void _PhysicsProcess(double delta)
	{
		var input = (InputSynchronizer)GetNode<MultiplayerSynchronizer>("InputSynchronizer");
		
		_speed = PLAYER_SPEED;
		
		//Check boost
		if (input.Boosting)
		{
			_speed *= 3;
			SetCollisionLayerValue(1,false);
			SetCollisionMaskValue(1, false);
			SetCollisionMaskValue(2, false);
		}
		else
		{
			SetCollisionLayerValue(1, true);
			SetCollisionMaskValue(1, true);
			SetCollisionMaskValue(2, true);
		}

		Velocity = input.Direction * _speed;
		MoveAndSlide();
		
		//Apply impulse to Ball
		for (sbyte i = 0; i < GetSlideCollisionCount(); i++)
		{
			var c = GetSlideCollision(i);
			if (c.GetCollider() is RigidBody2D body)
				body.ApplyCentralImpulse(-c.GetNormal() *  _speed/80F);
		}
	}
}
