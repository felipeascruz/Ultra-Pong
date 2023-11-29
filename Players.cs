using Godot;

public partial class Players : Node
{
	private Vector2 position;
	private float rotation;
	
	public void FetchInputWrapper(int timestamp, bool[] direction, bool boosting, float rotation)
	{
		RpcId(1, nameof(FetchInput), direction, boosting, rotation);
	}
	
	[Rpc(MultiplayerAPI.RPCMode.AnyPeer, TransferMode = MultiplayerPeer.TransferModeEnum.UnreliableOrdered)]
	private void FetchInput(bool[] direction, bool boosting, float rotation)
	{
		var player = GetNode<Player>(Multiplayer.GetRemoteSenderId().toString());
		
		var speed = Global.Player.Speed;
		if (boosting)
			speed *= 3F;
			
		player.Velocity = Input.GetVector(direction[0], direction[1], direction[2], direction[3]) * player.speed;
		player.Rotation = MathF.Clamp(-Global.Player.MaxRotation, Global.Player.MaxRotation, rotation);
		
		RpcId(Multiplayer.GetRemoteSenderId(), nameof(ReturnLocalState), timestamp, player.Position, player.Rotation)
	}
	
	[Rpc]
	private void ReturnLocalState(int timestamp, Vector2 position, float rotation)
	{
		
	}
	
	[Rpc]
	private void ReturnOthersState()
	{
		
	}
}
