namespace UltraPong;

using Godot;
using static Godot.Input;
using Color = Godot.Color;

public partial class Player : CharacterBody2D
{
	private static readonly PlayerStats Stats = JsonFileAccess.Read<PlayerStats>("res://playerStats.json");
	
	private static readonly float Sensitivity = JsonFileAccess.Read<UserStats>("user://userStats.json").Sensitivity;

	public Device Device;
	
	public uint InputStamp { get; set; }
	public byte Number { get; set; }

	public Vector2 SpawnPoint => Stats.SpawnPoints[Number];

	private Color InitialColor => Stats.ColorsArray[Number];

	public int Id { get; set; }
	
	public bool IsLocalPlayer => Multiplayer.GetUniqueId() == Id;

	private sbyte _rotationDirection = -1;
	private float _currentRotation;
	private float _targetRotation;

	public Vector2 Direction { get; set; } = Vector2.Zero;
	public bool Boosting { get; set; }
	public float RotateTo { get; set; }
	public double Overtime { get; set; }
	
	public override void _Ready()
	{
		Name = Id.ToString() + Device;
		Position = SpawnPoint;
		
		var hitBox = (RectangleShape2D)GetNode<CollisionShape2D>("Collision").Shape;
		hitBox.Size = Stats.Size;
		
		var rectangle = GetNode<ColorRect>("Rectangle");
		rectangle.Size = Stats.Size;
		rectangle.PivotOffset = rectangle.Size / 2;
		rectangle.Position = -rectangle.Size / 2;
		rectangle.Color = InitialColor;

		GetNode<Label>("Nickname").TopLevel = true;
		
		_currentRotation = Rotation;
		
		SetProcessUnhandledInput(IsLocalPlayer);
		SetPhysicsProcess(IsLocalPlayer || Multiplayer.IsServer());
		
		if (!IsLocalPlayer) return;
		if (DisplayServer.WindowGetMode() == DisplayServer.WindowMode.ExclusiveFullscreen)
			MouseMode = MouseModeEnum.Captured;

		var indicatorModel = new Sprite2D
			{ Texture = GD.Load<Texture2D>("BallSprite.png"), Modulate = new Color{A = 1}, Scale = new Vector2(0.01f, 0.01f) };
		if (Device.Type == 'K')
		{
			var rotationIndicator = new Node2D{Name = "Rotation Indicator"};
			
			indicatorModel.Position = new Vector2(0f, -Stats.Size.Y/2.5f);
			indicatorModel.Name = "Up";
			rotationIndicator.AddChild(indicatorModel, true);

			var downIndicator = new Sprite2D{Name = "Down", Position = new Vector2(0f, Stats.Size.Y/2.5f),
				Texture = indicatorModel.Texture, Modulate  = indicatorModel.Modulate, Scale = indicatorModel.Scale};
			rotationIndicator.AddChild(downIndicator, true);
			
			AddChild(rotationIndicator, true);
			return;
		}

		indicatorModel.Name = "Rotation Indicator";
		indicatorModel.TopLevel = true;
		AddChild(indicatorModel, true);
	}

	public override void _Process(double delta)
	{
		var nickname = GetNode<Label>("Nickname");
		nickname.Position = new Vector2(-nickname.Size.X/2, nickname.Size.Y/2 - Stats.Size.Y) + Position;

		//Check Overtime
		if (Overtime > 0)
		{
			Scale -= new Vector2(0f, 0.05f * (float)delta);
			Overtime -= delta;
			SetPhysicsProcess(false);
		}
		else
			SetPhysicsProcess(true);
		
		//Process rotation indicator
		if (!IsLocalPlayer)
			return;
		if (Device.Type == 'K')
		{
			var color = new Color { A = Mathf.Abs(Mathf.Cos(Rotation / 2f)) };
			GetNode<Sprite2D>("Rotation Indicator/Down").Modulate = color;

			color.A = Mathf.Abs(Mathf.Sin(Rotation / 2f));
			GetNode<Sprite2D>("Rotation Indicator/Up").Modulate = color;
			return;
		}
		
		GetNode<Sprite2D>("Rotation Indicator").GlobalPosition =
			Position + 
			GetVector("Rotate Left" + Device, "Rotate Right" + Device, "Rotate Up" + Device, "Rotate Down" + Device) * 
			Stats.Size.Y/2.5f;
	}

	public override void _PhysicsProcess(double delta)
	{
		float speed;
		//Check boost
		{
			var rectangle = GetNode<ColorRect>("Rectangle");
			if (Boosting)
			{
				rectangle.Color = Colors.Yellow;
				speed = Stats.Speed * 3;
				SetCollisionLayerValue(1, false);
				SetCollisionMaskValue(1, false);
				SetCollisionMaskValue(2, false);
			}
			else
			{
				rectangle.Color = InitialColor;
				speed = Stats.Speed;
				SetCollisionLayerValue(1, true);
				SetCollisionMaskValue(1, true);
				SetCollisionMaskValue(2, true);
			}
		}

		Velocity = Direction * speed;
		MoveAndSlide();
		
		HandleRotation(delta);

		//Apply impulse to Ball
		for (sbyte i = 0; i < GetSlideCollisionCount(); i++)
		{
			var c = GetSlideCollision(i);
			if (c.GetCollider() is not Ball ball) continue;

			var sfx = ball.GetNode<AudioStreamPlayer2D>("SoundFX");
			sfx.PitchScale = 0.5f + ball.LinearVelocity.Length() / Ball.Stats.MaxSpeed;
			sfx.Play();
				
			ball.ApplyImpulse(-c.GetNormal() * Stats.Speed / 80f, c.GetPosition() - ball.GlobalPosition);

			if (!Multiplayer.IsServer()) 
				continue;
			var possessionTimer = GetNodeOrNull<PossessionTimer>("../../Possession Timer");
			if (possessionTimer is null) 
				continue;
			var timeDisplay = GetNode<Label>("../../Time Display");
			switch (timeDisplay.Position.X)
			{
				case < 1f when Position.X > 960f:
					timeDisplay.Position = new Vector2(960f, timeDisplay.Position.Y);
					possessionTimer.Stop();
					possessionTimer.Start();
					break;
				case > 959f when Position.X < 960f:
					timeDisplay.Position = new Vector2(0f, timeDisplay.Position.Y);
					possessionTimer.Stop();
					possessionTimer.Start();
					break;
			}
			break;
		}
	}
	
	private void HandleRotation(double delta)
	{
		var maxRotationThisFrame = Stats.MaxRotation * (float)delta;
        
		if (Device.Type == 'K')
		{
			var clampedRotation = Mathf.Clamp(RotateTo, -maxRotationThisFrame, maxRotationThisFrame);
			var newRotation = _currentRotation + clampedRotation;
			
			newRotation = Mathf.Clamp(newRotation, -Stats.MaxRotation, Stats.MaxRotation);
            
			var rotationDelta = newRotation - _currentRotation;
			Rotate(rotationDelta);
			_currentRotation = newRotation;
		}
		else
		{
			if (RotateTo != 0f)
				_targetRotation = Mathf.Clamp(RotateTo, -Stats.MaxRotation, Stats.MaxRotation);
			
			var rotationDiff = Mathf.AngleDifference(_currentRotation, _targetRotation);
			var maxChange = maxRotationThisFrame;
			var change = Mathf.Clamp(rotationDiff, -maxChange, maxChange);
            
			Rotate(change);
			_currentRotation += change;
		}
        
		// Normalizar a rotação atual para evitar valores muito grandes
		_currentRotation = NormalizeAngle(_currentRotation);
		RotateTo = 0f;
	}

	private float NormalizeAngle(float angle)
	{
		while (angle > Mathf.Pi)
			angle -= 2 * Mathf.Pi;
		while (angle < -Mathf.Pi)
			angle += 2 * Mathf.Pi;
		return angle;
	}

	
	
	public override void _UnhandledInput(InputEvent @event)
	{
		if (@event.Device != Device.Number)
			return;
		
		Boosting = IsActionPressed("Boost" + Device);
		Direction = GetVector("Move Left" + Device, "Move Right" + Device, "Move Up" + Device, "Move Down" + Device);
		
		if (@event.IsActionPressed("Change Rotation"))
		{
			_rotationDirection *= -1;
			GetNode<Node2D>("Rotation Indicator").RotationDegrees += 180f;
			return;
		}

		var rotation = 0f;
		switch (@event)
		{
			case InputEventMouseMotion mouseMotion when Device.Type == 'K':
				rotation = _rotationDirection * mouseMotion.Relative.X * Sensitivity;
				RotateTo = rotation;
				break;
			case InputEventJoypadMotion when Device.Type == 'C':
			{
				var to = GetVector("Rotate Left" + Device, "Rotate Right" + Device, "Rotate Up" + Device, "Rotate Down" + Device);
				if (to.Length() >= 1f)
				{
					var targetAngle = Mathf.Pi / 2 + to.Angle();
					RotateTo = targetAngle;
				}
				break;
			}
		}

		
		if (!Multiplayer.IsServer())
			GetNode<PlayersHandler>("../../../Network/PlayersHandler").FetchInputWrapper
			(
				Name,
				new PlayersHandler.State(Position, Rotation, InputStamp++), 
				GetVector("Move Left" + Device, "Move Right" + Device, "Move Up" + Device, "Move Down" + Device), 
				IsActionPressed("Boost" + Device), rotation
			);
	}
}
