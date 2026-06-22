using CoreModels = ExportAzureWiki.Core.Models;
using ExportAzureWiki.Core.Services;
using System.Net.Http.Headers;
using System.Text.Json;
using Serilog;

namespace ExportAzureWiki.Services.WikiProviders
{
    /// <summary>
    /// GitLab Markdown source. Supports two modes (config key "Mode"):
    ///   "Repo"  (default) - Markdown files in the project repository, listed via
    ///                       the Repository Tree API and fetched from the raw file
    ///                       endpoint.
    ///   "Wiki"            - the project's wiki, listed and fetched via the Wikis
    ///                       API.
    ///
    /// The project is identified by config key "ProjectId", which accepts either a
    /// numeric project id or the URL path "group/project" (incl. subgroups); the
    /// latter is URL-encoded for the API. "BaseUrl" points at self-managed GitLab
    /// instances (defaults to https://gitlab.com).
    ///
    /// The URL/JSON helpers are static and side-effect-free so they can be
    /// unit-tested without network access.
    /// </summary>
    public class GitLabWikiService : IWikiService
    {
        private CoreModels.WikiConfiguration _configuration = new();
        private HttpClient _httpClient = new();
        private string _apiBase = "https://gitlab.com";
        private string _projectId = string.Empty;
        // The branch as configured (may be empty). When empty it is resolved
        // lazily from the project's default_branch, falling back to "master".
        private string _configuredBranch = string.Empty;
        private string? _resolvedBranch;
        private string _docsPath = string.Empty;
        private string _mode = "Repo";

        /// <summary>
        /// Branch used when the configuration leaves "Branch" empty and the
        /// project's default branch cannot be resolved. GitLab.com creates new
        /// projects with "main", but the large existing install base uses
        /// "master", so master is the safer blind fallback.
        /// </summary>
        internal const string DefaultBranchFallback = "master";

        public CoreModels.WikiPlatform Platform => CoreModels.WikiPlatform.GitLab;

        public void Configure(CoreModels.WikiConfiguration configuration)
        {
            _configuration = configuration;
            // Fail fast when a host is unreachable: an 8s connect cap instead of
            // the OS ~21s SYN timeout, while still allowing 30s for a large tree.
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
                // GitLab accepts the PAT via the PRIVATE-TOKEN header.
                _httpClient.DefaultRequestHeaders.Add("PRIVATE-TOKEN", token.Trim());
            }
            _httpClient.DefaultRequestHeaders.UserAgent.ParseAdd("ExportAzureWiki/1.0");

            _apiBase = NormalizeApiBase(configuration.BaseUrl);
            _projectId = Get(configuration, "ProjectId");
            _configuredBranch = Get(configuration, "Branch");
            _resolvedBranch = null;
            _docsPath = NormalizeDocsPath(Get(configuration, "DocsPath"));
            _mode = NormalizeMode(Get(configuration, "Mode"));
        }

        public async Task<bool> TestConnectionAsync()
        {
            try
            {
                using var response = await _httpClient.GetAsync(BuildProjectUrl(_apiBase, _projectId)).ConfigureAwait(false);
                return response.IsSuccessStatusCode;
            }
            catch (Exception ex)
            {
                Log.Warning(ex, "GitLab TestConnection failed for project {ProjectId}", _projectId);
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
                    var pages = await ListWikiPagesAsync().ConfigureAwait(false);
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
                Log.Warning(ex, "GitLab GetPages failed for project {ProjectId}", _projectId);
                return new List<CoreModels.WikiPage>();
            }
        }

        /// <summary>
        /// Lists every Markdown page in the repository by paging the recursive
        /// Repository Tree API, then narrows to the requested scopes in memory.
        /// </summary>
        private async Task<List<CoreModels.WikiPage>> ListRepositoryMarkdownPagesAsync(IReadOnlyList<string> scopes)
        {
            var stopwatch = System.Diagnostics.Stopwatch.StartNew();
            var branch = await ResolveBranchAsync().ConfigureAwait(false);
            Log.Information(
                "GitLab list start: project={ProjectId} branch={Branch} docsPath={DocsPath} mode={Mode} scopes=[{Scopes}] base={Base}",
                _projectId, branch, _docsPath, _mode, string.Join(", ", scopes), _apiBase);

            var all = new List<CoreModels.WikiPage>();
            const int perPage = 100;
            for (var page = 1; ; page++)
            {
                var url = BuildTreeUrl(_apiBase, _projectId, branch, page, perPage);
                using var response = await _httpClient.GetAsync(url).ConfigureAwait(false);
                if (!response.IsSuccessStatusCode)
                {
                    Log.Warning("GitLab tree listing returned {Status} for project {ProjectId}@{Branch} (page {Page})",
                        (int)response.StatusCode, _projectId, branch, page);
                    break;
                }

                var json = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
                var batch = ParseTreePages(json);
                all.AddRange(batch);

                // GitLab signals the last page via X-Next-Page (empty when done);
                // fall back to a short batch if the header is absent.
                var nextPage = response.Headers.TryGetValues("X-Next-Page", out var values)
                    ? values.FirstOrDefault()
                    : null;
                if (string.IsNullOrWhiteSpace(nextPage) || batch.Count == 0)
                {
                    break;
                }
            }

            var scoped = ScopePages(all, scopes);
            Log.Information(
                "GitLab list done: {Total} md in repo, {Scoped} after scope, in {ElapsedMs}ms",
                all.Count, scoped.Count, stopwatch.ElapsedMilliseconds);
            return scoped;
        }

        /// <summary>
        /// Resolves the branch to read in Repo mode. An explicitly configured
        /// branch wins; otherwise the project's <c>default_branch</c> is fetched
        /// once and cached, falling back to <see cref="DefaultBranchFallback"/>
        /// ("master") when the project can't be queried.
        /// </summary>
        private async Task<string> ResolveBranchAsync()
        {
            if (!string.IsNullOrWhiteSpace(_configuredBranch))
            {
                return _configuredBranch.Trim();
            }

            if (_resolvedBranch != null)
            {
                return _resolvedBranch;
            }

            try
            {
                using var response = await _httpClient.GetAsync(BuildProjectUrl(_apiBase, _projectId)).ConfigureAwait(false);
                if (response.IsSuccessStatusCode)
                {
                    var json = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
                    var defaultBranch = ParseDefaultBranch(json);
                    _resolvedBranch = string.IsNullOrWhiteSpace(defaultBranch) ? DefaultBranchFallback : defaultBranch;
                    Log.Information("GitLab default branch resolved to {Branch} for project {ProjectId}", _resolvedBranch, _projectId);
                    return _resolvedBranch;
                }

                Log.Warning("GitLab project lookup returned {Status} resolving default branch for {ProjectId}; using {Fallback}",
                    (int)response.StatusCode, _projectId, DefaultBranchFallback);
            }
            catch (Exception ex)
            {
                Log.Warning(ex, "GitLab default-branch resolution failed for {ProjectId}; using {Fallback}", _projectId, DefaultBranchFallback);
            }

            _resolvedBranch = DefaultBranchFallback;
            return _resolvedBranch;
        }

        private async Task<List<CoreModels.WikiPage>> ListWikiPagesAsync()
        {
            using var response = await _httpClient.GetAsync(BuildWikiListUrl(_apiBase, _projectId)).ConfigureAwait(false);
            if (!response.IsSuccessStatusCode)
            {
                Log.Warning("GitLab wiki listing returned {Status} for project {ProjectId}",
                    (int)response.StatusCode, _projectId);
                return new List<CoreModels.WikiPage>();
            }

            var json = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
            return ParseWikiList(json);
        }

        public async Task<CoreModels.WikiPageContent> GetPageContentAsync(string pagePath)
        {
            try
            {
                if (IsWikiMode(_mode))
                {
                    using var response = await _httpClient.GetAsync(BuildWikiPageUrl(_apiBase, _projectId, pagePath)).ConfigureAwait(false);
                    if (response.IsSuccessStatusCode)
                    {
                        var json = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
                        return ParseWikiPageContent(json, pagePath);
                    }
                    Log.Warning("GitLab wiki page fetch returned {Status} for {Path}", (int)response.StatusCode, pagePath);
                    return new CoreModels.WikiPageContent { PageId = pagePath, Content = string.Empty };
                }

                var branch = await ResolveBranchAsync().ConfigureAwait(false);
                using var rawResponse = await _httpClient.GetAsync(BuildRawUrl(_apiBase, _projectId, branch, pagePath)).ConfigureAwait(false);
                if (rawResponse.IsSuccessStatusCode)
                {
                    var content = await rawResponse.Content.ReadAsStringAsync().ConfigureAwait(false);
                    return new CoreModels.WikiPageContent
                    {
                        PageId = pagePath,
                        Content = content,
                        ContentType = "markdown",
                        LastModified = DateTime.Now
                    };
                }
                Log.Warning("GitLab raw fetch returned {Status} for {Path}", (int)rawResponse.StatusCode, pagePath);
            }
            catch (Exception ex)
            {
                Log.Warning(ex, "GitLab GetPageContent failed for {Path}", pagePath);
            }

            return new CoreModels.WikiPageContent { PageId = pagePath, Content = string.Empty };
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
            => string.Equals(mode?.Trim(), "Wiki", StringComparison.OrdinalIgnoreCase) ? "Wiki" : "Repo";

        internal static bool IsWikiMode(string? mode)
            => string.Equals(NormalizeMode(mode), "Wiki", StringComparison.OrdinalIgnoreCase);

        /// <summary>Trims leading/trailing slashes; empty means repo root.</summary>
        internal static string NormalizeDocsPath(string? docsPath)
            => string.IsNullOrWhiteSpace(docsPath) ? string.Empty : docsPath.Trim().Trim('/');

        /// <summary>Trims the API base URL; defaults to gitlab.com.</summary>
        internal static string NormalizeApiBase(string? baseUrl)
        {
            if (string.IsNullOrWhiteSpace(baseUrl))
            {
                return "https://gitlab.com";
            }

            return baseUrl.Trim().TrimEnd('/');
        }

        /// <summary>
        /// Encodes the project identifier for the API: a numeric id is kept as-is,
        /// a "group/project" path is URL-encoded (slashes become %2F) as GitLab
        /// requires.
        /// </summary>
        internal static string EncodeProjectId(string? projectId)
        {
            var value = projectId?.Trim().Trim('/') ?? string.Empty;
            if (value.Length == 0 || value.All(char.IsDigit))
            {
                return value;
            }

            return Uri.EscapeDataString(value);
        }

        internal static string BuildProjectUrl(string apiBase, string projectId)
            => $"{apiBase}/api/v4/projects/{EncodeProjectId(projectId)}";

        internal static string BuildTreeUrl(string apiBase, string projectId, string branch, int page, int perPage)
            => $"{apiBase}/api/v4/projects/{EncodeProjectId(projectId)}/repository/tree" +
               $"?ref={Uri.EscapeDataString(branch)}&recursive=true&per_page={perPage}&page={page}";

        internal static string BuildRawUrl(string apiBase, string projectId, string branch, string path)
            => $"{apiBase}/api/v4/projects/{EncodeProjectId(projectId)}/repository/files/" +
               $"{Uri.EscapeDataString(NormalizeDocsPath(path))}/raw?ref={Uri.EscapeDataString(branch)}";

        internal static string BuildWikiListUrl(string apiBase, string projectId)
            => $"{apiBase}/api/v4/projects/{EncodeProjectId(projectId)}/wikis";

        internal static string BuildWikiPageUrl(string apiBase, string projectId, string slug)
            => $"{apiBase}/api/v4/projects/{EncodeProjectId(projectId)}/wikis/{Uri.EscapeDataString(slug?.Trim() ?? string.Empty)}";

        /// <summary>
        /// Parses one page of a Repository Tree API response into the Markdown
        /// pages it contains. Filters to blobs (files) ending in .md/.markdown;
        /// ignores trees (folders) and other files.
        /// </summary>
        internal static List<CoreModels.WikiPage> ParseTreePages(string treeJson)
        {
            var pages = new List<CoreModels.WikiPage>();
            using var doc = JsonDocument.Parse(treeJson);
            if (doc.RootElement.ValueKind != JsonValueKind.Array)
            {
                return pages;
            }

            foreach (var node in doc.RootElement.EnumerateArray())
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

                pages.Add(new CoreModels.WikiPage
                {
                    Id = node.GetPropertyOrDefault("id"),
                    Path = path,
                    Title = TitleFromPath(path),
                    Order = 0,
                    HasChildren = false
                });
            }

            return pages;
        }

        /// <summary>Reads the project's <c>default_branch</c> from a Projects API response.</summary>
        internal static string ParseDefaultBranch(string json)
        {
            using var doc = JsonDocument.Parse(json);
            return doc.RootElement.ValueKind == JsonValueKind.Object
                ? doc.RootElement.GetPropertyOrDefault("default_branch")
                : string.Empty;
        }

        internal static List<CoreModels.WikiPage> ParseWikiList(string json)
        {
            var pages = new List<CoreModels.WikiPage>();
            using var doc = JsonDocument.Parse(json);
            if (doc.RootElement.ValueKind != JsonValueKind.Array)
            {
                return pages;
            }

            foreach (var node in doc.RootElement.EnumerateArray())
            {
                var slug = node.GetPropertyOrDefault("slug");
                if (string.IsNullOrWhiteSpace(slug))
                {
                    continue;
                }

                var title = node.GetPropertyOrDefault("title");
                pages.Add(new CoreModels.WikiPage
                {
                    Id = slug,
                    Path = slug,
                    Title = string.IsNullOrWhiteSpace(title) ? TitleFromPath(slug) : title,
                    Order = 0,
                    HasChildren = false
                });
            }

            return pages
                .OrderBy(p => p.Path, StringComparer.OrdinalIgnoreCase)
                .ToList();
        }

        internal static CoreModels.WikiPageContent ParseWikiPageContent(string json, string pagePath)
        {
            using var doc = JsonDocument.Parse(json);
            if (doc.RootElement.ValueKind != JsonValueKind.Object)
            {
                return new CoreModels.WikiPageContent { PageId = pagePath, Content = string.Empty };
            }

            var format = doc.RootElement.GetPropertyOrDefault("format");
            return new CoreModels.WikiPageContent
            {
                PageId = pagePath,
                Content = doc.RootElement.GetPropertyOrDefault("content"),
                ContentType = string.IsNullOrWhiteSpace(format) ? "markdown" : format,
                LastModified = DateTime.Now
            };
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

        private static List<CoreModels.WikiPage> SortDistinctPages(IEnumerable<CoreModels.WikiPage> pages)
        {
            return pages
                .Where(page => !string.IsNullOrWhiteSpace(page.Path))
                .GroupBy(page => NormalizeDocsPath(page.Path), StringComparer.OrdinalIgnoreCase)
                .Select(group => group.First())
                .OrderBy(page => page.Path, StringComparer.OrdinalIgnoreCase)
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
    }
}
