using PocketMC.Domain.Models;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace PocketMC.Domain.Models
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
        public string? Hash { get; set; }
        public string? HashType { get; set; }
        public string? ReleaseType { get; set; } = "release";
        public List<string> Warnings { get; set; } = new();
        public List<MarketplaceDependency> Dependencies { get; set; } = new();
        public string? ClientSide { get; set; }
        public string? ServerSide { get; set; }
        public string? SelectedLoader { get; set; }
        public string? MatchedMinecraftVersion { get; set; }
        public string? IconUrl { get; set; }
    }

    public interface IAddonProvider
    {
        string Name { get; }
        Task<MarketplaceVersion?> GetLatestVersionAsync(string projectId, string mcVersion, string loader);
        Task<MarketplaceVersion?> GetLatestVersionAsync(string projectId, string mcVersion, IReadOnlyList<string> loaderCandidates);
        Task<MarketplaceVersion?> GetVersionByIdAsync(string versionId);
        Task<MarketplaceProjectInfo?> GetProjectInfoAsync(string projectId);
    }

    public class MarketplaceProjectInfo
    {
        public string Id { get; set; } = "";
        public string Title { get; set; } = "";
        public string Slug { get; set; } = "";
        public string? IconUrl { get; set; }
        public string? ClientSide { get; set; }
        public string? ServerSide { get; set; }
    }
}
