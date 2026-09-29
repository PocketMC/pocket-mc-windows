using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using PocketMC.Domain.Storage;
using PocketMC.Infrastructure.Configuration;

namespace PocketMC.Infrastructure.News;

public sealed record NewsSyncResult(
    bool Succeeded,
    int DownloadedCount,
    int InvalidCount,
    IReadOnlyList<NewsItem> PopupItems,
    string? ErrorMessage = null);

public sealed class NewsService : IDisposable
{
    public static readonly TimeSpan SyncInterval = TimeSpan.FromHours(6);
    private static readonly TimeSpan RequestTimeout = TimeSpan.FromSeconds(20);
    private const string RepositoryContentsUrl = "https://api.github.com/repos/PocketMC/pocket-mc-windows/contents/news?ref=master";
    private const string RawNewsPathPrefix = "/PocketMC/pocket-mc-windows/master/news/";
    private const int MaxMetadataBytes = 32 * 1024;
    private const int MaxNewsFileBytes = 256 * 1024;
    private const int MaxNewsFiles = 1000;
    private const int MaxDirectoryListingBytes = 1024 * 1024;
    private static readonly JsonSerializerOptions StateJsonOptions = new() { WriteIndented = true };

    private readonly SettingsManager _settingsManager;
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly NewsTextParser _parser;
    private readonly ILogger<NewsService> _logger;
    private readonly Func<string> _getCurrentVersion;
    private readonly string _newsRoot;
    private readonly string _cacheDirectory;
    private readonly string _statePath;
    private readonly SemaphoreSlim _syncGate = new(1, 1);
    private readonly object _stateLock = new();
    private NewsPersistentState? _state;
    private bool _stateIsCorrupt;
    private CancellationTokenSource? _lifetime;
    private int _isStarted;

    public event Action<IReadOnlyList<NewsItem>>? PopupNewsAvailable;

    public NewsService(
        SettingsManager settingsManager,
        IHttpClientFactory httpClientFactory,
        NewsTextParser parser,
        ILogger<NewsService> logger,
        Func<string>? getCurrentVersion = null)
    {
        _settingsManager = settingsManager;
        _httpClientFactory = httpClientFactory;
        _parser = parser;
        _logger = logger;
        _getCurrentVersion = getCurrentVersion ?? (() => AppConfig.AppVersion);

        _newsRoot = Path.Combine(_settingsManager.GetPersistentDataDirectory(), "news");
        _cacheDirectory = Path.Combine(_newsRoot, "cache");
        _statePath = Path.Combine(_newsRoot, "state.json");
    }

    public string CacheDirectory => _cacheDirectory;
    public string StateFilePath => _statePath;

    public IReadOnlyList<NewsItem> GetCachedNews()
    {
        if (!Directory.Exists(_cacheDirectory)) return Array.Empty<NewsItem>();

        Version? currentVersion = ParseVersion(_getCurrentVersion());
        List<NewsItem> items = new();
        foreach (string path in Directory.EnumerateFiles(_cacheDirectory, "*.txt", SearchOption.TopDirectoryOnly))
        {
            try
            {
                NewsItem item = _parser.Parse(Path.GetFileName(path), File.ReadAllText(path, Encoding.UTF8));
                if (currentVersion == null || IsForVersion(item.Metadata, currentVersion)) items.Add(item);
            }
            catch (Exception ex) when (ex is IOException or InvalidDataException or UnauthorizedAccessException)
            {
                _logger.LogWarning(ex, "Skipping invalid cached news file {NewsPath}.", path);
            }
        }

        return items.OrderBy(item => item.Metadata.PublishedUtc).ThenBy(item => item.Metadata.Id, StringComparer.Ordinal).ToArray();
    }

    public IReadOnlySet<string> GetAcknowledgedNewsIds()
    {
        lock (_stateLock)
        {
            NewsPersistentState state = LoadStateLocked();
            return new HashSet<string>(state.AcknowledgedNewsIds, StringComparer.Ordinal);
        }
    }

    public void Acknowledge(string newsId)
    {
        if (string.IsNullOrWhiteSpace(newsId)) return;
        lock (_stateLock)
        {
            NewsPersistentState state = LoadStateLocked();
            if (_stateIsCorrupt) return;
            if (state.AcknowledgedNewsIds.Add(newsId)) SaveStateLocked(state);
        }
    }

    public void Start()
    {
        if (Interlocked.Exchange(ref _isStarted, 1) != 0) return;
        _lifetime = new CancellationTokenSource();
        _ = RunPeriodicSyncAsync(_lifetime.Token);
    }

    public void Stop()
    {
        _lifetime?.Cancel();
    }

    public async Task<NewsSyncResult> SynchronizeAsync(CancellationToken cancellationToken = default)
    {
        await _syncGate.WaitAsync(cancellationToken);
        try
        {
            return await SynchronizeCoreAsync(cancellationToken);
        }
        finally
        {
            _syncGate.Release();
        }
    }

    private async Task<NewsSyncResult> SynchronizeCoreAsync(CancellationToken cancellationToken)
    {
        CancellationToken token = cancellationToken;

        try
        {
            Directory.CreateDirectory(_cacheDirectory);
            using HttpClient client = CreateClient();
            List<RemoteNewsFile> remoteFiles = await DiscoverFilesAsync(client, token);
            NewsPersistentState state;
            lock (_stateLock)
            {
                state = CloneState(LoadStateLocked());
                if (_stateIsCorrupt)
                {
                    throw new InvalidDataException("News state.json is invalid; existing local state was preserved.");
                }
            }

            string currentVersionText = _getCurrentVersion();
            Version currentVersion = ParseVersion(currentVersionText)
                ?? throw new InvalidDataException($"Pocket MC version '{currentVersionText}' is invalid.");
            bool appVersionChanged = !string.Equals(state.LastEvaluatedAppVersion, currentVersionText, StringComparison.Ordinal);
            if (appVersionChanged)
            {
                state.KnownRemoteBlobShas.Clear();
                state.LastEvaluatedAppVersion = currentVersionText;
            }

            HashSet<string> currentFileNames = remoteFiles.Select(file => file.Name).ToHashSet(StringComparer.OrdinalIgnoreCase);
            foreach (string removedFile in state.KnownRemoteBlobShas.Keys.Where(name => !currentFileNames.Contains(name)).ToArray())
            {
                state.KnownRemoteBlobShas.Remove(removedFile);
            }

            HashSet<string> cachedIds = GetCachedNews().Select(item => item.Metadata.Id).ToHashSet(StringComparer.Ordinal);
            List<NewsCandidate> candidates = new();
            int invalidCount = 0;
            bool metadataDiscoveryIncomplete = false;
            foreach (RemoteNewsFile remoteFile in remoteFiles)
            {
                token.ThrowIfCancellationRequested();
                if (state.KnownRemoteBlobShas.TryGetValue(remoteFile.Name, out string? knownSha) &&
                    string.Equals(knownSha, remoteFile.Sha, StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                string cachePath = GetCachePath(remoteFile.Name);
                if (File.Exists(cachePath))
                {
                    try
                    {
                        byte[] cachedBytes = await File.ReadAllBytesAsync(cachePath, token);
                        if (string.Equals(GetGitBlobSha(cachedBytes), remoteFile.Sha, StringComparison.OrdinalIgnoreCase))
                        {
                            NewsItem cachedItem = _parser.Parse(remoteFile.Name, new UTF8Encoding(false, true).GetString(cachedBytes));
                            cachedIds.Add(cachedItem.Metadata.Id);
                            state.KnownRemoteBlobShas[remoteFile.Name] = remoteFile.Sha;
                            if (cachedItem.Metadata.PublishedUtc <= DateTimeOffset.UtcNow &&
                                IsForVersion(cachedItem.Metadata, currentVersion) &&
                                (cachedItem.Metadata.ExpiresUtc == null || cachedItem.Metadata.ExpiresUtc > DateTimeOffset.UtcNow))
                            {
                                AdvanceLastFetched(state, cachedItem.Metadata);
                            }
                            SaveState(state);
                            continue;
                        }
                    }
                    catch (InvalidDataException ex)
                    {
                        invalidCount++;
                        state.KnownRemoteBlobShas[remoteFile.Name] = remoteFile.Sha;
                        SaveState(state);
                        _logger.LogWarning(ex, "Skipping invalid cached news file {NewsPath} until its repository content changes.", cachePath);
                        continue;
                    }
                    catch (DecoderFallbackException ex)
                    {
                        invalidCount++;
                        state.KnownRemoteBlobShas[remoteFile.Name] = remoteFile.Sha;
                        SaveState(state);
                        _logger.LogWarning(ex, "Skipping non-UTF8 cached news file {NewsPath} until its repository content changes.", cachePath);
                        continue;
                    }
                    catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                    {
                        _logger.LogWarning(ex, "Could not inspect cached news file {NewsPath}.", cachePath);
                    }
                }

                try
                {
                    MetadataDownload metadataDownload = await DownloadMetadataAsync(client, remoteFile, token);
                    NewsMetadata metadata = _parser.ParseMetadataHeader(metadataDownload.Prefix);
                    bool alreadyCached = cachedIds.Contains(metadata.Id);
                    bool afterCursor = IsAfterLastFetched(metadata, state);
                    bool versionRelevant = IsForVersion(metadata, currentVersion);
                    DateTimeOffset metadataNow = DateTimeOffset.UtcNow;
                    bool active = metadata.ExpiresUtc == null || metadata.ExpiresUtc > metadataNow;

                    if (metadata.PublishedUtc > metadataNow)
                    {
                        continue;
                    }

                    if (!alreadyCached && !afterCursor && !(appVersionChanged && versionRelevant))
                    {
                        state.KnownRemoteBlobShas[remoteFile.Name] = remoteFile.Sha;
                        SaveState(state);
                        continue;
                    }

                    if (!versionRelevant || !active)
                    {
                        state.KnownRemoteBlobShas[remoteFile.Name] = remoteFile.Sha;
                        SaveState(state);
                        continue;
                    }

                    candidates.Add(new NewsCandidate(remoteFile, metadata, metadataDownload.CompleteBytes));
                }
                catch (OperationCanceledException) when (token.IsCancellationRequested)
                {
                    throw;
                }
                catch (Exception ex) when (ex is InvalidDataException or DecoderFallbackException)
                {
                    invalidCount++;
                    state.KnownRemoteBlobShas[remoteFile.Name] = remoteFile.Sha;
                    SaveState(state);
                    _logger.LogWarning(ex, "Skipping invalid remote news file {NewsFile} until its repository content changes.", remoteFile.Name);
                }
                catch (Exception ex) when (ex is HttpRequestException or IOException or InvalidDataException or JsonException or DecoderFallbackException)
                {
                    invalidCount++;
                    metadataDiscoveryIncomplete = true;
                    _logger.LogWarning(ex, "Could not inspect remote news file {NewsFile}; it will be retried on the next sync.", remoteFile.Name);
                }
            }

            candidates.Sort((left, right) => CompareNewsOrder(left.Metadata, right.Metadata));
            if (metadataDiscoveryIncomplete)
            {
                _logger.LogWarning("News metadata discovery was incomplete; deferring candidate downloads to preserve chronological cursor ordering.");
                candidates.Clear();
            }

            int downloadedCount = 0;

            foreach (NewsCandidate candidate in candidates)
            {
                token.ThrowIfCancellationRequested();
                byte[] contentBytes;
                try
                {
                    contentBytes = candidate.CompleteBytes ?? await DownloadFullNewsAsync(client, candidate.RemoteFile, token);
                }
                catch (OperationCanceledException) when (token.IsCancellationRequested)
                {
                    throw;
                }
                catch (Exception ex) when (ex is HttpRequestException or IOException)
                {
                    invalidCount++;
                    _logger.LogWarning(ex, "Could not download or process remote news file {NewsFile}; it will be retried on the next sync.", candidate.RemoteFile.Name);
                    break;
                }

                if (!string.Equals(GetGitBlobSha(contentBytes), candidate.RemoteFile.Sha, StringComparison.OrdinalIgnoreCase))
                {
                    invalidCount++;
                    _logger.LogWarning("Downloaded news file {NewsFile} did not match its discovered GitHub blob SHA; later items were not processed.", candidate.RemoteFile.Name);
                    break;
                }

                NewsItem item;
                try
                {
                    string text = new UTF8Encoding(false, true).GetString(contentBytes);
                    item = _parser.Parse(candidate.RemoteFile.Name, text);
                }
                catch (Exception ex) when (ex is InvalidDataException or DecoderFallbackException)
                {
                    invalidCount++;
                    state.KnownRemoteBlobShas[candidate.RemoteFile.Name] = candidate.RemoteFile.Sha;
                    SaveState(state);
                    _logger.LogWarning(ex, "Skipping invalid news item {NewsFile} until its repository content changes.", candidate.RemoteFile.Name);
                    continue;
                }

                if (item.Metadata.Id != candidate.Metadata.Id || item.Metadata.PublishedUtc != candidate.Metadata.PublishedUtc)
                {
                    invalidCount++;
                    _logger.LogWarning("News metadata changed between discovery and content download for {NewsFile}; later items were not processed.", candidate.RemoteFile.Name);
                    break;
                }

                if (cachedIds.Contains(item.Metadata.Id) && !File.Exists(GetCachePath(item.FileName)))
                {
                    invalidCount++;
                    state.KnownRemoteBlobShas[candidate.RemoteFile.Name] = candidate.RemoteFile.Sha;
                    SaveState(state);
                    _logger.LogWarning("Skipping duplicate news id {NewsId} in {NewsFile}.", item.Metadata.Id, candidate.RemoteFile.Name);
                    continue;
                }

                try
                {
                    await WriteCacheAtomicallyAsync(GetCachePath(candidate.RemoteFile.Name), contentBytes, token);
                    cachedIds.Add(item.Metadata.Id);
                    state.KnownRemoteBlobShas[candidate.RemoteFile.Name] = candidate.RemoteFile.Sha;
                    AdvanceLastFetched(state, item.Metadata);
                    SaveState(state);
                    downloadedCount++;
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                {
                    invalidCount++;
                    _logger.LogWarning(ex, "Could not cache news item {NewsFile}; later items were not processed.", candidate.RemoteFile.Name);
                    break;
                }
            }

            SaveState(state);
            HashSet<string> acknowledgedIds = new(GetAcknowledgedNewsIds(), StringComparer.Ordinal);
            DateTimeOffset now = DateTimeOffset.UtcNow;
            IReadOnlyList<NewsItem> popupResult = GetCachedNews()
                .Where(item => item.Metadata.Popup &&
                               !acknowledgedIds.Contains(item.Metadata.Id) &&
                               (item.Metadata.ExpiresUtc == null || item.Metadata.ExpiresUtc > now))
                .OrderBy(item => item.Metadata.PublishedUtc)
                .ThenBy(item => item.Metadata.Id, StringComparer.Ordinal)
                .ToArray();
            if (popupResult.Count > 0)
            {
                try { PopupNewsAvailable?.Invoke(popupResult); }
                catch (Exception ex) { _logger.LogWarning(ex, "A News popup subscriber failed."); }
            }

            _logger.LogInformation("News sync completed: {DownloadedCount} downloaded, {InvalidCount} skipped or retryable.", downloadedCount, invalidCount);
            return new NewsSyncResult(true, downloadedCount, invalidCount, popupResult);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            _logger.LogInformation("News synchronization was canceled.");
            return new NewsSyncResult(false, 0, 0, Array.Empty<NewsItem>(), "Synchronization canceled.");
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "News synchronization failed; cached news and local state were retained.");
            return new NewsSyncResult(false, 0, 0, Array.Empty<NewsItem>(), ex.Message);
        }
    }

    private async Task RunPeriodicSyncAsync(CancellationToken cancellationToken)
    {
        try
        {
            await SynchronizeAsync(cancellationToken);
            using PeriodicTimer timer = new(SyncInterval);
            while (await timer.WaitForNextTickAsync(cancellationToken))
            {
                await SynchronizeAsync(cancellationToken);
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "News sync background loop stopped unexpectedly.");
        }
    }

    private async Task<List<RemoteNewsFile>> DiscoverFilesAsync(HttpClient client, CancellationToken cancellationToken)
    {
        using CancellationTokenSource requestTimeout = CreateRequestTimeout(cancellationToken);
        CancellationToken requestToken = requestTimeout.Token;
        using HttpResponseMessage response = await client.GetAsync(RepositoryContentsUrl, HttpCompletionOption.ResponseHeadersRead, requestToken);
        response.EnsureSuccessStatusCode();
        byte[] listingBytes = await ReadAtMostAsync(response.Content, MaxDirectoryListingBytes + 1, requestToken);
        if (listingBytes.Length > MaxDirectoryListingBytes)
        {
            throw new InvalidDataException("GitHub news directory response exceeded the allowed size.");
        }

        using JsonDocument document = JsonDocument.Parse(listingBytes);
        if (document.RootElement.ValueKind != JsonValueKind.Array)
        {
            throw new InvalidDataException("GitHub news directory response was not a file list.");
        }

        List<RemoteNewsFile> files = new();
        foreach (JsonElement entry in document.RootElement.EnumerateArray())
        {
            if (!entry.TryGetProperty("type", out JsonElement type) || type.GetString() != "file" ||
                !entry.TryGetProperty("name", out JsonElement nameElement))
            {
                continue;
            }

            string name = nameElement.GetString() ?? string.Empty;
            if (!IsSafeNewsFileName(name)) continue;

            string sha = GetRequiredString(entry, "sha");
            string downloadUrl = GetRequiredString(entry, "download_url");
            if (!Uri.TryCreate(downloadUrl, UriKind.Absolute, out Uri? uri) ||
                uri.Scheme != Uri.UriSchemeHttps ||
                !uri.Host.Equals("raw.githubusercontent.com", StringComparison.OrdinalIgnoreCase) ||
                !uri.AbsolutePath.StartsWith(RawNewsPathPrefix, StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidDataException($"GitHub returned an unexpected download URL for '{name}'.");
            }

            files.Add(new RemoteNewsFile(name, sha, uri));
            if (files.Count > MaxNewsFiles) throw new InvalidDataException("GitHub news directory exceeded its file limit.");
        }

        return files;
    }

    private async Task<MetadataDownload> DownloadMetadataAsync(HttpClient client, RemoteNewsFile remoteFile, CancellationToken cancellationToken)
    {
        for (int byteLimit = 4096; byteLimit <= MaxMetadataBytes; byteLimit *= 2)
        {
            using CancellationTokenSource requestTimeout = CreateRequestTimeout(cancellationToken);
            using HttpRequestMessage request = new(HttpMethod.Get, remoteFile.DownloadUri);
            request.Headers.Range = new RangeHeaderValue(0, byteLimit - 1);
            using HttpResponseMessage response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, requestTimeout.Token);
            if (response.StatusCode is not (HttpStatusCode.PartialContent or HttpStatusCode.OK)) response.EnsureSuccessStatusCode();

            bool isCompleteResponse = response.StatusCode == HttpStatusCode.OK && response.Content.Headers.ContentLength is long length && length <= byteLimit;
            byte[] bytes = await ReadAtMostAsync(response.Content, byteLimit, requestTimeout.Token);
            string prefix = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: true).GetString(bytes);
            if (HasClosedMetadataHeader(prefix))
            {
                return new MetadataDownload(prefix, isCompleteResponse ? bytes : null);
            }

            if (isCompleteResponse || bytes.Length < byteLimit)
            {
                throw new InvalidDataException($"News metadata header in '{remoteFile.Name}' is incomplete.");
            }
        }

        throw new InvalidDataException($"News metadata header in '{remoteFile.Name}' exceeds the allowed size.");
    }

    private async Task<byte[]> DownloadFullNewsAsync(HttpClient client, RemoteNewsFile remoteFile, CancellationToken cancellationToken)
    {
        using CancellationTokenSource requestTimeout = CreateRequestTimeout(cancellationToken);
        using HttpResponseMessage response = await client.GetAsync(remoteFile.DownloadUri, HttpCompletionOption.ResponseHeadersRead, requestTimeout.Token);
        response.EnsureSuccessStatusCode();
        byte[] bytes = await ReadAtMostAsync(response.Content, MaxNewsFileBytes + 1, requestTimeout.Token);
        if (bytes.Length > MaxNewsFileBytes) throw new InvalidDataException($"News file '{remoteFile.Name}' exceeds the allowed size.");
        return bytes;
    }

    private static CancellationTokenSource CreateRequestTimeout(CancellationToken cancellationToken)
    {
        CancellationTokenSource timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(RequestTimeout);
        return timeout;
    }

    private static async Task<byte[]> ReadAtMostAsync(HttpContent content, int maxBytes, CancellationToken cancellationToken)
    {
        await using Stream stream = await content.ReadAsStreamAsync(cancellationToken);
        using MemoryStream output = new(Math.Min(maxBytes, 8192));
        byte[] buffer = new byte[8192];
        while (output.Length < maxBytes)
        {
            int wanted = (int)Math.Min(buffer.Length, maxBytes - output.Length);
            int read = await stream.ReadAsync(buffer.AsMemory(0, wanted), cancellationToken);
            if (read == 0) break;
            output.Write(buffer, 0, read);
        }

        return output.ToArray();
    }

    private static bool HasClosedMetadataHeader(string prefix)
    {
        string normalized = prefix.Replace("\r\n", "\n", StringComparison.Ordinal).Replace('\r', '\n');
        return normalized.StartsWith("---\n", StringComparison.Ordinal) && normalized.IndexOf("\n---\n", StringComparison.Ordinal) >= 0;
    }

    private HttpClient CreateClient()
    {
        HttpClient client = _httpClientFactory.CreateClient("PocketMC.News");
        client.Timeout = RequestTimeout;
        return client;
    }

    private string GetCachePath(string fileName)
    {
        if (!IsSafeNewsFileName(fileName)) throw new InvalidDataException("News cache filename is invalid.");
        return Path.Combine(_cacheDirectory, fileName);
    }

    private static bool IsSafeNewsFileName(string name)
        => !string.IsNullOrWhiteSpace(name) && Path.GetFileName(name) == name &&
           name.EndsWith(".txt", StringComparison.OrdinalIgnoreCase) &&
           name.Length <= 128 && System.Text.RegularExpressions.Regex.IsMatch(
               name, @"^[A-Za-z0-9][A-Za-z0-9._-]*\.txt$", System.Text.RegularExpressions.RegexOptions.IgnoreCase | System.Text.RegularExpressions.RegexOptions.CultureInvariant, TimeSpan.FromSeconds(1));

    private static string GetRequiredString(JsonElement element, string propertyName)
        => element.TryGetProperty(propertyName, out JsonElement property) && property.ValueKind == JsonValueKind.String &&
           !string.IsNullOrWhiteSpace(property.GetString())
            ? property.GetString()!
            : throw new InvalidDataException($"GitHub response is missing '{propertyName}'.");

    private static string GetGitBlobSha(byte[] content)
    {
        using IncrementalHash hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA1);
        byte[] header = Encoding.ASCII.GetBytes($"blob {content.Length}\0");
        hash.AppendData(header);
        hash.AppendData(content);
        return Convert.ToHexString(hash.GetHashAndReset()).ToLowerInvariant();
    }

    private static async Task WriteCacheAtomicallyAsync(string path, byte[] content, CancellationToken cancellationToken)
    {
        string tempPath = path + $".{Guid.NewGuid():N}.tmp";
        try
        {
            await File.WriteAllBytesAsync(tempPath, content, cancellationToken);
            File.Move(tempPath, path, overwrite: true);
        }
        finally
        {
            try { if (File.Exists(tempPath)) File.Delete(tempPath); }
            catch (IOException) { }
        }
    }

    private NewsPersistentState LoadStateLocked()
    {
        if (_state != null) return _state;
        if (!File.Exists(_statePath)) return _state = new NewsPersistentState();

        try
        {
            string content = File.ReadAllText(_statePath);
            _state = JsonSerializer.Deserialize<NewsPersistentState>(content) ?? new NewsPersistentState();
            _state.KnownRemoteBlobShas ??= new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            _state.AcknowledgedNewsIds ??= new HashSet<string>(StringComparer.Ordinal);
            return _state;
        }
        catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException)
        {
            _stateIsCorrupt = true;
            _logger.LogError(ex, "News state file {StatePath} is unreadable; preserving it and keeping cached news available.", _statePath);
            return _state = new NewsPersistentState();
        }
    }

    private void SaveState(NewsPersistentState state)
    {
        lock (_stateLock) SaveStateLocked(state);
    }

    private void SaveStateLocked(NewsPersistentState state)
    {
        if (_stateIsCorrupt) return;
        if (_state != null)
        {
            state.AcknowledgedNewsIds.UnionWith(_state.AcknowledgedNewsIds);
        }

        Directory.CreateDirectory(_newsRoot);
        NewsPersistentState snapshot = CloneState(state);
        string json = JsonSerializer.Serialize(snapshot, StateJsonOptions);
        FileUtils.AtomicWriteAllText(_statePath, json);
        _state = snapshot;
    }

    private static NewsPersistentState CloneState(NewsPersistentState state)
        => new()
        {
            LastFetchedNewsId = state.LastFetchedNewsId,
            LastFetchedPublishedUtc = state.LastFetchedPublishedUtc,
            AcknowledgedNewsIds = new HashSet<string>(state.AcknowledgedNewsIds, StringComparer.Ordinal),
            KnownRemoteBlobShas = new Dictionary<string, string>(state.KnownRemoteBlobShas, StringComparer.OrdinalIgnoreCase),
            LastEvaluatedAppVersion = state.LastEvaluatedAppVersion
        };

    private bool IsAfterLastFetched(NewsMetadata metadata, NewsPersistentState state)
    {
        if (state.LastFetchedPublishedUtc == null) return true;
        int order = metadata.PublishedUtc.CompareTo(state.LastFetchedPublishedUtc.Value);
        return order > 0 || (order == 0 && string.Compare(metadata.Id, state.LastFetchedNewsId, StringComparison.Ordinal) > 0);
    }

    private static int CompareNewsOrder(NewsMetadata left, NewsMetadata right)
    {
        int publishedOrder = left.PublishedUtc.CompareTo(right.PublishedUtc);
        return publishedOrder != 0 ? publishedOrder : string.Compare(left.Id, right.Id, StringComparison.Ordinal);
    }

    private static bool IsForVersion(NewsMetadata metadata, Version currentVersion)
        => (metadata.MinimumVersion == null || currentVersion >= metadata.MinimumVersion) &&
           (metadata.MaximumVersion == null || currentVersion <= metadata.MaximumVersion);

    private static Version? ParseVersion(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return null;
        string normalized = System.Text.RegularExpressions.Regex.Replace(value.Trim(), @"[-+].*$", "", System.Text.RegularExpressions.RegexOptions.CultureInvariant, TimeSpan.FromSeconds(1));
        string[] parts = normalized.Split('.');
        if (parts.Length == 2) normalized += ".0";
        return Version.TryParse(normalized, out Version? version) ? version : null;
    }

    private void AdvanceLastFetched(NewsPersistentState state, NewsMetadata metadata)
    {
        if (state.LastFetchedPublishedUtc == null || IsAfterLastFetched(metadata, state))
        {
            state.LastFetchedNewsId = metadata.Id;
            state.LastFetchedPublishedUtc = metadata.PublishedUtc;
        }
    }

    private void Dispose(bool disposing)
    {
        if (disposing)
        {
            Stop();
            _lifetime?.Dispose();
        }
    }

    public void Dispose() => Dispose(true);

    private sealed record RemoteNewsFile(string Name, string Sha, Uri DownloadUri);
    private sealed record MetadataDownload(string Prefix, byte[]? CompleteBytes);
    private sealed record NewsCandidate(RemoteNewsFile RemoteFile, NewsMetadata Metadata, byte[]? CompleteBytes);

    private sealed class NewsPersistentState
    {
        public NewsPersistentState() { }

        [JsonPropertyName("lastFetchedNewsId")]
        public string? LastFetchedNewsId { get; set; }

        [JsonPropertyName("lastFetchedPublishedUtc")]
        public DateTimeOffset? LastFetchedPublishedUtc { get; set; }

        [JsonPropertyName("acknowledgedNewsIds")]
        public HashSet<string> AcknowledgedNewsIds { get; set; } = new(StringComparer.Ordinal);

        [JsonPropertyName("knownRemoteBlobShas")]
        public Dictionary<string, string> KnownRemoteBlobShas { get; set; } = new(StringComparer.OrdinalIgnoreCase);

        [JsonPropertyName("lastEvaluatedAppVersion")]
        public string? LastEvaluatedAppVersion { get; set; }
    }
}