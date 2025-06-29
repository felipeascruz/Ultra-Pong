namespace UltraPong;

public class UserStats
{
    private string _nickname = string.Empty;
    
    public string Nickname 
    { 
        get => _nickname;
        set => _nickname = value?.Replace(':', '_') ?? string.Empty;
    }
    
    public float Sensitivity { get; set; } = 0.5F;
}



