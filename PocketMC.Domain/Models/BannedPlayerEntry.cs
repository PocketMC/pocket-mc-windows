namespace PocketMC.Domain.Models
{
    public sealed class BannedPlayerEntry
    {
        public string Name { get; set; } = string.Empty;
        public string Reason { get; set; } = string.Empty;
        public string Created { get; set; } = string.Empty;
        public string Expires { get; set; } = "forever";
        public bool IsSidecar { get; set; }
    }
}
