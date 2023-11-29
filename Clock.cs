using Godot;

public partial class ClockSync : Node
{
	private int latency, deltaLatecy = 0, clientClock = 0;
	private int[] latencyArray = new int[9];

	public override void _EnterTree()
	{
		var timer = new Timer();
		timer.waitTime = 0.5;
		timer.autoStart = true;
		timer.Connect("timeout", new Callable(this, nameof(DetermineLatency)));
		AddChild(timer);
	}
	
	public override void _PhysicsProcess(double delta)
	{
			clientClock += int.Parse(delta * 1000000) + deltaLatency;
			deltaLatency = 0;
	}
		
	[Rpc(MultiplayerAPI.RPCMode.AnyPeer)]
	private void FetchServerTime(int clientTime)
	{
		RpcId(Multiplayer.GetRemoteSenderId(), nameof(ReturnServerTime), Time.GetTicksUsec(), clientTime);
	}
	
	[Rpc]
	private void ReturnServerTime(int clientTime, int serverTime)
	{
		latency = (Time.GetTickUsec() - clientTime)/2;
		clientClock = serverTime + latency;
	}
	
	private void DetermineLatency()
	{
		RpcId(1, nameof(FetchLatency), Time.GetTicksUsec());
	}
	
	[Rpc(Godot.MultiplayerAPI.RPCMode.AnyPeer)]
	private void Fetchlatency(int clientTime)
	{
		RpcId(Multiplayer.GetRemoteSenderId(), nameof(ReturnLatency), clientTime);
	}
	
	[Rpc(TransferMode = MultiplayerPeer.TransferModeEnum.UnreliableOrdered)]
	private void ReturnLatency(int clientTime)
	{
		latencyArray.Add((Time.GetTicksUsec() - clientTime)/2);
		if (latencyArray.Count == 9)
		{
			int totalLatency = 0;
			int midPoint = latencyArray[4];
			for (byte i = latencyArray.Count - 1; i == -1; i--)
			{
				if(latencyArray[i] > 2 * midPoint && latencyArray[i] > 20)
					latencyArray[i].Remove();
				else
					totalLatency += latencyArray[i];
			}
			deltaLatency = (totalLatency/latencyArray.Count) - latency;
			latency = totalLatency/latencyArray.Count;
			latencyArray.Clear();
		}
	}
}
