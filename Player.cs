using Godot;

public partial class Player : CharacterBody2D
{
	[Export(PropertyHint.Range, "0,3,")]
	public int Number { get; set; }

	private float _speed = Global.Player.Speed;
	public Vector2 SpawnPoint { get; private set; }

	public Color InitialColor { get; private set; }
	
	private bool IsLocalPlayer => Multiplayer.GetUniqueId().ToString() == Name;
	
	public override void _Ready()
	{
		SpawnPoint = Global.Player.SpawnPoints[Number];
		InitialColor = Global.Player.ColorsArray[Number];
		Position = SpawnPoint;
		
		var hitBox = (RectangleShape2D)GetNode<CollisionShape2D>("Collision").Shape;
		hitBox.Size = Global.Player.Size;
		
		var rectangle = GetNode<ColorRect>("Rectangle");
		rectangle.Size = Global.Player.Size;
		rectangle.PivotOffset = rectangle.Size / 2;
		rectangle.Position = -rectangle.Size / 2;
		rectangle.Color = InitialColor;
		
		switch (IsLocalPlayer)
		{
			case false:
				ProcessThreadGroupOrder = 1;
				SetProcess(false);
				break;
			//Add rotation indicator
			case true:
				GetNode<Node2D>("RotationIndicator").Visible = true;
				break;
		}
	}

	public override void _Process(double delta)
	{
		var color = new Color { A = Mathf.Abs(Mathf.Cos(Rotation/2F)) };
		GetNode<Sprite2D>("RotationIndicator/Down").Modulate = color;

		color.A = Mathf.Abs(Mathf.Sin(Rotation/2F));
		GetNode<Sprite2D>("RotationIndicator/Up").Modulate = color;
	}

	/*public override void _PhysicsProcess(double delta)
	{
		var input = (InputSynchronizer)GetNode<MultiplayerSynchronizer>("InputSynchronizer");
		
		_speed = Global.Player.Speed;
		
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
				body.ApplyImpulse(-c.GetNormal() * _speed/80F, c.GetPosition() - body.GlobalPosition);
		}
	}*/
}
