namespace UltraPong;

public class Peer(string publicIp, string privateIp, ushort port, string nickname)
{
    public string PublicIp { get; set; } = publicIp;
    public string PrivateIp { get; set; } = privateIp;
    public ushort Port { get; set; } = port;
    public string Nickname { get; set; } = nickname;
}