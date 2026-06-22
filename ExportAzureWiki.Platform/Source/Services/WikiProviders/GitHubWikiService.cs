using CoreModels = ExportAzureWiki.Core.Models;
using ExportAzureWiki.Core.Services;
using System.Net.Http.Headers;
using System.Text.Json;
using Serilog;

namespace ExportAzureWiki.Services.WikiProviders
{
    /// <summary>
    /// GitHub Markdown source. Supports two modes (config key "Mode"):
    ///   "Repo"  (default) - Markdown files in the repository, listed via the
    ///                       Git Trees API and fetched from raw.githubusercontent.
    ///   "Wiki"            - the repo's wiki (owner/repo.wiki.git), cloned
    ///                       locally with LibGit2Sharp.
    ///
    /// The URL/JSON helpers are static and side-effect-free so they can be
    /// unit-tested without network access.
    /// </summary>
    public class GitHubWikiService : IWikiService
    {
        private CoreModels.WikiConfiguration _configuration = new();
        private HttpClient _httpClient = new();
        private string _owner = string.Empty;
        private string _repo = string.Empty;
        private string _branch = "main";
        private string _docsPath = string.Empty;
        private string _mode = "Repo";
        private string? _wikiCloneRoot;
        // Populated only by the repo-mode clone fallback (when the REST API host
        // is unreachable but the git host is routable). Reused across pages.
        private string? _repoCloneRoot;

        public CoreModels.WikiPlatform Platform => CoreModels.WikiPlatform.GitHub;

        public void Configure(CoreModels.WikiConfiguration configuration)
        {
            _configuration = configuration;
            // Fail fast when a host is unreachable (e.g. networks that block
            // api.github.com): an 8s connect cap instead of the OS ~21s SYN
            // timeout, while still allowing 30s for a large tree download.
            _httpClient = new HttpClient(new SocketsHttpHandler
            {
                ConnectTimeout = TimeSpan.FromSeconds(8)
            })
            {
                Timeout = TimeSpan.FromSeconds(30)
            };

            var token = configuration.AuthenticationData.TryGetValue("Token", out var t) ? t : string.Empty;
            if (!string.IsNullOrWhiteSpace(token))
            {
                _httpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
            }
            _httpClient.DefaultRequestHeaders.UserAgent.ParseAdd("ExportAzureWiki/1.0");
            _httpClient.DefaultRequestHeaders.Accept.ParseAdd("application/vnd.github+json");

            _owner = Get(configuration, "Owner");
            _repo = Get(configuration, "Repository");
            var branch = Get(configuration, "Branch");
            _branch = string.IsNullOrWhiteSpace(branch) ? "main" : branch.Trim();
            _docsPath = NormalizeDocsPath(Get(configuration, "DocsPath"));
            _mode = NormalizeMode(Get(configuration, "Mode"));
            _wikiCloneRoot = null;
            _repoCloneRoot = null;
        }

        public async Task<bool> TestConnectionAsync()
        {
            try
            {
                if (IsWikiMode(_mode))
                {
                    return EnsureWikiClone() != null;
                }

                using var response = await _httpClient.GetAsync($"https://api.github.com/repos/{_owner}/{_repo}").ConfigureAwait(false);
                return response.IsSuccessStatusCode;
            }
            catch (Exception ex)
            {
                Log.Warning(ex, "GitHub TestConnection failed for {Owner}/{Repo}", _owner, _repo);
                return false;
            }
        }

        public Task<CoreModels.WikiPage> GetRootPageAsync()
        {
            return Task.FromResult(new CoreModels.WikiPage
            {
                Id = "root",
                Path = IsWikiMode(_mode)
                    ? "/"
                    : string.IsNullOrEmpty(_docsPath) ? "/" : _docsPath,
                Title = "Home",
                Order = 0,
                HasChildren = true
            });
        }

        public async Task<List<CoreModels.WikiPage>> GetPagesAsync(string? parentPath = null)
        {
            try
            {
                var configuredStartPoints = ParseConfiguredStartPoints(_configuration.RootPath);

                // Explicit Markdown files: no remote listing needed.
                if (configuredStartPoints.Count > 0 && configuredStartPoints.All(IsMarkdown))
                {
                    return BuildPagesFromPaths(configuredStartPoints);
                }

                if (IsWikiMode(_mode))
                {
                    var root = EnsureWikiClone();
                    var pages = root == null
                        ? new List<CoreModels.WikiPage>()
                        : ParseWikiClonePages(root);

                    return ApplyStartPointFilter(pages, configuredStartPoints);
                }

                // Repo mode. The folders/start-points we must list; empty means
                // the whole repository.
                var scopes = configuredStartPoints.Count > 0
                    ? configuredStartPoints
                    : string.IsNullOrWhiteSpace(_docsPath)
                        ? new List<string>()
                        : new List<string> { _docsPath };

                return await ListRepositoryMarkdownPagesAsync(scopes).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                Log.Warning(ex, "GitHub GetPages failed for {Owner}/{Repo}", _owner, _repo);
                return new List<CoreModels.WikiPage>();
            }
        }

        /// <summary>
        /// Lists every Markdown page in <paramref name="scopes"/> with a single
        /// recursive Git Trees request, narrowing to the requested folders in
        /// memory. This replaces the old per-directory Contents walk (one HTTP
        /// round-trip per folder), which was slow on large docs trees and the
        /// reason a scoped folder rendered with no children. The Contents walk
        /// survives only as a fallback for repositories big enough that GitHub
        /// truncates the recursive tree response.
        /// </summary>
        private async Task<List<CoreModels.WikiPage>> ListRepositoryMarkdownPagesAsync(IReadOnlyList<string> scopes)
        {
            var stopwatch = System.Diagnostics.Stopwatch.StartNew();
            var treeUrl = BuildTreeUrl(_owner, _repo, _branch);
            Log.Information(
                "GitHub list start: owner={Owner} repo={Repo} branch={Branch} docsPath={DocsPath} mode={Mode} scopes=[{Scopes}] treeUrl={TreeUrl}",
                _owner, _repo, _branch, _docsPath, _mode, string.Join(", ", scopes), treeUrl);

            try
            {
                using var response = await _httpClient.GetAsync(treeUrl).ConfigureAwait(false);
                if (!response.IsSuccessStatusCode)
                {
                    Log.Warning("GitHub tree listing returned {Status} for {Owner}/{Repo}@{Branch} in {ElapsedMs}ms",
                        (int)response.StatusCode, _owner, _repo, _branch, stopwatch.ElapsedMilliseconds);
                    return new List<CoreModels.WikiPage>();
                }

                var json = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
                if (IsTreeTruncated(json))
                {
                    Log.Warning("GitHub recursive tree truncated for {Owner}/{Repo}; falling back to contents walk",
                        _owner, _repo);
                    return await ListViaContentsWalkAsync(scopes).ConfigureAwait(false);
                }

                var allPages = ParseMarkdownPages(json, docsPath: string.Empty);
                var scoped = ScopePages(allPages, scopes);
                Log.Information(
                    "GitHub list done: {Total} md in repo, {Scoped} after scope, in {ElapsedMs}ms (single recursive tree request)",
                    allPages.Count, scoped.Count, stopwatch.ElapsedMilliseconds);
                return scoped;
            }
            catch (Exception ex) when (IsConnectivityError(ex))
            {
                // The REST API host (api.github.com) is unreachable but the git
                // host usually is not (some networks have broken routes to
                // GitHub's newer Azure API IPs). Fall back to cloning the repo.
                Log.Warning(ex,
                    "GitHub REST API unreachable for {Owner}/{Repo} after {ElapsedMs}ms; falling back to git clone",
                    _owner, _repo, stopwatch.ElapsedMilliseconds);
                return await ListRepositoryViaCloneAsync(scopes).ConfigureAwait(false);
            }
        }

        /// <summary>
        /// Repo-mode listing fallback: clones the repository over the git host
        /// (github.com) and enumerates Markdown from the working tree. Used when
        /// the REST API host is unreachable.
        /// </summary>
        private async Task<List<CoreModels.WikiPage>> ListRepositoryViaCloneAsync(IReadOnlyList<string> scopes)
        {
            var root = await Task.Run(EnsureRepoClone).ConfigureAwait(false);
            if (root == null)
            {
                Log.Warning("GitHub repo clone fallback failed for {Owner}/{Repo}", _owner, _repo);
                return new List<CoreModels.WikiPage>();
            }

            var scoped = ScopePages(ParseWikiClonePages(root), scopes);
            Log.Information("GitHub list via clone: {Scoped} page(s) from {Root}", scoped.Count, root);
            return scoped;
        }

        /// <summary>
        /// Fallback used only when the recursive tree is truncated: walks the
        /// Contents API per scope (one request per folder). Walks the whole repo
        /// when no scope is configured.
        /// </summary>
        private async Task<List<CoreModels.WikiPage>> ListViaContentsWalkAsync(IReadOnlyList<string> scopes)
        {
            var roots = scopes.Count == 0 ? new List<string> { string.Empty } : scopes.ToList();
            var pages = new List<CoreModels.WikiPage>();
            foreach (var root in roots)
            {
                pages.AddRange(await GetMarkdownPagesFromContentsAsync(root).ConfigureAwait(false));
            }
            return SortDistinctPages(pages);
        }

        private async Task<List<CoreModels.WikiPage>> GetMarkdownPagesFromContentsAsync(string rootPath)
        {
            var pages = new List<CoreModels.WikiPage>();
            var pending = new Queue<string>();
            pending.Enqueue(NormalizeDocsPath(rootPath));

            while (pending.Count > 0)
            {
                var currentPath = pending.Dequeue();
                using var response = await _httpClient.GetAsync(BuildContentsUrl(_owner, _repo, _branch, currentPath)).ConfigureAwait(false);
                if (!response.IsSuccessStatusCode)
                {
                    Log.Warning("GitHub contents listing returned {Status} for {Owner}/{Repo}@{Branch}:{Path}",
                        (int)response.StatusCode, _owner, _repo, _branch, currentPath);
                    continue;
                }

                var json = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
                using var doc = JsonDocument.Parse(json);

                if (doc.RootElement.ValueKind == JsonValueKind.Object)
                {
                    AddContentNode(doc.RootElement, pages, pending);
                    continue;
                }

                if (doc.RootElement.ValueKind != JsonValueKind.Array)
                {
                    continue;
                }

                foreach (var node in doc.RootElement.EnumerateArray())
                {
                    AddContentNode(node, pages, pending);
                }
            }

            return SortDistinctPages(pages);
        }

        private static void AddContentNode(JsonElement node, List<CoreModels.WikiPage> pages, Queue<string> pending)
        {
            var type = node.GetPropertyOrDefault("type");
            var path = node.GetPropertyOrDefault("path");
            if (string.IsNullOrWhiteSpace(path))
            {
                return;
            }

            if (string.Equals(type, "dir", StringComparison.OrdinalIgnoreCase))
            {
                pending.Enqueue(path);
                return;
            }

            if (!string.Equals(type, "file", StringComparison.OrdinalIgnoreCase) || !IsMarkdown(path))
            {
                return;
            }

            pages.Add(new CoreModels.WikiPage
            {
                Id = node.GetPropertyOrDefault("sha"),
                Path = path,
                Title = TitleFromPath(path),
                Order = 0,
                HasChildren = false
            });
        }

        internal static List<string> ParseConfiguredStartPoints(string? rootPath)
        {
            if (string.IsNullOrWhiteSpace(rootPath))
            {
                return [];
            }

            return rootPath
                .Split('|', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Select(NormalizeDocsPath)
                .Where(path => !string.IsNullOrWhiteSpace(path))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(path => path, StringComparer.OrdinalIgnoreCase)
                .ToList();
        }

        internal static List<CoreModels.WikiPage> BuildPagesFromPaths(IEnumerable<string> paths)
        {
            return SortDistinctPages(paths
                .Where(path => !string.IsNullOrWhiteSpace(path) && IsMarkdown(path))
                .Select(path => new CoreModels.WikiPage
                {
                    Id = path,
                    Path = NormalizeDocsPath(path),
                    Title = TitleFromPath(path),
                    Order = 0,
                    HasChildren = false
                }));
        }

        internal static List<CoreModels.WikiPage> ApplyStartPointFilter(
            IEnumerable<CoreModels.WikiPage> pages,
            IReadOnlyCollection<string> startPoints)
        {
            var list = pages
                .Where(p => !string.IsNullOrWhiteSpace(p.Path))
                .ToList();

            if (startPoints.Count == 0)
            {
                return SortDistinctPages(list);
            }

            return SortDistinctPages(list.Where(page =>
            {
                var candidate = NormalizeDocsPath(page.Path);
                return startPoints.Any(startPoint =>
                    string.Equals(startPoint, candidate, StringComparison.OrdinalIgnoreCase) ||
                    candidate.StartsWith(startPoint.TrimEnd('/') + "/", StringComparison.OrdinalIgnoreCase));
            }));
        }

        /// <summary>
        /// Narrows a full page list to the configured scopes (folders or files).
        /// No scope means "everything".
        /// </summary>
        internal static List<CoreModels.WikiPage> ScopePages(
            IReadOnlyList<CoreModels.WikiPage> pages,
            IReadOnlyList<string> scopes)
            => scopes.Count == 0 ? SortDistinctPages(pages) : ApplyStartPointFilter(pages, scopes);

        /// <summary>
        /// True when GitHub flagged the recursive tree as truncated (repo too
        /// large to return in one response), signalling the Contents fallback.
        /// </summary>
        internal static bool IsTreeTruncated(string treeJson)
        {
            using var doc = JsonDocument.Parse(treeJson);
            return doc.RootElement.TryGetProperty("truncated", out var t)
                && t.ValueKind == JsonValueKind.True;
        }

        private static List<CoreModels.WikiPage> SortDistinctPages(IEnumerable<CoreModels.WikiPage> pages)
        {
            return pages
                .Where(page => !string.IsNullOrWhiteSpace(page.Path))
                .GroupBy(page => NormalizeDocsPath(page.Path), StringComparer.OrdinalIgnoreCase)
                .Select(group => group.First())
                .OrderBy(page => page.Path, StringComparer.OrdinalIgnoreCase)
                .ToList();
        }

        public async Task<CoreModels.WikiPageContent> GetPageContentAsync(string pagePath)
        {
            try
            {
                if (IsWikiMode(_mode))
                {
                    var root = EnsureWikiClone();
                    var fullPath = root == null ? null : ResolveWikiFilePath(root, pagePath);
                    if (fullPath != null)
                    {
                        return new CoreModels.WikiPageContent
                        {
                            PageId = pagePath,
                            Content = await File.ReadAllTextAsync(fullPath).ConfigureAwait(false),
                            ContentType = "markdown",
                            LastModified = File.GetLastWriteTime(fullPath)
                        };
                    }

                    Log.Warning("GitHub wiki page {Path} was not found in cloned wiki", pagePath);
                    return new CoreModels.WikiPageContent { PageId = pagePath, Content = string.Empty };
                }

                using var response = await _httpClient.GetAsync(BuildRawUrl(_owner, _repo, _branch, pagePath)).ConfigureAwait(false);
                if (response.IsSuccessStatusCode)
                {
                    var content = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
                    return new CoreModels.WikiPageContent
                    {
                        PageId = pagePath,
                        Content = content,
                        ContentType = "markdown",
                        LastModified = DateTime.Now
                    };
                }
                Log.Warning("GitHub raw fetch returned {Status} for {Path}", (int)response.StatusCode, pagePath);
            }
            catch (Exception ex) when (IsConnectivityError(ex))
            {
                // raw.githubusercontent unreachable: read from the repo clone if
                // one was (or can be) made over the git host.
                Log.Warning(ex, "GitHub raw fetch unreachable for {Path}; trying repo clone", pagePath);
                var fromClone = await Task.Run(() => ReadFromRepoClone(pagePath)).ConfigureAwait(false);
                if (fromClone != null)
                {
                    return fromClone;
                }
            }
            catch (Exception ex)
            {
                Log.Warning(ex, "GitHub GetPageContent failed for {Path}", pagePath);
            }

            return new CoreModels.WikiPageContent { PageId = pagePath, Content = string.Empty };
        }

        private CoreModels.WikiPageContent? ReadFromRepoClone(string pagePath)
        {
            var root = EnsureRepoClone();
            var fullPath = root == null ? null : ResolveWikiFilePath(root, pagePath);
            if (fullPath == null)
            {
                return null;
            }

            return new CoreModels.WikiPageContent
            {
                PageId = pagePath,
                Content = File.ReadAllText(fullPath),
                ContentType = "markdown",
                LastModified = File.GetLastWriteTime(fullPath)
            };
        }

        public Task<List<CoreModels.WikiComment>> GetPageCommentsAsync(string pagePath)
            => Task.FromResult(new List<CoreModels.WikiComment>());

        public Task<List<CoreModels.WikiAttachment>> GetPageAttachmentsAsync(string pagePath)
            => Task.FromResult(new List<CoreModels.WikiAttachment>());

        public Task<byte[]> GetAttachmentContentAsync(string attachmentPath)
            => Task.FromResult(Array.Empty<byte>());

        // ---- Pure, unit-testable helpers ----------------------------------

        internal static string Get(CoreModels.WikiConfiguration config, string key)
            => config.PlatformSpecificData.TryGetValue(key, out var v) ? v?.Trim() ?? string.Empty : string.Empty;

        internal static string NormalizeMode(string? mode)
        {
            if (string.Equals(mode?.Trim(), "Wiki", StringComparison.OrdinalIgnoreCase))
            {
                return "Wiki";
            }

            return "Repo";
        }

        internal static bool IsWikiMode(string? mode)
            => string.Equals(NormalizeMode(mode), "Wiki", StringComparison.OrdinalIgnoreCase);

        /// <summary>Trims leading/trailing slashes; empty means repo root.</summary>
        internal static string NormalizeDocsPath(string? docsPath)
            => string.IsNullOrWhiteSpace(docsPath) ? string.Empty : docsPath.Trim().Trim('/');

        internal static string BuildTreeUrl(string owner, string repo, string branch)
            => $"https://api.github.com/repos/{owner}/{repo}/git/trees/{Uri.EscapeDataString(branch)}?recursive=1";

        internal static string BuildContentsUrl(string owner, string repo, string branch, string path)
        {
            var normalizedPath = NormalizeRepositoryPath(path);
            var pathPart = string.IsNullOrWhiteSpace(normalizedPath) ? string.Empty : "/" + normalizedPath;
            return $"https://api.github.com/repos/{owner}/{repo}/contents{pathPart}?ref={Uri.EscapeDataString(branch)}";
        }

        internal static string NormalizeRepositoryPath(string? path)
        {
            if (string.IsNullOrWhiteSpace(path))
            {
                return string.Empty;
            }

            var segments = path.Trim()
                .Replace('\\', '/')
                .Split('/', StringSplitOptions.RemoveEmptyEntries)
                .Select(Uri.EscapeDataString);

            return string.Join("/", segments);
        }

        internal static string BuildRawUrl(string owner, string repo, string branch, string path)
            => $"https://raw.githubusercontent.com/{owner}/{repo}/{Uri.EscapeDataString(branch)}/{NormalizeRepositoryPath(path)}";

        internal static List<CoreModels.WikiPage> ParseWikiClonePages(string rootDir)
        {
            return GitWikiClone.EnumerateMarkdownFiles(rootDir)
                .Select(path => new CoreModels.WikiPage
                {
                    Id = path,
                    Path = path,
                    Title = TitleFromPath(path),
                    Order = 0,
                    HasChildren = false
                })
                .ToList();
        }

        internal static string? ResolveWikiFilePath(string rootDir, string pagePath)
        {
            if (string.IsNullOrWhiteSpace(rootDir) || string.IsNullOrWhiteSpace(pagePath))
            {
                return null;
            }

            var normalized = pagePath.Replace('\\', '/').Trim('/');
            if (normalized.Contains("..", StringComparison.Ordinal))
            {
                return null;
            }

            var candidate = Path.GetFullPath(Path.Combine(rootDir, normalized.Replace('/', Path.DirectorySeparatorChar)));
            var root = Path.GetFullPath(rootDir).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
                + Path.DirectorySeparatorChar;

            if (!candidate.StartsWith(root, StringComparison.OrdinalIgnoreCase) || !File.Exists(candidate))
            {
                return null;
            }

            return candidate;
        }

        /// <summary>
        /// Parses a Git Trees API response into the Markdown pages under
        /// <paramref name="docsPath"/>. Filters to blobs (files) ending in
        /// .md/.markdown; ignores trees (folders) and other files.
        /// </summary>
        internal static List<CoreModels.WikiPage> ParseMarkdownPages(string treeJson, string docsPath)
        {
            var pages = new List<CoreModels.WikiPage>();
            using var doc = JsonDocument.Parse(treeJson);
            if (!doc.RootElement.TryGetProperty("tree", out var tree) || tree.ValueKind != JsonValueKind.Array)
            {
                return pages;
            }

            var prefix = string.IsNullOrEmpty(docsPath) ? string.Empty : docsPath + "/";
            foreach (var node in tree.EnumerateArray())
            {
                if (node.GetPropertyOrDefault("type") != "blob")
                {
                    continue;
                }

                var path = node.GetPropertyOrDefault("path");
                if (string.IsNullOrEmpty(path) || !IsMarkdown(path))
                {
                    continue;
                }

                if (prefix.Length > 0 && !path.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                pages.Add(new CoreModels.WikiPage
                {
                    Id = node.GetPropertyOrDefault("sha"),
                    Path = path,
                    Title = TitleFromPath(path),
                    Order = 0,
                    HasChildren = false
                });
            }

            return pages
                .OrderBy(p => p.Path, StringComparer.OrdinalIgnoreCase)
                .ToList();
        }

        internal static bool IsMarkdown(string path)
            => path.EndsWith(".md", StringComparison.OrdinalIgnoreCase)
            || path.EndsWith(".markdown", StringComparison.OrdinalIgnoreCase);

        internal static string TitleFromPath(string path)
        {
            var name = path.Contains('/') ? path[(path.LastIndexOf('/') + 1)..] : path;
            var dot = name.LastIndexOf('.');
            return dot > 0 ? name[..dot] : name;
        }

        private string? EnsureWikiClone()
        {
            if (!string.IsNullOrWhiteSpace(_wikiCloneRoot) && Directory.Exists(_wikiCloneRoot))
            {
                return _wikiCloneRoot;
            }

            var token = _configuration.AuthenticationData.TryGetValue("Token", out var t) ? t : string.Empty;
            var host = GetGitHubHost(_configuration.BaseUrl);
            var cloneUrl = GitWikiClone.BuildWikiCloneUrl(host, _owner, _repo);
            _wikiCloneRoot = GitWikiClone.CloneToTemp(
                cloneUrl,
                token,
                $"github-{host}-{_owner}-{_repo}-wiki");

            return _wikiCloneRoot;
        }

        private string? EnsureRepoClone()
        {
            if (!string.IsNullOrWhiteSpace(_repoCloneRoot) && Directory.Exists(_repoCloneRoot))
            {
                return _repoCloneRoot;
            }

            var token = _configuration.AuthenticationData.TryGetValue("Token", out var t) ? t : string.Empty;
            var host = GetGitHubHost(_configuration.BaseUrl);
            var cloneUrl = GitWikiClone.BuildRepoCloneUrl(host, _owner, _repo);
            _repoCloneRoot = GitWikiClone.CloneToTemp(
                cloneUrl,
                token,
                $"github-{host}-{_owner}-{_repo}-repo");

            return _repoCloneRoot;
        }

        /// <summary>
        /// True for transport-level failures (host unreachable, connect/timeout,
        /// DNS, TLS) that warrant the git-clone fallback. A real HTTP response
        /// (404, 403, ...) is not a connectivity error and is handled inline.
        /// </summary>
        internal static bool IsConnectivityError(Exception ex)
            => ex is HttpRequestException
                || ex is TaskCanceledException
                || ex is TimeoutException
                || ex is System.Net.Sockets.SocketException
                || (ex.InnerException != null && IsConnectivityError(ex.InnerException));

        private static string GetGitHubHost(string? baseUrl)
        {
            if (Uri.TryCreate(baseUrl, UriKind.Absolute, out var uri) && !string.IsNullOrWhiteSpace(uri.Host))
            {
                return uri.Host;
            }

            return "github.com";
        }
    }

    internal static class JsonElementExtensions
    {
        public static string GetPropertyOrDefault(this JsonElement element, string name)
            => element.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String
                ? v.GetString() ?? string.Empty
                : string.Empty;
    }
}
