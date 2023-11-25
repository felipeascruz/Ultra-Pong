using Godot;
using static GLOBAL;
using static Godot.Input;

public partial class InputSynchronizer : MultiplayerSynchronizer
{	
	public Vector2 Direction { get; private set; }
	public bool Boosting { get; set; }
	
	private bool _isMultiplayerAuthority;
	private sbyte _rotationDirection = -1;
	
	public override void _Ready()
	{
		_isMultiplayerAuthority = GetMultiplayerAuthority() == Multiplayer.GetUniqueId();
		SetPhysicsProcess(_isMultiplayerAuthority);
		SetProcessInput(_isMultiplayerAuthority);
		if (_isMultiplayerAuthority)
			MouseMode = MouseModeEnum.Captured;
	}

	public override void _PhysicsProcess(double delta)
	{
		Direction = GetVector("MoveLeft", "MoveRight", "MoveUp", "MoveDown");
		if (IsActionJustPressed("Boost"))
			Rpc(nameof(SetBoost), true);
		if (IsActionJustReleased("Boost"))
			Rpc(nameof(SetBoost), false);
	}

	public override void _Input(InputEvent @event)
	{
		if (@event.IsActionPressed("ChangeRotation"))
			_rotationDirection *= -1;

		if (@event is InputEventMouseMotion motion)
			Rpc(nameof(RotatePlayer), _rotationDirection * motion.Relative.X);
	}

	[Rpc(CallLocal = true, TransferMode = MultiplayerPeer.TransferModeEnum.Reliable)]
	public void RotatePlayer(float value)
	{
		GetParent<CharacterBody2D>().Rotate(Mathf.Clamp(value * SENSITIVITY, -MAX_ROTATION, MAX_ROTATION));
	}

	[Rpc(CallLocal = true, TransferMode = MultiplayerPeer.TransferModeEnum.Reliable)]
	private void SetBoost(bool boosting)
	{
		Boosting = boosting;

		GetNode<ColorRect>("../Rectangle").Color = boosting ? Colors.Yellow : GetNode<Player>("../").InitialColor;
	}
}
