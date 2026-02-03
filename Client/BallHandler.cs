namespace UltraPong;
using Godot;

public partial class BallHandler : Node
{
	public void ReturnBallStateWrapper(State state)
	{
		Rpc(nameof(ReturnBallState), state.Position, state.Rotation, state.LinearVelocity, state.AngularVelocity);
	}
	
	[Rpc(TransferMode = MultiplayerPeer.TransferModeEnum.UnreliableOrdered, TransferChannel = 2)]
	private void ReturnBallState(Vector2 position, float rotation, Vector2 linearVelocity, float angularVelocity)
	{
		GetNode<Ball>("../../World/Ball").ServerState = new State(position, rotation, linearVelocity, angularVelocity);
	}

	public class State(Vector2 position, float rotation, Vector2 linearVelocity, float angularVelocity)
	{
		public readonly float Rotation = rotation, AngularVelocity = angularVelocity;
		public readonly Vector2 Position = position, LinearVelocity = linearVelocity;

	}
}
