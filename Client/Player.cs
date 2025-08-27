namespace UltraPong;

using Godot;
using static Godot.Input;
using Color = Godot.Color;

public partial class Player : CharacterBody2D
{
	// Cache nodes for optimization
	private PlayersHandler? _playersHandler;
	private Label? _nicknameNode;
	private ColorRect? _rectangleNode;
	private Label? _timeDisplayNode;
	private Sprite2D? _rotationIndicatorDown;
	private Sprite2D? _rotationIndicatorUp;
	private Node2D? _singleRotationIndicator;
	private AudioStreamPlayer2D? _ballsfx;
	
	// Cache strings concatenation
	private string? _moveLeftAction;
	private string? _moveRightAction;
	private string? _moveUpAction;
	private string? _moveDownAction;
	private string? _boostAction;
	private string? _rotateLeftAction;
	private string? _rotateRightAction;
	private string? _rotateUpAction;
	private string? _rotateDownAction;
	
	private bool _wasBoosting;
	private float _currentSpeed = Stats.Speed;

	
	private static readonly PlayerStats Stats = JsonFileAccess.Read<PlayerStats>("res://playerStats.json");
	
	private static readonly float Sensitivity = JsonFileAccess.Read<UserStats>("user://userStats.json").Sensitivity/100F;
	
	public Device Device;
	
	private PlayersHandler.State _cachedState;
	
	public uint InputStamp { get; set; }
	public byte Number { get; set; }

	public Vector2 SpawnPoint => Stats.SpawnPoints[Number];

	private Color InitialColor => Stats.ColorsArray[Number];

	public int Id { get; set; }
	
	public bool IsLocalPlayer => Multiplayer.GetUniqueId() == Id;

	private sbyte _rotationDirection = -1;

	public Vector2 Direction { get; set; } = Vector2.Zero;
	public bool Boosting { get; set; }
	public float RotateToAmount { get; set; }
	public double Overtime { get; set; }
	
	public override void _Ready()
	{
		// Cache Nodes
		_playersHandler = GetNode<PlayersHandler>("../../../Network/PlayersHandler");
		_nicknameNode = GetNode<Label>("Nickname");
		_rectangleNode = GetNode<ColorRect>("Rectangle");
		_timeDisplayNode = GetNode<Label>("../../Time Display");
		_ballsfx = GetNode<AudioStreamPlayer2D>("../../Ball/SoundFX");
		
		// Cache action strings
		_moveLeftAction = "Move Left" + Device;
		_moveRightAction = "Move Right" + Device;
		_moveUpAction = "Move Up" + Device;
		_moveDownAction = "Move Down" + Device;
		_boostAction = "Boost" + Device;
		_rotateLeftAction = "Rotate Left" + Device;
		_rotateRightAction = "Rotate Right" + Device;
		_rotateUpAction = "Rotate Up" + Device;
		_rotateDownAction = "Rotate Down" + Device;

		
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
		
		SetProcessUnhandledInput(IsLocalPlayer);
		SetPhysicsProcess(IsLocalPlayer || Multiplayer.IsServer());
		
		if (!IsLocalPlayer) return;
		
		if (DisplayServer.WindowGetMode() is 
		    DisplayServer.WindowMode.ExclusiveFullscreen or DisplayServer.WindowMode.Fullscreen)
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
		}
		else 
		{
			indicatorModel.Name = "Rotation Indicator";
			indicatorModel.TopLevel = true;
			AddChild(indicatorModel, true);
		}
		
		_rotationIndicatorDown = GetNode<Sprite2D>("Rotation Indicator/Down");
		_rotationIndicatorUp = GetNode<Sprite2D>("Rotation Indicator/Up");
		_singleRotationIndicator = GetNode<Node2D>("Rotation Indicator");
	}

	public override void _Process(double delta)
	{
		if (_nicknameNode is null)
		{
			GD.PrintErr("Nickname node is null");
			return;
		}
		
		_nicknameNode.Position = new Vector2(-_nicknameNode.Size.X/2, _nicknameNode.Size.Y/2 - Stats.Size.Y) + Position;
		
		//TODO: better implement Overtime
		//Check Overtime
		/*if (Overtime > 0)
		{
			Scale -= new Vector2(0f, 0.05f * (float)delta);
			Overtime -= delta;
			SetPhysicsProcess(false);
		}
		else
			SetPhysicsProcess(true);*/
		
		//Process rotation indicator
		if (!IsLocalPlayer)
			return;
		if (Device.Type == 'K')
		{
			if (_rotationIndicatorDown is null || _rotationIndicatorUp is null)
			{
				GD.PrintErr("Rotation indicator node is null");
				return;
			}
			
			var color = new Color { A = Mathf.Abs(Mathf.Cos(Rotation / 2F)) };
			_rotationIndicatorDown.Modulate = color;

			color.A = Mathf.Abs(Mathf.Sin(Rotation / 2F));
			_rotationIndicatorUp.Modulate = color;
			return;
		}
		
		if (_singleRotationIndicator is null)
		{
			GD.PrintErr("Rotation indicator node is null");
			return;
		}
		
		_singleRotationIndicator.GlobalPosition =
			Position + 
			GetVector("Rotate Left" + Device, "Rotate Right" + Device, "Rotate Up" + Device, "Rotate Down" + Device) * 
			Stats.Size.Y/2.5f;
	}

	public override void _PhysicsProcess(double delta)
	{
		if (Boosting != _wasBoosting)
		{
			if (_rectangleNode is null)
			{
				GD.PrintErr("Rectangle node is null");
				return;
			}
			
			if (Boosting)
			{
				_rectangleNode.Color = Colors.Yellow;
				_currentSpeed = Stats.Speed * 3;
				SetCollisionLayerValue(1, false);
				SetCollisionMaskValue(1, false);
				SetCollisionMaskValue(2, false);
			}
			else
			{
				_rectangleNode.Color = InitialColor;
				_currentSpeed = Stats.Speed;
				SetCollisionLayerValue(1, true);
				SetCollisionMaskValue(1, true);
				SetCollisionMaskValue(2, true);
			}
			_wasBoosting = Boosting;
		}

		Velocity = Direction * _currentSpeed;
		MoveAndSlide();
		
		var maxRotationThisFrame = Stats.MaxRotation * (float)delta;
		var clampedRotation = Mathf.Clamp(RotateToAmount, -maxRotationThisFrame, maxRotationThisFrame);
		
		Rotate(clampedRotation);
				
		RotateToAmount = 0f;

		//Apply impulse to Ball
		for (sbyte i = 0; i < GetSlideCollisionCount(); i++)
		{
			var c = GetSlideCollision(i);
			if (c.GetCollider() is not Ball ball) continue;
			
			if (_ballsfx is null)
			{
				GD.PrintErr("Ball SFX node is null");
				return;
			}
			
			_ballsfx.PitchScale = 0.5F + ball.LinearVelocity.Length() / Ball.Stats.MaxSpeed;
			_ballsfx.Play();
				
			ball.ApplyImpulse(-c.GetNormal() * Stats.Speed / 80f, c.GetPosition() - ball.GlobalPosition);

			if (!Multiplayer.IsServer()) 
				continue;
			var possessionTimer = GetNodeOrNull<PossessionTimer>("../../Possession Timer");
			if (possessionTimer is null) 
				continue;
			
			if (_timeDisplayNode is null)
			{
				GD.PrintErr("Time Display node is null");
				return;
			}
			
			switch (_timeDisplayNode.Position.X)
			{
				case < 1F when Position.X > 960F:
					_timeDisplayNode.Position = new Vector2(960F, _timeDisplayNode.Position.Y);
					possessionTimer.Stop();
					possessionTimer.Start();
					break;
				case > 959F when Position.X < 960F:
					_timeDisplayNode.Position = new Vector2(0F, _timeDisplayNode.Position.Y);
					possessionTimer.Stop();
					possessionTimer.Start();
					break;
			}
			break;
		}
	}
	
	public override void _UnhandledInput(InputEvent @event)
	{
		if (@event.Device != Device.Number)
			return;

		if (_boostAction is null || _moveLeftAction is null || _moveRightAction is null || 
		    _moveUpAction is null || _moveDownAction is null)
			return;
		
		Boosting = IsActionPressed(_boostAction);
		Direction = GetVector(_moveLeftAction, _moveRightAction, 
			_moveUpAction, _moveDownAction);
		
		if (@event.IsActionPressed("Change Rotation"))
		{
			if (_singleRotationIndicator is null)
			{
				GD.PrintErr("Rotation indicator node is null");
				return;
			}
			
			if (_rotationDirection == 1)
				_rotationDirection *= -1;
			
			_singleRotationIndicator.RotationDegrees += 180F;
			return;
		}
		
		if (_rotateLeftAction is null || _rotateRightAction is null || 
		    _rotateUpAction is null || _rotateDownAction is null)
			return;

		var rotation = 0f;
		switch (@event)
		{
			case InputEventMouseMotion mouseMotion when Device.Type == 'K':
				rotation = _rotationDirection * mouseMotion.Relative.X * Sensitivity;
				RotateToAmount = rotation;
				break;
			case InputEventJoypadMotion when Device.Type == 'C':
			{
				var to = GetVector(_rotateLeftAction, _rotateRightAction,
					_rotateUpAction, _rotateDownAction);
				if (to.Length() >= 1f)
				{
					var targetAngle = Mathf.Pi / 2 + to.Angle();
					RotateToAmount = targetAngle;
				}
				break;
			}
		}

		if (Multiplayer.IsServer()) return;
			
		_cachedState.Position = Position;
		_cachedState.Rotation = Rotation;
		_cachedState.InputStamp = InputStamp++;
		_cachedState.Boosting = Boosting;
		
		if (_playersHandler is null)
		{
			GD.PrintErr("PlayersHandler node is null");
			return;
		}

		_playersHandler.FetchInputWrapper
		(
			Name,
			_cachedState, 
			Direction,
			rotation
		);
	}
}
