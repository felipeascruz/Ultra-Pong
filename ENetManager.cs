using System;
using System.Threading.Tasks;

namespace UltraPong;
using Godot;

public partial class ENetManager(bool isHost) : Node
{
    private static readonly ENetMultiplayerPeer Peer = new();
    private bool isHost = isHost;

    public override void _Ready()
    {
        var error = Peer.CreateMesh(isHost ? 1 : 2);
        if (error != Error.Ok)
        {
            GD.PrintErr("Error creating ENet Mesh: " + error);
        }
        GetTree().GetMultiplayer().SetMultiplayerPeer(Peer);
    }

    public void ConnectToPeer(ENetConnection connection)
    {
        var error = Peer.AddMeshPeer(isHost ? 2 : 1, connection);
        if (error != Error.Ok)
            GD.PrintErr("Error adding ENet Mesh peer: " + error);
    }
}