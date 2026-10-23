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
    public bool IsValid()
    {
        string?[] strings = { Motd, Gamemode, Difficulty, ViewDistance, Seed };
        return strings.All(s => s != null && s.Length <= 512 && !s.Any(char.IsControl)) &&
            MaxPlayers is >= 1 and <= 100_000 &&
            new[] { "survival", "creative", "adventure", "spectator", "0", "1", "2", "3" }.Contains(Gamemode.ToLowerInvariant()) &&
            new[] { "peaceful", "easy", "normal", "hard", "0", "1", "2", "3" }.Contains(Difficulty.ToLowerInvariant()) &&
            int.TryParse(ViewDistance, out int distance) && distance is >= 2 and <= 64;
    }
}
