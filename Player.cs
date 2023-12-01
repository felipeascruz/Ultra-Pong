using Godot;
using static Godot.Input;

public partial class Player : CharacterBody2D
{
	[Export(PropertyHint.Range, "0,3,")]
	public int Number { get; set; }

	public Vector2 SpawnPoint { get; private set; }

	public Color InitialColor { get; private set; }
	
	private bool IsLocalPlayer => Multiplayer.GetUniqueId().ToString() == Name;

	private float _rotationDirection = -1F;

	public bool Boosting { get; set; }
	
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

		if (IsLocalPlayer)
			MouseMode = MouseModeEnum.Captured;
		SetProcess(IsLocalPlayer);
        GetNode<Node2D>("RotationIndicator").Visible = IsLocalPlayer;
		SetProcessInput(IsLocalPlayer);
	}

	public override void _Process(double delta)
	{
		var color = new Color { A = Mathf.Abs(Mathf.Cos(Rotation/2F)) };
		GetNode<Sprite2D>("RotationIndicator/Down").Modulate = color;

		color.A = Mathf.Abs(Mathf.Sin(Rotation/2F));
		GetNode<Sprite2D>("RotationIndicator/Up").Modulate = color;
	}

	public override void _PhysicsProcess(double delta)
	{
		if (IsLocalPlayer)
		{
			Boosting = IsActionPressed("Boost");
            Velocity = GetVector("MoveLeft", "MoveRight", "MoveUp", "MoveDown") * Global.Player.Speed;
        }

        //Check boost
        var rectangle = GetNode<ColorRect>("Rectangle");
        if (Boosting)
        {
			rectangle.Color = Colors.Yellow;
            Velocity *= 3;
            SetCollisionLayerValue(1, false);
            SetCollisionMaskValue(1, false);
            SetCollisionMaskValue(2, false);
        }
        else
        {
			rectangle.Color = InitialColor;
            SetCollisionLayerValue(1, true);
            SetCollisionMaskValue(1, true);
            SetCollisionMaskValue(2, true);
        }

        MoveAndSlide();
		
		//Apply impulse to Ball
		for (sbyte i = 0; i < GetSlideCollisionCount(); i++)
		{
			var c = GetSlideCollision(i);
			if (c.GetCollider() is RigidBody2D body)
				body.ApplyImpulse(-c.GetNormal() * Global.Player.Speed/80F, c.GetPosition() - body.GlobalPosition);
		}
	}

	public override void _UnhandledInput(InputEvent @event)
	{
        if (@event.IsActionPressed("ChangeRotation"))
        {
            _rotationDirection *= -1;
            GetParent().GetNode<Node2D>("RotationIndicator").RotationDegrees += 180F;
			return;
        }

        var rotation = 0F;
        if (@event is InputEventMouseMotion motion)
		{
            rotation = _rotationDirection * motion.Relative.X * Global.Player.Sensitivity;
            Rotate(rotation);	
		}

        GetNode<PlayersHandler>("/root/Main/Network/PlayersHandler").FetchInputWrapper
		(
			new PlayersHandler.State(Position, Rotation), 
			new[]{IsActionPressed("MoveLeft"), IsActionPressed("MoveDown"), IsActionPressed("MoveUp"), IsActionPressed("MoveDown")},
			IsActionPressed("Boost"), rotation
		);
	}
}
