namespace UltraPong;

using Godot;

public partial class MenuSchema : Control
{
    private static readonly UserStats UserStats = JsonFileAccess.Read<UserStats>("user://userStats.json");
    private LineEdit NicknameNode => GetNode<LineEdit>("Nickname");
    public override void _Ready()
    {
        GetNode<HSlider>("Sensitivity").Value = UserStats.Sensitivity;

        NicknameNode.Text = UserStats.Nickname;
        NicknameNode.GrabFocus();
        NicknameNode.CaretColumn = NicknameNode.Text.Length;
    }

    public override void _ExitTree()
    {
        UserStats.Nickname = NicknameNode.Text;
        UserStats.Sensitivity = (float)GetNode<HSlider>("Sensitivity").Value;
        JsonFileAccess.Write("user://userStats.json", UserStats);
    }
}
