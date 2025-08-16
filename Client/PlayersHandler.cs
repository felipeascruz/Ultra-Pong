namespace UltraPong;

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Godot;

public partial class PlayersHandler : Node
{
	private readonly float _maxRotation = JsonFileAccess.Read<PlayerStats>("res://playerStats.json").MaxRotation;
	
	private readonly Dictionary<string, Dictionary<uint, State>> _localStates = new ();
	public List<KeyValuePair<ulong, Dictionary<string, State>>> StatesBuffer { get; private set; } = [];
	private const ulong INTERPOLATION_USEC = 10_000;
	private readonly Dictionary<string, Player> _playersCache = new();
	
	// Cache nodes to avoid calling GetNode every frame
	private Node _playersNode;
	private Clock _clockNode;
	
	public override void _Ready()
	{
		_playersNode = GetNode("../../World/Players");
		_clockNode = GetNode<Clock>("../Clock");
	}
	
	public void FetchInputWrapper(string name, State state, Vector2 direction, float rotation)
	{
		if (!_localStates.TryAdd(name, new Dictionary<uint, State>{[state.InputStamp] = state}))
			_localStates[name].Add(state.InputStamp, state);
		
		if (_localStates[name].Count > 10)
			_localStates[name].Remove(_localStates[name].Keys.Min());
		
		RpcId(1, nameof(FetchInput),name, state.InputStamp, direction, state.Boosting, rotation);
	}
	
	private Player GetCachedPlayer(string playerName)
	{
		// If the player is cached it returns it
		if (_playersCache.TryGetValue(playerName, out var player) && IsInstanceValid(player)) return player;
		
		// If the player is not already cached, it caches it and returns the Node
		player = _playersNode.GetNode<Player>(playerName);
		_playersCache[playerName] = player;
		return player;
	}

	[Rpc(MultiplayerApi.RpcMode.AnyPeer, TransferMode = MultiplayerPeer.TransferModeEnum.UnreliableOrdered)]
	private void FetchInput(string playerName, uint inputStamp, Vector2 direction, bool boosting, float rotation)
	{
	    var player = GetCachedPlayer(playerName); // Use cache

	    direction = direction.LimitLength();
	    if (!player.Direction.IsEqualApprox(direction))
	        player.Direction = direction;

	    player.Boosting = boosting;
	    player.InputStamp = inputStamp;
	    player.RotateTo = rotation;
	    //player.Rotate(player.Device.Type == 'K' ? Mathf.Clamp(rotation, -_maxRotation, _maxRotation) : rotation);
	}
		
	public override void _PhysicsProcess(double delta)
	{
	    if (!Multiplayer.IsServer()) 
	    { 
	        RenderRemoteStates(); 
	        return; 
	    }
	    
	    var players = _playersNode.GetChildren().Cast<Player>();
	    var serverStates = new Dictionary<string, State>();
	    
	    foreach (var player in players)
	        serverStates[player.Name] = new State(player.Position, player.Rotation, player.InputStamp, player.Boosting);

		if (serverStates.Count == 0) return;
		
		var states = SerializeToBinary(serverStates);
		Rpc(nameof(ReturnPlayersStates),Time.GetTicksUsec(), states);
	}
	
	[Rpc(TransferChannel = 1)]
	private void ReturnPlayersStates(ulong timestamp, byte[] playersStates)
	{
		var states = DeserializeFromBinary(playersStates);
		foreach (Player player in GetNode("../../World/Players").GetChildren())
			if (player.IsLocalPlayer && states.ContainsKey(player.Name))
			{
				var localState = states[player.Name];
				RenderLocalStates(player.Name, localState.InputStamp, localState.Position, localState.Rotation);
				states.Remove(player.Name);
			}

		if (states.Count > 0)
			StatesBuffer.Add(new KeyValuePair<ulong, Dictionary<string, State>>(timestamp, states));
	}
	
	private static byte[] SerializeToBinary(Dictionary<string, State> states)
	{
		using var stream = new MemoryStream();
		using var writer = new BinaryWriter(stream);
		
		writer.Write(states.Count);
		
		foreach (var kvp in states)
		{
			writer.Write(kvp.Key);
			writer.Write(kvp.Value.Position.X);
			writer.Write(kvp.Value.Position.Y);
			writer.Write(kvp.Value.Rotation);
			writer.Write(kvp.Value.InputStamp);
			writer.Write(kvp.Value.Boosting);
		}
		
		return stream.ToArray();
	}
	
	private static Dictionary<string, State> DeserializeFromBinary(byte[] data)
	{
		using var stream = new MemoryStream(data);
		using var reader = new BinaryReader(stream);
		
		var count = reader.ReadInt32();
		var states = new Dictionary<string, State>();
		
		for (int i = 0; i < count; i++)
		{
			var name = reader.ReadString();
			var posX = reader.ReadSingle();
			var posY = reader.ReadSingle();
			var rotation = reader.ReadSingle();
			var inputStamp = reader.ReadUInt32();
			var boosting = reader.ReadBoolean();
			
			states[name] = new State(new Vector2(posX, posY), rotation, inputStamp, boosting);
		}
		
		return states;
	}
	
	private void RenderLocalStates(string name, uint inputStamp, Vector2 position, float rotation)
	{
		var player = GetCachedPlayer(name);

		try
		{
			if (++inputStamp < player.InputStamp)
				UpdateState(
					position + player.Position - _localStates[name][inputStamp].Position,
					rotation + player.Rotation - _localStates[name][inputStamp].Rotation);
			else
				UpdateState(position, rotation);
		}
		catch (Exception e)
		{
			GD.PrintErr("Error while updating state: " + e.Message);
		}

		return;

		void UpdateState(Vector2 newPosition, float newRotation)
		{
			if (player.Position != newPosition)
				player.Position = player.Position.Lerp(newPosition, 0.1F);

			if (Math.Abs(player.Rotation - newRotation) > 0.5F)
				player.Rotation = Mathf.LerpAngle(player.Rotation, newRotation, 1F);
		}
	}

	private void RenderRemoteStates()
	{
		if (StatesBuffer.Count <= 1) return;
		
		StatesBuffer = StatesBuffer.OrderBy(state => state.Key).ToList();
		
		var renderTime = _clockNode.ClientClock - (INTERPOLATION_USEC + _clockNode.Latency);
		while (StatesBuffer.Count > 2 && renderTime > StatesBuffer[1].Key)
			StatesBuffer.RemoveAt(0);

		var latestStates = StatesBuffer[0].Value;
		var nextStates = StatesBuffer[1].Value;

		var interpolationFactor = Mathf.Clamp((float)(renderTime - StatesBuffer[0].Key) / (StatesBuffer[1].Key - StatesBuffer[0].Key), 0F, 1F);

		foreach (var player in latestStates.Where(player => nextStates.ContainsKey(player.Key)))
		{
			var latestState = latestStates[player.Key];
			var nextState = nextStates[player.Key];
			
			var playerNode = GetCachedPlayer(player.Key);
			playerNode.GlobalPosition = latestState.Position.Lerp(nextState.Position, interpolationFactor);
			playerNode.Rotation = Mathf.LerpAngle(latestState.Rotation, nextState.Rotation, interpolationFactor);
			playerNode.Boosting = nextState.Boosting;
		}
	}
	
	public struct State
	{
		public Vector2 Position;
		public float Rotation;
		public uint InputStamp;
		public bool Boosting;

		public State() { }
		
		public State(Vector2 position, float rotation, uint inputStamp = 0, bool boosting = false)
		{
			Position = position;
			Rotation = rotation;
			InputStamp = inputStamp;
			Boosting = boosting;
		}
	}
}