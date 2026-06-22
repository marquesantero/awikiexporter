using Newtonsoft.Json;
using Microsoft.TeamFoundation.Wiki.WebApi;
using Microsoft.VisualStudio.Services.Common;
using Microsoft.VisualStudio.Services.WebApi;
using Microsoft.TeamFoundation.SourceControl.WebApi;
using Microsoft.TeamFoundation.Core.WebApi;
using System.Net.Http.Headers;
using Microsoft.Azure.DevOps.Comments.WebApi;
using Newtonsoft.Json.Linq;
using ExportAzureWiki.Core;
using ExportAzureWiki.Localization;
using System.Collections;
using System.Text;
using Comment = Microsoft.Azure.DevOps.Comments.WebApi.Comment;

namespace ExportAzureWiki
{
    public class AzureDevOpsService : IDisposable
    {
        private static readonly HttpClient SharedHttpClient = new();
        private VssConnection? _connection;
        private WikiHttpClient? _wikiClient;
        private readonly AppConfig? _config;
        private bool _disposed = false;

        private Dictionary<string, List<CommentsWikiPage>> _commentsDictionary = new();

        public AzureDevOpsService() : this(null)
        {
        }

        public AzureDevOpsService(AppConfig? config)
        {
            _config = config;

            if (!string.IsNullOrWhiteSpace(_config?.OrganizationUrl) &&
                !string.IsNullOrWhiteSpace(_config?.PersonalAccessToken))
            {
                Connect(_config.OrganizationUrl, _config.PersonalAccessToken);
            }

            InitializeCommentsCache();
        }

        private void Connect(string organizationUrl, string personalAccessToken)
        {
            try
            {
                if (string.IsNullOrEmpty(organizationUrl))
                    throw new ArgumentException("Organization URL cannot be empty", nameof(organizationUrl));
                if (string.IsNullOrEmpty(personalAccessToken))
                    throw new ArgumentException("Personal Access Token cannot be empty", nameof(personalAccessToken));

                _connection = new VssConnection(new Uri(organizationUrl),
                    new VssBasicCredential(string.Empty, personalAccessToken));
                _wikiClient = _connection.GetClient<WikiHttpClient>();
            }
            catch (ArgumentException ex)
            {
                LocalizedMessageBox.ShowError(
                    LocalizationManager.Sf("azdo.config.error", ex.Message),
                    LocalizationManager.S("azdo.caption.config"));
                throw;
            }
            catch (Exception ex)
            {
                LocalizedMessageBox.ShowError(
                    LocalizationManager.Sf("azdo.connect.error", ex.Message),
                    LocalizationManager.S("azdo.caption.config"));
                throw;
            }
        }

        public async Task<(byte[] Content, string ContentType)> DownloadAttachmentAsync(string attachmentPath)
        {
            if (_config?.PersonalAccessToken == null)
                throw new InvalidOperationException("Personal Access Token is not configured");

            SharedHttpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Basic",
                Convert.ToBase64String(System.Text.Encoding.ASCII.GetBytes($":{_config.PersonalAccessToken}")));

            if (_wikiClient == null)
                throw new InvalidOperationException("Wiki client is not initialized");

            var wikis = await _wikiClient.GetAllWikisAsync().ConfigureAwait(false);
            var wiki = wikis.FirstOrDefault(w => w.Name == _config.WikiName);
            var baseUrl = $"{_config.OrganizationUrl}/{_config.Projectname}/_apis/git/repositories/{_config.RepositoryId}/items";

            if (wiki == null)
            {
                throw new Exception(LocalizationManager.S("azdo.error.wiki_not_found"));
            }

            var fullUrl = $"{baseUrl}?path={attachmentPath}&download=true&api-version=6.0";
            var response = await SharedHttpClient.GetAsync(fullUrl).ConfigureAwait(false);

            if (response.IsSuccessStatusCode)
            {
                var content = await response.Content.ReadAsByteArrayAsync().ConfigureAwait(false);
                var contentType = response.Content.Headers.ContentType?.MediaType ?? "application/octet-stream";
                return (content, contentType);
            }
            else
            {
                throw new HttpRequestException($"Failed to download attachment: {response.StatusCode}");
            }
        }

        public async Task<Tuple<Guid, Guid, WikiPageResponse>> GetWikiRootPageAsync()
        {
            if (_wikiClient == null)
                throw new InvalidOperationException("Wiki client is not initialized. Please check your configuration.");

            var wikis = await _wikiClient.GetAllWikisAsync().ConfigureAwait(false);
            var wiki = wikis.FirstOrDefault(w => w.Name == _config?.WikiName);

            if (wiki == null)
            {
                throw new Exception(LocalizationManager.S("azdo.error.wiki_not_found"));
            }

            var rootPage = await _wikiClient.GetPageAsync(wiki.ProjectId, wiki.Id, "/",
                VersionControlRecursionType.Full, includeContent: true).ConfigureAwait(false);

            return new Tuple<Guid, Guid, WikiPageResponse>(wiki.ProjectId, wiki.Id, rootPage);
        }

        public async Task<int?> GetPageIdAsync(string pagePath)
        {
            if (_wikiClient == null)
            {
                throw new InvalidOperationException(LocalizationManager.S("azdo.error.wiki_client_not_initialized"));
            }

            var wikis = await _wikiClient.GetAllWikisAsync().ConfigureAwait(false);
            var wiki = wikis.FirstOrDefault(w => w.Name == _config?.WikiName);

            if (wiki == null)
            {
                throw new Exception(LocalizationManager.Sf("azdo.error.wiki_named_not_found", _config?.WikiName ?? string.Empty));
            }

            try
            {
                var pageInfo = await _wikiClient.GetPageAsync(wiki.ProjectId, wiki.Id, pagePath, includeContent: false).ConfigureAwait(false);
                return pageInfo.Page.Id;
            }
            catch (Exception ex)
            {
                Console.WriteLine(LocalizationManager.Sf("azdo.log.page_id_error", pagePath, ex.Message));
                return null;
            }
        }

        public void BuildWikiHierarchy(WikiPage page, IList<LocalWikiPage> localWikiPages)
        {
            var localPage = new LocalWikiPage
            {
                PageId = page.Id,
                Path = page.Path,
                Content = null,
                IsParentPage = page.IsParentPage,
                SubPages = new List<LocalWikiPage>()
            };

            localWikiPages.Add(localPage);

            if (page.SubPages is not { Count: > 0 }) return;
            foreach (var subPage in page.SubPages)
            {
                BuildWikiHierarchy(subPage, localPage.SubPages);
            }
        }

        public async Task<string> GetPageContentAsync(string? path)
        {
            var wikis = await (_wikiClient?.GetAllWikisAsync()).ConfigureAwait(false)!;
            var wiki = wikis.FirstOrDefault(w => w.Name == _config?.WikiName);

            if (wiki == null)
            {
                throw new Exception(LocalizationManager.S("azdo.error.wiki_not_found"));
            }
            
            var pageWithContent = await _wikiClient.GetPageAsync(wiki.ProjectId, wiki.Id, path, includeContent: true).ConfigureAwait(false);
            return pageWithContent.Page.Content;
        }

        public async Task<List<CommentsWikiPage>> GetPageCommentsAsync(string path)
        {
            if (_wikiClient == null) throw new InvalidOperationException(LocalizationManager.S("azdo.error.wiki_client_not_initialized"));
            if (string.IsNullOrEmpty(_config?.WikiName))
                throw new InvalidOperationException(LocalizationManager.S("azdo.error.wiki_name_not_configured"));

            var wikis = await _wikiClient.GetAllWikisAsync().ConfigureAwait(false);
            var wiki = wikis.FirstOrDefault(w => w.Name == _config.WikiName);

            if (wiki == null) throw new Exception(LocalizationManager.Sf("azdo.error.wiki_named_not_found", _config.WikiName));

            var pageId = await GetPageIdAsync(path).ConfigureAwait(false);
            if (!pageId.HasValue)
            {
                throw new Exception(LocalizationManager.Sf("azdo.error.page_id_not_found", path));
            }

            var pageCommentList = await _wikiClient.ListCommentsAsync(
                project: wiki.ProjectId.ToString(),
                wikiIdentifier: wiki.Id.ToString(),
                pageId: pageId.Value,
                continuationToken: null,
                top: 1000,
                excludeDeleted: true,
                expand: CommentExpandOptions.All,
                order: CommentSortOrder.Asc).ConfigureAwait(false);

            var commentsWikiPages = pageCommentList.Comments.Select(comment => new CommentsWikiPage
            {
                Text = comment.Text,
                Author = comment.CreatedBy.DisplayName,
                CommentDate = comment.ModifiedDate,
                ImageAuthor = comment.CreatedBy.ImageUrl,
                ReactionsSummary = BuildReactionsSummary(comment)
            }).ToList();

            return commentsWikiPages;
        }

        public async Task AddPageCommentAsync(string pagePath, string commentText)
        {
            if (_wikiClient == null) throw new InvalidOperationException(LocalizationManager.S("azdo.error.wiki_client_not_initialized"));
            if (string.IsNullOrWhiteSpace(_config?.WikiName)) throw new InvalidOperationException(LocalizationManager.S("azdo.error.wiki_name_not_configured"));
            if (string.IsNullOrWhiteSpace(_config?.OrganizationUrl) || string.IsNullOrWhiteSpace(_config?.PersonalAccessToken))
                throw new InvalidOperationException(LocalizationManager.S("comments.error.add.missing_config"));

            var wikis = await _wikiClient.GetAllWikisAsync().ConfigureAwait(false);
            var wiki = wikis.FirstOrDefault(w => w.Name == _config.WikiName);
            if (wiki == null) throw new Exception(LocalizationManager.Sf("azdo.error.wiki_named_not_found", _config.WikiName));

            var pageId = await GetPageIdAsync(pagePath).ConfigureAwait(false);
            if (!pageId.HasValue) throw new Exception(LocalizationManager.Sf("azdo.error.page_id_not_found", pagePath));

            SharedHttpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Basic",
                Convert.ToBase64String(Encoding.ASCII.GetBytes($":{_config.PersonalAccessToken}")));

            var projectSegment = Uri.EscapeDataString(_config.Projectname ?? wiki.ProjectId.ToString());
            var url =
                $"{_config.OrganizationUrl}/{projectSegment}/_apis/wiki/wikis/{wiki.Id}/pages/{pageId.Value}/comments?api-version=7.1-preview.1";
            var payload = new JObject
            {
                ["text"] = commentText
            };

            using var content = new StringContent(payload.ToString(Formatting.None), Encoding.UTF8, "application/json");
            using var response = await SharedHttpClient.PostAsync(url, content).ConfigureAwait(false);
            if (!response.IsSuccessStatusCode)
            {
                var details = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
                throw new Exception(LocalizationManager.Sf("comments.error.add.remote", (int)response.StatusCode, details));
            }
        }


        public async Task<List<TeamProjectReference>> GetProjectsAsync(string organizationUrl, string token)
        {
            _connection = new VssConnection(new Uri(organizationUrl), new VssBasicCredential(string.Empty, token));
            var projectClient = _connection.GetClient<ProjectHttpClient>();
            var projects = await projectClient.GetProjects().ConfigureAwait(false);
            return projects.ToList();
        }

        public async Task<List<Tuple<string, Guid>>> GetWikisAsync(string organizationUrl, string token, Guid projectId)
        {
            _connection = new VssConnection(new Uri(organizationUrl), new VssBasicCredential(string.Empty, token));
            var wikiClient = _connection.GetClient<WikiHttpClient>();
            var wikis = await wikiClient.GetAllWikisAsync(projectId).ConfigureAwait(false);
            return wikis.Select(wiki => new Tuple<string, Guid>(wiki.Name, wiki.RepositoryId)).ToList();
        }

        public void AddCommentsToCache(string pagePath, List<CommentsWikiPage> comments)
        {
            if (!_commentsDictionary.ContainsKey(pagePath))
            {
                _commentsDictionary[pagePath] = new List<CommentsWikiPage>();
            }

            _commentsDictionary[pagePath].AddRange(comments);
        }

        public List<CommentsWikiPage> GetCommentsFromCache(string pagePath)
        {
            if (_commentsDictionary.ContainsKey(pagePath))
            {
                return _commentsDictionary[pagePath];
            }

            Console.WriteLine(LocalizationManager.Sf("azdo.log.no_comments_for_page", pagePath));
            return new List<CommentsWikiPage>();
        }

        private void InitializeCommentsCache()
        {
            _commentsDictionary = new Dictionary<string, List<CommentsWikiPage>>();
        }

        public class LocalWikiPage
        {
            public int? PageId { get; set; }
            public string? Path { get; set; }
            public string? Content { get; set; }
            public bool IsParentPage { get; set; }
            public string PublishedBy { get; set; } = string.Empty;
            public DateTime LastModification { get; set; }
            public IList<LocalWikiPage>? SubPages { get; set; }
        }

        public class CommentsWikiPage
        {
            public string  Text { get; set; } = string.Empty;
            public string Author { get; set; } = string.Empty;
            public DateTime CommentDate { get; set; }
            public  string ImageAuthor { get; set; } = string.Empty;
            public string ReactionsSummary { get; set; } = string.Empty;
        }

        private static string BuildReactionsSummary(Comment comment)
        {
            var reactionsProp = comment.GetType().GetProperty("Reactions");
            if (reactionsProp?.GetValue(comment) is not IEnumerable reactions)
            {
                return string.Empty;
            }

            var items = new List<string>();
            foreach (var reaction in reactions)
            {
                if (reaction == null)
                {
                    continue;
                }

                if (reaction is DictionaryEntry entry)
                {
                    var key = entry.Key?.ToString() ?? string.Empty;
                    var count = TryParseReactionCount(entry.Value);
                    if (!string.IsNullOrWhiteSpace(key) && count > 0)
                    {
                        items.Add($"{MapReactionEmoji(key)} {count}");
                    }

                    continue;
                }

                var type = reaction.GetType().GetProperty("Type")?.GetValue(reaction)?.ToString()
                           ?? reaction.GetType().GetProperty("Name")?.GetValue(reaction)?.ToString()
                           ?? string.Empty;
                var value = reaction.GetType().GetProperty("Count")?.GetValue(reaction)
                            ?? reaction.GetType().GetProperty("Total")?.GetValue(reaction);
                var countValue = TryParseReactionCount(value);
                if (countValue > 0 && !string.IsNullOrWhiteSpace(type))
                {
                    items.Add($"{MapReactionEmoji(type)} {countValue}");
                }
            }

            return string.Join("  ", items);
        }

        private static int TryParseReactionCount(object? value)
        {
            return value switch
            {
                null => 0,
                int i => i,
                long l => (int)l,
                short s => s,
                byte b => b,
                _ when int.TryParse(value.ToString(), out var parsed) => parsed,
                _ => 0
            };
        }

        private static string MapReactionEmoji(string reactionType)
        {
            var key = reactionType.Trim().ToLowerInvariant();
            return key switch
            {
                "like" => "👍",
                "heart" => "❤️",
                "laugh" => "😄",
                "hooray" => "🎉",
                "confused" => "😕",
                "sad" => "😢",
                "dislike" => "👎",
                _ => "•"
            };
        }

        public void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }

        protected virtual void Dispose(bool disposing)
        {
            if (!_disposed)
            {
                if (disposing)
                {
                    _connection?.Dispose();
                    _wikiClient = null;
                }
                _disposed = true;
            }
        }
    }
}

