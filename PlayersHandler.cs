using Godot;
using static System.Convert;

public partial class PlayersHandler : Node
{
	private readonly State[] _states = new State[6];
	private sbyte clientStamp;
	
	public void FetchInputWrapper(State state, bool[] direction, bool boosting, float rotation)
	{
		if (clientStamp == _states.Length)
			clientStamp = 0;
		_states[clientStamp++] = state;
		RpcId(1, nameof(FetchInput), clientStamp, direction[0], direction[1], direction[2], direction[3], boosting, rotation);
	}
	
	[Rpc(MultiplayerApi.RpcMode.AnyPeer, TransferMode = MultiplayerPeer.TransferModeEnum.UnreliableOrdered, CallLocal = true)]
	private void FetchInput(sbyte stamp, bool left, bool right, bool up, bool down, bool boosting, float rotation)
	{
		var player = GetNode<Player>("/root/Main/World/Players/" + Multiplayer.GetRemoteSenderId());
		
		var speed = Global.Player.Speed;
		if (boosting)
			speed *= 3F;
			
		var x = ToSingle(right) - ToSingle(left);
		var y = ToSingle(down) - ToSingle(up);
		player.Velocity = new Vector2(x, y) * speed;
		player.Rotation = Mathf.Clamp(rotation, -Global.Player.MaxRotation, Global.Player.MaxRotation);
		
		RpcId(Multiplayer.GetRemoteSenderId(), nameof(ReturnLocalState), stamp, player.Position, player.Rotation);
	}
	
	[Rpc(TransferMode = MultiplayerPeer.TransferModeEnum.UnreliableOrdered, CallLocal = true)]
	private void ReturnLocalState(sbyte stamp, Vector2 position, float rotation)
	{
		var player = GetNode<Player>("/root/Main/World/Players/" + Multiplayer.GetRemoteSenderId());
		
		if (stamp < clientStamp)
			UpdateState(player.Position, player.Rotation, position, rotation);
		else
			UpdateState(_states[stamp].Position, _states[stamp].Rotation,
				position + player.Position - _states[stamp].Position, 
				rotation + player.Rotation - _states[stamp].Rotation);
		return;

		void UpdateState(Vector2 positionCheck, float rotationCheck, Vector2 newPosition, float newRotation)
		{
			if (positionCheck != position)
				if (positionCheck.IsEqualApprox(position))
					player.Position.Lerp(newPosition, 0.5F);
				else
					player.Position = newPosition;

			if (rotationCheck != newRotation)
				player.Rotation = Mathf.Lerp(player.Rotation, newRotation, 1F);
		}
	}
	
	[Rpc(TransferChannel = 1)]
	private void ReturnOthersState(ulong timestamp, Vector2 position, float rotation)
	{
		
	}

	public class State
	{
		public Vector2 Position;
		public float Rotation;

		public State(Vector2 position, float rotation)
		{
			Position = position;
			Rotation = rotation;
		}
	}
}
