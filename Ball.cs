using Godot;
using System;


public partial class Ball : RigidBody2D
{
	private const float MaxSpeed = Global.Player.Speed * 4F;
	
	private bool _reset;
	private Vector2 _resetPosition;
	
	private Vector2 _serverPosition = new(960F, 540F), _serverLinearVelocity = Vector2.Zero;
	private float _serverRotation, _serverAngularVelocity;

	public void Reset(Vector2 position)
	{
		_resetPosition = position;
		_reset = true;
	}
	
	public override void _EnterTree()
	{
		var hitBox = (CircleShape2D)GetNode<CollisionShape2D>("Collision").Shape;
		hitBox.Radius = Global.Player.Size.Y / 7;
		GetNode<Sprite2D>("Sprite2D").Scale = new Vector2(hitBox.Radius * 0.0022F, hitBox.Radius * 0.0022F);
	}
	
	public override void _IntegrateForces(PhysicsDirectBodyState2D state)
	{
		var t = Transform2D.Identity;
		//Set position to server's position
		if (!Multiplayer.IsServer())
		{
			Rotation = _serverRotation;
			t.Origin = _serverPosition;
			state.Transform = t;
			state.LinearVelocity = _serverLinearVelocity;
			state.AngularVelocity = _serverAngularVelocity;
		}
		else
			Rpc(nameof(GetServerState), Position, Rotation, LinearVelocity, AngularVelocity);

		//Set Ball Max Speed
		if (Math.Abs(state.LinearVelocity.X) > MaxSpeed || Math.Abs(state.LinearVelocity.Y) > MaxSpeed)
			state.LinearVelocity = state.LinearVelocity.Normalized() * MaxSpeed;

		//Reset Ball's position if wanted
		if (_reset)
		{
			state.LinearVelocity = Vector2.Zero;
			state.AngularVelocity = 0F;

			t.Origin = _resetPosition;
			state.Transform = t;

			_reset = false;
		}
	}

	[Rpc(CallLocal = false, TransferMode = MultiplayerPeer.TransferModeEnum.UnreliableOrdered, TransferChannel = 1)]
	private void GetServerState(Vector2 position, float rotation, Vector2 linearVelocity, float angularVelocity)
	{
		_serverPosition = position;
		_serverRotation = rotation;
		_serverLinearVelocity = linearVelocity;
		_serverAngularVelocity = angularVelocity;
	}
}
