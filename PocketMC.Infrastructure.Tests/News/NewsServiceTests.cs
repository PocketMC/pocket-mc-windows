using System.Net;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using PocketMC.Infrastructure.Configuration;
using PocketMC.Infrastructure.News;

namespace PocketMC.Infrastructure.Tests.News;

public sealed class NewsServiceTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "PocketMC.News.Tests", Guid.NewGuid().ToString("N"));

    [Fact]
    public async Task SynchronizeAsync_FreshInstallCachesNewsAndCreatesState()
    {
        NewsFixture fixture = CreateFixture();
        fixture.Remote.Add("a.txt", CreateNews("news-a", "2026-09-20T10:00:00Z"));
        fixture.Remote.Add("b.txt", CreateNews("news-b", "2026-09-21T10:00:00Z", popup: true));

        NewsSyncResult result = await fixture.Service.SynchronizeAsync();

        Assert.True(result.Succeeded, result.ErrorMessage);
        Assert.Equal(2, result.DownloadedCount);
        Assert.Equal(new[] { "news-b" }, result.PopupItems.Select(item => item.Metadata.Id));
        Assert.True(File.Exists(fixture.Service.StateFilePath));
        Assert.Equal(2, fixture.Service.GetCachedNews().Count);
        Assert.Equal("news-b", ReadState(fixture.Service.StateFilePath).GetProperty("lastFetchedNewsId").GetString());
    }

    [Fact]
    public async Task SynchronizeAsync_ExistingInstallDownloadsOnlyNewFilesAndDoesNotRepeatDownloads()
    {
        NewsFixture fixture = CreateFixture();
        fixture.Remote.Add("a.txt", CreateNews("news-a", "2026-09-20T10:00:00Z"));
        await fixture.Service.SynchronizeAsync();
        fixture.Handler.ResetRequests();
        fixture.Remote.Add("b.txt", CreateNews("news-b", "2026-09-21T10:00:00Z"));
        fixture.Remote.Add("c.txt", CreateNews("news-c", "2026-09-22T10:00:00Z"));

        NewsSyncResult result = await fixture.Service.SynchronizeAsync();
        int downloadsAfterNewFiles = fixture.Handler.FullDownloadCount;
        int metadataAfterNewFiles = fixture.Handler.MetadataRequestCount;
        NewsSyncResult secondSync = await fixture.Service.SynchronizeAsync();

        Assert.True(result.Succeeded, result.ErrorMessage);
        Assert.Equal(2, result.DownloadedCount);
        Assert.Equal("news-c", ReadState(fixture.Service.StateFilePath).GetProperty("lastFetchedNewsId").GetString());
        Assert.True(secondSync.Succeeded);
        Assert.Equal(downloadsAfterNewFiles, fixture.Handler.FullDownloadCount);
        Assert.Equal(metadataAfterNewFiles, fixture.Handler.MetadataRequestCount);
        Assert.Equal(3, fixture.Service.GetCachedNews().Count);
    }

    [Fact]
    public async Task SynchronizeAsync_FailedNewFileLeavesCursorAtLastSuccessfulItemAndRetries()
    {
        NewsFixture fixture = CreateFixture();
        fixture.Remote.Add("a.txt", CreateNews("news-a", "2026-09-20T10:00:00Z"));
        await fixture.Service.SynchronizeAsync();
        fixture.Remote.Add("b.txt", CreateNews("news-b", "2026-09-21T10:00:00Z"));
        fixture.Remote.Add("c.txt", CreateNews("news-c", "2026-09-22T10:00:00Z"));
        fixture.Handler.FailingFileName = "c.txt";

        NewsSyncResult failedResult = await fixture.Service.SynchronizeAsync();
        string cursorAfterFailure = ReadState(fixture.Service.StateFilePath).GetProperty("lastFetchedNewsId").GetString()!;
        fixture.Handler.FailingFileName = null;

        NewsSyncResult retryResult = await fixture.Service.SynchronizeAsync();

        Assert.True(failedResult.Succeeded);
        Assert.Equal("news-b", cursorAfterFailure);
        Assert.True(retryResult.Succeeded, retryResult.ErrorMessage);
        Assert.Equal("news-c", ReadState(fixture.Service.StateFilePath).GetProperty("lastFetchedNewsId").GetString());
        Assert.Contains(fixture.Service.GetCachedNews(), item => item.Metadata.Id == "news-c");
    }

    [Fact]
    public async Task SynchronizeAsync_FailedMiddleItemDoesNotAdvanceCursorPastIt()
    {
        NewsFixture fixture = CreateFixture();
        fixture.Remote.Add("a.txt", CreateNews("news-a", "2026-09-20T10:00:00Z"));
        fixture.Remote.Add("b.txt", CreateNews("news-b", "2026-09-21T10:00:00Z"));
        fixture.Remote.Add("c.txt", CreateNews("news-c", "2026-09-22T10:00:00Z"));
        fixture.Handler.FailingFileName = "b.txt";

        NewsSyncResult first = await fixture.Service.SynchronizeAsync();

        Assert.True(first.Succeeded, first.ErrorMessage);
        Assert.Equal("news-a", ReadState(fixture.Service.StateFilePath).GetProperty("lastFetchedNewsId").GetString());
        Assert.DoesNotContain(fixture.Service.GetCachedNews(), item => item.Metadata.Id == "news-c");

        fixture.Handler.FailingFileName = null;
        NewsSyncResult retry = await fixture.Service.SynchronizeAsync();

        Assert.True(retry.Succeeded, retry.ErrorMessage);
        Assert.Equal("news-c", ReadState(fixture.Service.StateFilePath).GetProperty("lastFetchedNewsId").GetString());
        Assert.Equal(3, fixture.Service.GetCachedNews().Count);
    }

    [Fact]
    public async Task SynchronizeAsync_OfflineKeepsCachedNewsAndPopupAcknowledgement()
    {
        NewsFixture fixture = CreateFixture();
        fixture.Remote.Add("popup.txt", CreateNews("important-news", "2026-09-20T10:00:00Z", popup: true));
        NewsSyncResult initial = await fixture.Service.SynchronizeAsync();
        fixture.Service.MarkRead("important-news");
        NewsSyncResult acknowledgedSync = await fixture.Service.SynchronizeAsync();
        fixture.Handler.IsOffline = true;

        NewsSyncResult offline = await fixture.Service.SynchronizeAsync();

        Assert.Single(initial.PopupItems);
        Assert.Empty(acknowledgedSync.PopupItems);
        Assert.Equal(1, fixture.Handler.FullDownloadCount);
        Assert.False(offline.Succeeded);
        Assert.Contains("important-news", fixture.Service.GetReadNewsIds());
        Assert.Contains("read: true", File.ReadAllText(Path.Combine(fixture.Service.CacheDirectory, "popup.txt")), StringComparison.Ordinal);
        Assert.DoesNotContain("acknowledgedNewsIds", File.ReadAllText(fixture.Service.StateFilePath), StringComparison.Ordinal);
        Assert.Single(fixture.Service.GetCachedNews());
        Assert.Equal("important-news", ReadState(fixture.Service.StateFilePath).GetProperty("lastFetchedNewsId").GetString());
    }

    [Fact]
    public async Task SynchronizeAsync_RemoteContentRefreshPreservesLocalReadMarker()
    {
        NewsFixture fixture = CreateFixture();
        fixture.Remote.Add("item.txt", CreateNews("stable-news-id", "2026-09-20T10:00:00Z", popup: true));
        await fixture.Service.SynchronizeAsync();
        fixture.Service.MarkRead("stable-news-id");
        fixture.Remote["item.txt"] = CreateNews("stable-news-id", "2026-09-21T10:00:00Z", popup: true)
            .Replace("Test content for stable-news-id.", "Updated text for this same news item.", StringComparison.Ordinal);

        NewsSyncResult refreshed = await fixture.Service.SynchronizeAsync();
        NewsItem cached = Assert.Single(fixture.Service.GetCachedNews());

        Assert.True(refreshed.Succeeded, refreshed.ErrorMessage);
        Assert.True(cached.Metadata.IsRead);
        Assert.Contains("read: true", File.ReadAllText(Path.Combine(fixture.Service.CacheDirectory, "item.txt")), StringComparison.Ordinal);
        Assert.Empty(refreshed.PopupItems);
        Assert.Contains("Updated text", cached.RawText, StringComparison.Ordinal);
    }

    [Fact]
    public async Task SynchronizeAsync_CachedReadMarkerSurvivesServiceRestart()
    {
        NewsFixture firstLaunch = CreateFixture();
        firstLaunch.Remote.Add("popup.txt", CreateNews("persistent-read", "2026-09-20T10:00:00Z", popup: true));
        await firstLaunch.Service.SynchronizeAsync();
        firstLaunch.Service.MarkRead("persistent-read");

        NewsFixture nextLaunch = CreateFixture();
        nextLaunch.Remote.Add("popup.txt", CreateNews("persistent-read", "2026-09-20T10:00:00Z", popup: true));
        NewsSyncResult result = await nextLaunch.Service.SynchronizeAsync();

        Assert.True(result.Succeeded, result.ErrorMessage);
        Assert.Empty(result.PopupItems);
        Assert.Contains("persistent-read", nextLaunch.Service.GetReadNewsIds());
        Assert.Equal(0, nextLaunch.Handler.FullDownloadCount);
    }

    [Fact]
    public async Task GetUnreadNewsCount_CountsOnlyActiveVersionEligibleUnreadItems()
    {
        NewsFixture fixture = CreateFixture();
        fixture.Remote.Add("unread-a.txt", CreateNews("unread-a", "2026-09-20T10:00:00Z"));
        fixture.Remote.Add("unread-b.txt", CreateNews("unread-b", "2026-09-21T10:00:00Z"));
        fixture.Remote.Add("expired.txt", CreateNews("expired", "2026-09-19T10:00:00Z", expires: "2026-09-20T10:00:00Z"));
        fixture.Remote.Add("wrong-version.txt", CreateNews("wrong-version", "2026-09-22T10:00:00Z", minVersion: "2.0.0"));
        int stateChanges = 0;
        fixture.Service.NewsStateChanged += () => stateChanges++;

        await fixture.Service.SynchronizeAsync();

        Assert.Equal(2, fixture.Service.GetUnreadNewsCount());
        Assert.Equal(1, stateChanges);
        fixture.Service.MarkRead("unread-a");
        Assert.Equal(1, fixture.Service.GetUnreadNewsCount());
        Assert.Equal(2, stateChanges);
    }

    [Fact]
    public async Task SynchronizeAsync_UnacknowledgedPopupIsOfferedAgainWithoutRedownloading()
    {
        NewsFixture fixture = CreateFixture();
        fixture.Remote.Add("popup.txt", CreateNews("important-news", "2026-09-20T10:00:00Z", popup: true));
        NewsSyncResult first = await fixture.Service.SynchronizeAsync();
        fixture.Handler.ResetRequests();

        NewsSyncResult second = await fixture.Service.SynchronizeAsync();

        Assert.Single(first.PopupItems);
        Assert.Single(second.PopupItems);
        Assert.Equal(0, fixture.Handler.FullDownloadCount);
    }

    [Fact]
    public async Task SynchronizeAsync_ExpiredAndWrongVersionItemsAreNotCached()
    {
        NewsFixture fixture = CreateFixture(currentVersion: "1.9.9.1");
        fixture.Remote.Add("expired.txt", CreateNews("expired-news", "2026-09-20T10:00:00Z", expires: "2026-09-21T10:00:00Z"));
        fixture.Remote.Add("wrong-version.txt", CreateNews("wrong-version", "2026-09-22T10:00:00Z", minVersion: "2.0.0"));

        NewsSyncResult result = await fixture.Service.SynchronizeAsync();

        Assert.True(result.Succeeded, result.ErrorMessage);
        Assert.Empty(fixture.Service.GetCachedNews());
        Assert.Equal(0, fixture.Handler.FullDownloadCount);
    }

    [Fact]
    public async Task SynchronizeAsync_InvalidRemoteFileIsLoggedAndNotRefetchedUntilItsBlobChanges()
    {
        NewsFixture fixture = CreateFixture();
        fixture.Remote.Add("broken.txt", "---\nid: broken\nunknown: field\n---\ntitle: Broken\n");

        NewsSyncResult first = await fixture.Service.SynchronizeAsync();
        int metadataRequests = fixture.Handler.MetadataRequestCount;
        NewsSyncResult second = await fixture.Service.SynchronizeAsync();

        Assert.True(first.Succeeded, first.ErrorMessage);
        Assert.Equal(1, first.InvalidCount);
        Assert.True(second.Succeeded, second.ErrorMessage);
        Assert.Equal(metadataRequests, fixture.Handler.MetadataRequestCount);
        Assert.Empty(fixture.Service.GetCachedNews());
    }

    [Fact]
    public async Task SynchronizeAsync_VersionChangeReevaluatesPreviouslyTargetedNews()
    {
        NewsFixture fixture = CreateFixture(currentVersion: "1.9.9.1");
        fixture.Remote.Add("targeted.txt", CreateNews("targeted-news", "2026-09-20T10:00:00Z", minVersion: "2.0.0"));
        await fixture.Service.SynchronizeAsync();
        fixture.Service.Dispose();

        NewsFixture upgraded = CreateFixture(currentVersion: "2.0.0");
        upgraded.Remote.Add("targeted.txt", CreateNews("targeted-news", "2026-09-20T10:00:00Z", minVersion: "2.0.0"));

        NewsSyncResult result = await upgraded.Service.SynchronizeAsync();

        Assert.True(result.Succeeded);
        Assert.Contains(upgraded.Service.GetCachedNews(), item => item.Metadata.Id == "targeted-news");
    }

    private NewsFixture CreateFixture(string currentVersion = "1.9.9.1")
    {
        Directory.CreateDirectory(_root);
        string settingsPath = Path.Combine(_root, "settings.json");
        SettingsManager settingsManager = new(settingsPath);
        FakeNewsHandler handler = new();
        HttpClient client = new(handler);
        Mock<IHttpClientFactory> factory = new();
        factory.Setup(item => item.CreateClient("PocketMC.News")).Returns(() => new HttpClient(handler));
        NewsService service = new(settingsManager, factory.Object, new NewsTextParser(), NullLogger<NewsService>.Instance, () => currentVersion);
        return new NewsFixture(service, handler);
    }

    private static string CreateNews(
        string id,
        string published,
        bool popup = false,
        string? expires = null,
        string? minVersion = null,
        string? maxVersion = null)
        => $"""
        ---
        id: {id}
        type: announcement
        priority: normal
        popup: {popup.ToString().ToLowerInvariant()}
        published: {published}
        expires: {expires ?? string.Empty}
        minVersion: {minVersion ?? string.Empty}
        maxVersion: {maxVersion ?? string.Empty}
        ---
        title: {id}

        paragraph:
        Test content for {id}.
        """;

    private static JsonElement ReadState(string path)
    {
        using JsonDocument document = JsonDocument.Parse(File.ReadAllText(path));
        return document.RootElement.Clone();
    }

    public void Dispose()
    {
        if (Directory.Exists(_root)) Directory.Delete(_root, recursive: true);
    }

    private sealed record NewsFixture(NewsService Service, FakeNewsHandler Handler)
    {
        public Dictionary<string, string> Remote => Handler.Files;
    }

    private sealed class FakeNewsHandler : HttpMessageHandler
    {
        public Dictionary<string, string> Files { get; } = new(StringComparer.OrdinalIgnoreCase);
        public string? FailingFileName { get; set; }
        public bool IsOffline { get; set; }
        public int FullDownloadCount { get; private set; }
        public int MetadataRequestCount { get; private set; }

        public void ResetRequests()
        {
            FullDownloadCount = 0;
            MetadataRequestCount = 0;
        }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            if (IsOffline) throw new HttpRequestException("Offline test transport.");

            if (request.RequestUri?.AbsoluteUri.StartsWith("https://api.github.com/repos/PocketMC/pocket-mc-windows/contents/news", StringComparison.Ordinal) == true)
            {
                object[] listing = Files.OrderBy(pair => pair.Key, StringComparer.Ordinal).Select(pair => new
                {
                    type = "file",
                    name = pair.Key,
                    sha = GetGitBlobSha(Encoding.UTF8.GetBytes(pair.Value)),
                    download_url = $"https://raw.githubusercontent.com/PocketMC/pocket-mc-windows/master/news/{pair.Key}"
                }).Cast<object>().ToArray();
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent(JsonSerializer.Serialize(listing), Encoding.UTF8, "application/json")
                });
            }

            string fileName = request.RequestUri?.Segments.LastOrDefault() ?? string.Empty;
            if (request.Headers.Range == null && string.Equals(fileName, FailingFileName, StringComparison.OrdinalIgnoreCase))
            {
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.ServiceUnavailable));
            }

            if (!Files.TryGetValue(fileName, out string? contents))
            {
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.NotFound));
            }

            if (request.Headers.Range == null) FullDownloadCount++;
            else MetadataRequestCount++;
            return Task.FromResult(new HttpResponseMessage(request.Headers.Range == null ? HttpStatusCode.OK : HttpStatusCode.PartialContent)
            {
                Content = new StringContent(contents, Encoding.UTF8, "text/plain")
            });
        }

        private static string GetGitBlobSha(byte[] content)
        {
            byte[] header = Encoding.ASCII.GetBytes($"blob {content.Length}\0");
            byte[] blob = header.Concat(content).ToArray();
            return Convert.ToHexString(SHA1.HashData(blob)).ToLowerInvariant();
        }
    }
}