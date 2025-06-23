namespace UltraPong;

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Godot;

public partial class PlayersHandler : Node
{
	private readonly float _maxRotation = 
		JsonFileAccess.Read<PlayerStats>("res://playerStats.json").MaxRotation;
	
	private readonly Dictionary<string, Dictionary<uint, State>> _localStates = new ();
	public List<KeyValuePair<ulong, Dictionary<string, State>>> StatesBuffer { get; private set; } = new();
	private const byte INTERPOLATION_MS = 30;
	private Node Players => GetNode("../../World/Players");

	public void FetchInputWrapper(string name, State state , Vector2 direction, bool boosting, float rotation)
	{
		if (!_localStates.TryAdd(name, new Dictionary<uint, State>{[state.InputStamp] = state}))
			_localStates[name].Add(state.InputStamp, state);
		
		if (_localStates[name].Count > 10)
			_localStates[name].Remove(_localStates[name].Keys.Min());
		
		RpcId(1, nameof(FetchInput),name, state.InputStamp, direction, boosting, rotation);
	}
	
	[Rpc(MultiplayerApi.RpcMode.AnyPeer, TransferMode = MultiplayerPeer.TransferModeEnum.UnreliableOrdered)]
	private void FetchInput(string playerName, uint inputStamp, Vector2 direction, bool boosting, float rotation)
	{
		var player = Players.GetNode<Player>(playerName);

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
		if (!Multiplayer.IsServer()) { RenderRemoteStates(); return; }
		
		var serverStates = Players.GetChildren().Cast<Player>().
			ToDictionary<Player, string, State>(player => player.Name, player => new State(player.Position, player.Rotation, player.InputStamp, player.Boosting));

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
		var player = Players.GetNode<Player>(name);

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
		
		var renderTime = GetNode<Clock>("../../Network/Clock").ClientClock - INTERPOLATION_MS * 1000;
		while (StatesBuffer.Count > 2 && renderTime > StatesBuffer[1].Key)
			StatesBuffer.RemoveAt(0);

		var latestStates = StatesBuffer[0].Value;
		var nextStates = StatesBuffer[1].Value;

		var interpolationFactor = Mathf.Clamp((float)(renderTime - StatesBuffer[0].Key) / (StatesBuffer[1].Key - StatesBuffer[0].Key), 0F, 1F);

		foreach (var player in latestStates.Where(player => nextStates.ContainsKey(player.Key)))
		{
			var latestState = latestStates[player.Key];
			var nextState = nextStates[player.Key];
			
			var playerNode = Players.GetNode<Player>(player.Key);
			playerNode.GlobalPosition = latestState.Position.Lerp(nextState.Position, interpolationFactor);
			playerNode.Rotation = Mathf.LerpAngle(latestState.Rotation, nextState.Rotation, interpolationFactor);
			playerNode.Boosting = nextState.Boosting;
		}
	}
	
	public class State
	{
		public readonly Vector2 Position;
		public readonly float Rotation;
		public readonly uint InputStamp;
		public readonly bool Boosting;
		
		public State(Vector2 position, float rotation, uint inputStamp = 0, bool boosting = false)
		{
			Position = position;
			Rotation = rotation;
			InputStamp = inputStamp;
			Boosting = boosting;
		}
	}
}