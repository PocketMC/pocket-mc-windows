namespace PocketMC.RemoteControl.Models;

public sealed class RemoteServerPropertiesDto
{
    public string Motd { get; set; } = string.Empty;
    public string Gamemode { get; set; } = "survival";
    public string Difficulty { get; set; } = "easy";
    public int MaxPlayers { get; set; } = 20;
    public bool Pvp { get; set; } = true;
    public bool Whitelist { get; set; }
    public bool AllowFlight { get; set; }
    public bool AllowCommandBlock { get; set; }
    public bool AllowNether { get; set; } = true;
    public string ViewDistance { get; set; } = "10";
    public string Seed { get; set; } = string.Empty;
}
