namespace PocketMC.RemoteControl.Models;

public class RemoteFileItemDto
{
    public string Name { get; set; } = string.Empty;
    public string RelativePath { get; set; } = string.Empty;
    public bool IsDirectory { get; set; }
    public long SizeBytes { get; set; }
    public DateTime LastModified { get; set; }
    public string Extension { get; set; } = string.Empty;
}

public class RemoteFileContentDto
{
    public string RelativePath { get; set; } = string.Empty;
    public string Content { get; set; } = string.Empty;
    public bool IsText { get; set; } = true;
    public bool IsTruncated { get; set; }
    public long SizeBytes { get; set; }
}

public class SaveRemoteFileContentRequest
{
    public string RelativePath { get; set; } = string.Empty;
    public string Content { get; set; } = string.Empty;
}
