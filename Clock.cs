using System;
using System.Collections.Generic;
using Godot;

public partial class Clock : Node
{
	private ulong _latency, _deltaLatency;
	public ulong ClientClock;
	private readonly List<ulong> _latencyArray = new();

	public override void _Ready()
	{
		SetPhysicsProcess(!Multiplayer.IsServer());
		if (Multiplayer.IsServer())
			return;
		RpcId(1, nameof(FetchServerTime), Time.GetTicksUsec());
		
		var timer = new Timer();
		timer.WaitTime = 0.5;
		timer.Autostart = true;
		timer.Connect("timeout", new Callable(this, nameof(DetermineLatency)));
		AddChild(timer);
	}

	public override void _PhysicsProcess(double delta)
	{
		ClientClock += (ulong)(delta * 1_000_000D + _deltaLatency);
		_deltaLatency = 0;
	}
		
	[Rpc(MultiplayerApi.RpcMode.AnyPeer, TransferMode = MultiplayerPeer.TransferModeEnum.Reliable, TransferChannel = 5)]
	private void FetchServerTime(ulong clientTime)
	{
		RpcId(Multiplayer.GetRemoteSenderId(), nameof(ReturnServerTime), Time.GetTicksUsec(), clientTime);
	}
	
	[Rpc(TransferMode = MultiplayerPeer.TransferModeEnum.Reliable)]
	private void ReturnServerTime(ulong serverTime, ulong clientTime)
	{
		_latency = (Time.GetTicksUsec() - clientTime)/2;
		ClientClock = serverTime + _latency;
	}
	
	//Signaled through Timer
	private void DetermineLatency()
	{
		RpcId(1, nameof(FetchLatency), Time.GetTicksUsec());
	}
	
	[Rpc(MultiplayerApi.RpcMode.AnyPeer, TransferMode = MultiplayerPeer.TransferModeEnum.Reliable, TransferChannel = 5)]
	private void FetchLatency(ulong clientTime)
	{ 
		RpcId(Multiplayer.GetRemoteSenderId(), nameof(ReturnLatency), clientTime);
	}
	
	[Rpc(TransferMode = MultiplayerPeer.TransferModeEnum.Reliable,TransferChannel = 5)]
	private void ReturnLatency(ulong clientTime)
	{
		_latencyArray.Add((Time.GetTicksUsec() - clientTime)/2);
		if (_latencyArray.Count != 9) return;
		
		ulong totalLatency = 0;
		var midPoint = _latencyArray[4];
		for (var i = (sbyte)(_latencyArray.Count - 1); i == -1; i--)
		{
			if(_latencyArray[i] > 2 * midPoint && _latencyArray[i] > 20)
				_latencyArray.RemoveAt(i);
			else
				totalLatency += _latencyArray[i];
		}
		_deltaLatency = totalLatency/(ulong)_latencyArray.Count - _latency;
		_latency = totalLatency/(ulong)_latencyArray.Count;
	}
}
