namespace UltraPong;
using Godot;

public partial class Config : Resource
{
	[ExportCategory("Video")]
	[Export] public DisplayServer.WindowMode WindowMode { get; set; } = DisplayServer.WindowMode.ExclusiveFullscreen;
	[Export] public Vector2 Resolution { get; set; } = new(1920F, 1080F);
	[Export] public bool VSync { get; set; }

	[ExportGroup("Anti-Alisasing")]
	[Export] public Viewport.Msaa MSAA { get; set; } = Viewport.Msaa.Msaa8X;
	[Export] public Viewport.ScreenSpaceAAEnum FXAA { get; set; } = Viewport.ScreenSpaceAAEnum.Max;
	[Export] public bool TAA { get; set; }
	
	public Config() : this(0L, Vector2.Zero, false, 0, 0, false) {}
	public Config(DisplayServer.WindowMode windowMode, Vector2 resolution, bool vSync, Viewport.Msaa msaa, Viewport.ScreenSpaceAAEnum fxaa, bool taa)
	{
		//Video
		WindowMode = windowMode;
		Resolution = resolution;
		VSync = vSync;
		MSAA = msaa;
		FXAA = fxaa;
		TAA = taa;
	}
}
