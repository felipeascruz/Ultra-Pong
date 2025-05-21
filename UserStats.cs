using Godot;

public partial class UserStats : Resource
{
	[Export] public string Username;
	
	public UserStats() : this("") {}

	public UserStats(string username)
	{
		Username = username;
	}
}
