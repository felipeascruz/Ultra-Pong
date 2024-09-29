namespace UltraPong;

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;
using Godot;

public partial class PlayersHandler : Node
{
	private readonly float _maxRotation = GD.Load<PlayerStats>("res://PlayerStats.res").MaxRotation;
	private readonly Dictionary<string, Dictionary<uint, State>> _localStates = new ();
	public List<KeyValuePair<ulong, Dictionary<string, State>>> StatesBuffer { get; private set; } = new();
	private const byte InterpolationMs = 50;
	private readonly JsonSerializerOptions _jsonOptions = new() { Converters = { new StateConverter() } };
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
		
		var states = JsonSerializer.Serialize(serverStates, _jsonOptions);
		Rpc(nameof(ReturnPlayersStates),Time.GetTicksUsec(), states);
	}
	
	[Rpc(TransferChannel = 1)]
	private void ReturnPlayersStates(ulong timestamp, string playersStates)
	{
		var states = JsonSerializer.Deserialize<Dictionary<string, State>>(playersStates, _jsonOptions);
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
		catch (Exception)
		{
			//ignored
		}

		return;

		void UpdateState(Vector2 newPosition, float newRotation)
		{
			if (player.Position != newPosition)
				player.Position = player.Position.Lerp(newPosition, 0.1F);

			if (Math.Abs(player.Rotation - newRotation) > 0.5F)
				player.Rotation = (float)Mathf.LerpAngle(player.Rotation, newRotation, 1F);
		}
	}

	private void RenderRemoteStates()
	{
		if (StatesBuffer.Count <= 1) return;
		
		StatesBuffer = StatesBuffer.OrderBy(state => state.Key).ToList();
		
		var renderTime = GetNode<Clock>("../../Network/Clock").ClientClock - InterpolationMs * 1000;
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
			playerNode.Rotation = (float)Mathf.LerpAngle(latestState.Rotation, nextState.Rotation, interpolationFactor);
			playerNode.Boosting = nextState.Boosting;
		}
	}
	
	public class State
	{
		public Vector2 Position;
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
	
	public class StateConverter : JsonConverter<State>
	{
		public override State Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
		{
			using var doc = JsonDocument.ParseValue(ref reader);
			var root = doc.RootElement;

			return new State(
				new Vector2(root.GetProperty("Position").GetProperty("X").GetSingle(),
					root.GetProperty("Position").GetProperty("Y").GetSingle()), 
				root.GetProperty("Rotation").GetSingle(), 
				root.GetProperty("InputStamp").GetUInt32(),
				root.GetProperty("Boosting").GetBoolean());
		}

		public override void Write(Utf8JsonWriter writer, State value, JsonSerializerOptions options)
		{
			writer.WriteStartObject();

			writer.WriteStartObject("Position");
			writer.WriteNumber("X", value.Position.X);
			writer.WriteNumber("Y", value.Position.Y);
			writer.WriteEndObject();

			writer.WriteNumber("Rotation", value.Rotation);
			
			writer.WriteNumber("InputStamp", value.InputStamp);
			
			writer.WriteBoolean("Boosting", value.Boosting);

			writer.WriteEndObject();
		}
	}
}
