using Godot;

namespace UltraPong;

public partial class Clock : Node
{
	public ulong ClientClock;
	
	private ENetPacketPeer? _hostPacketPeer;
	
	// Two way Round Trip Time
	public double RttMean;
	
	private double _deltaRtt;

	public override void _Ready()
	{
		SetPhysicsProcess(!Multiplayer.IsServer());
		if (Multiplayer.IsServer())
			return;
		
		_hostPacketPeer = (Multiplayer.GetMultiplayerPeer() as ENetMultiplayerPeer)?.GetPeer(1);
		
		RpcId(1, nameof(FetchServerTime));
	}

	// Only called on clients
	public override void _PhysicsProcess(double delta)
	{
		if (_hostPacketPeer is null)
			return;
		
		_deltaRtt =  _hostPacketPeer.GetStatistic(ENetPacketPeer.PeerStatistic.RoundTripTime) - RttMean;
		RttMean = _hostPacketPeer.GetStatistic(ENetPacketPeer.PeerStatistic.RoundTripTime);
		
		ClientClock += (ulong)(delta * 1_000_000D + _deltaRtt/2);
	}
		
	[Rpc(MultiplayerApi.RpcMode.AnyPeer, TransferMode = MultiplayerPeer.TransferModeEnum.Reliable, TransferChannel = 5)]
	private void FetchServerTime()
	{
		RpcId(Multiplayer.GetRemoteSenderId(), nameof(ReturnServerTime), Time.GetTicksUsec());
	}
	
	[Rpc(TransferMode = MultiplayerPeer.TransferModeEnum.Reliable)]
	private void ReturnServerTime(ulong serverTime)
	{
		ClientClock = serverTime + (ulong)RttMean;
	}
}