namespace UltraPong;

using Godot;


public partial class Ball : RigidBody2D
{
	public static readonly BallStats Stats = JsonFileAccess.Read<BallStats>("res://ballStats.json");
	
	private bool _reset;
	private Vector2 _resetPosition;

	public BallHandler.State ServerState { get; set; }
	
	public override void _EnterTree()
	{
		var hitBox = (CircleShape2D)GetNode<CollisionShape2D>("Collision").Shape;
		hitBox.Radius = Stats.Size;
		GetNode<Sprite2D>("Sprite2D").Scale = new Vector2(hitBox.Radius * 0.0022F, hitBox.Radius * 0.0022F);
		ServerState = new BallHandler.State(Position, Rotation, LinearVelocity, AngularVelocity);
	}
	
	public override void _IntegrateForces(PhysicsDirectBodyState2D state)
	{
		//Set Ball Max Speed
		if (state.LinearVelocity.Length() > Stats.MaxSpeed)
			state.LinearVelocity = state.LinearVelocity.Normalized() * Stats.MaxSpeed;
		
		var t = Transform2D.Identity;
		//Reset Ball's position if wanted
		if (_reset)
		{
			state.LinearVelocity = Vector2.Zero;
			state.AngularVelocity = 0F;

			t.Origin = _resetPosition;
			state.Transform = t;

			_reset = false;
		}
		
		//Set state to server's state
		if (Multiplayer.IsServer())
		{
			GetNode<BallHandler>("../../Network/BallHandler").ReturnBallStateWrapper(new BallHandler.State(Position, Rotation, LinearVelocity, AngularVelocity));
			return;
		}
		
		Rotation = ServerState.Rotation;
		t.Origin = ServerState.Position;
		state.Transform = t;
		state.LinearVelocity = ServerState.LinearVelocity;
		state.AngularVelocity = ServerState.AngularVelocity;
	}

	public void Reset(Vector2 position)
	{
		_resetPosition = position;
		_reset = true;
	}

	private void OnCollided(Node body)
	{
		if (body is Player)
			return;
		
		if (body.Name == "MidField")
		{
			if (Multiplayer.IsServer())
				GetNodeOrNull<PossessionTimer>("../../World/Possession Timer")?.Stop();
			return;
		}

		var sfx = GetNode<AudioStreamPlayer2D>("SoundFX");
		sfx.PitchScale = 0.5F + LinearVelocity.Length()/Stats.MaxSpeed;
		sfx.Play();
	}
}
