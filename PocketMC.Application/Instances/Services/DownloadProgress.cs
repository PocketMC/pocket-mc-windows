namespace PocketMC.Application.Instances.Services;

public struct DownloadProgress
{
    public long BytesRead { get; set; }
    public long TotalBytes { get; set; }

    public double Percentage
    {
        get
        {
            if (TotalBytes <= 0) return 0;
            return (double)BytesRead / TotalBytes * 100;
        }
    }
}
