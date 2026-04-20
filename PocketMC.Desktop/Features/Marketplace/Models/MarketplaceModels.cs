using System.Collections.Generic;
using System.Threading.Tasks;

namespace PocketMC.Desktop.Features.Marketplace.Models
{
    public enum DependencyType
    {
        Required,
        Optional,
        Embedded,
        Incompatible
    }

    public class MarketplaceDependency
    {
        public string ProjectId { get; set; } = "";
        public string? VersionId { get; set; }
        public DependencyType Type { get; set; }
    }

    public class MarketplaceVersion
    {
        public string Id { get; set; } = "";
        public string Name { get; set; } = "";
        public string ProjectId { get; set; } = "";
        public string ProjectTitle { get; set; } = "";
        public string FileName { get; set; } = "";
        public string DownloadUrl { get; set; } = "";
        public List<MarketplaceDependency> Dependencies { get; set; } = new();
    }

    public interface IAddonProvider
    {
        string Name { get; }
        Task<MarketplaceVersion?> GetLatestVersionAsync(string projectId, string mcVersion, string loader);
        Task<MarketplaceVersion?> GetVersionByIdAsync(string versionId);
        Task<MarketplaceProjectInfo?> GetProjectInfoAsync(string projectId);
    }

    public class MarketplaceProjectInfo
    {
        public string Id { get; set; } = "";
        public string Title { get; set; } = "";
        public string Slug { get; set; } = "";
        public string? IconUrl { get; set; }
    }
}
