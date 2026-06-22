using LibGit2Sharp;
using Serilog;

namespace ExportAzureWiki.Services.WikiProviders
{
    /// <summary>
    /// Clones a Git-backed wiki repository (e.g. GitHub's owner/repo.wiki.git)
    /// to a local temp directory and lists its Markdown pages. Shared by the
    /// GitHub/GitLab/Bitbucket providers in "Wiki" mode - they differ only in
    /// the clone URL.
    ///
    /// Uses LibGit2Sharp, which bundles native libgit2, so the client does
    /// not need a system 'git' install. The token is passed via a credentials
    /// handler (never embedded in the URL, so it cannot leak into logs).
    /// </summary>
    public static class GitWikiClone
    {
        /// <summary>
        /// Builds the HTTPS clone URL for a provider's wiki repo. Pure.
        /// host examples: github.com, gitlab.com, bitbucket.org.
        /// </summary>
        internal static string BuildWikiCloneUrl(string host, string owner, string repo)
        {
            var o = owner.Trim().Trim('/');
            var r = repo.Trim().Trim('/');
            if (r.EndsWith(".wiki", StringComparison.OrdinalIgnoreCase))
            {
                r = r[..^5];
            }
            return $"https://{host}/{o}/{r}.wiki.git";
        }

        /// <summary>
        /// Builds the HTTPS clone URL for a provider's main repository (not the
        /// wiki). Pure. Used by the repo-Markdown fallback when the REST API host
        /// is unreachable but the git host is routable.
        /// </summary>
        internal static string BuildRepoCloneUrl(string host, string owner, string repo)
        {
            var o = owner.Trim().Trim('/');
            var r = repo.Trim().Trim('/');
            if (r.EndsWith(".git", StringComparison.OrdinalIgnoreCase))
            {
                r = r[..^4];
            }
            return $"https://{host}/{o}/{r}.git";
        }

        /// <summary>
        /// Returns the repo-relative paths of every Markdown file under
        /// <paramref name="rootDir"/>, excluding the .git directory, sorted.
        /// Filesystem-only and side-effect-free aside from reads - testable
        /// against any directory.
        /// </summary>
        internal static List<string> EnumerateMarkdownFiles(string rootDir)
        {
            var results = new List<string>();
            if (!Directory.Exists(rootDir))
            {
                return results;
            }

            foreach (var file in Directory.EnumerateFiles(rootDir, "*", SearchOption.AllDirectories))
            {
                var ext = Path.GetExtension(file);
                if (!ext.Equals(".md", StringComparison.OrdinalIgnoreCase) &&
                    !ext.Equals(".markdown", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                var rel = Path.GetRelativePath(rootDir, file).Replace('\\', '/');
                if (rel.StartsWith(".git/", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }
                results.Add(rel);
            }

            results.Sort(StringComparer.OrdinalIgnoreCase);
            return results;
        }

        /// <summary>
        /// Clones or updates the wiki repo in a deterministic app-owned cache
        /// directory keyed by <paramref name="cacheKey"/> (under
        /// <see cref="WikiCachePaths.Clone"/>, never the system TEMP folder), and
        /// returns the working-tree path. Existing clones are updated in place,
        /// so loading the page tree does not pay a full clone cost every time.
        /// </summary>
        public static string? CloneToTemp(string cloneUrl, string? token, string cacheKey)
        {
            var dest = WikiCachePaths.Clone(SanitizeKey(cacheKey));

            try
            {
                if (Repository.IsValid(dest))
                {
                    try
                    {
                        RefreshExistingClone(dest, token);
                    }
                    catch (Exception ex)
                    {
                        Log.Warning(ex, "Wiki clone refresh failed for {CacheKey}; using existing local clone", cacheKey);
                    }

                    return dest;
                }

                if (Directory.Exists(dest))
                {
                    ForceDelete(dest);
                }
                Directory.CreateDirectory(dest);

                var options = CreateCloneOptions(token);
                Repository.Clone(cloneUrl, dest, options);
                return dest;
            }
            catch (Exception ex)
            {
                Log.Error(ex, "Wiki clone failed for {CacheKey}", cacheKey);
                return null;
            }
        }

        private static void RefreshExistingClone(string dest, string? token)
        {
            using var repository = new Repository(dest);
            Commands.Fetch(
                repository,
                "origin",
                Array.Empty<string>(),
                CreateFetchOptions(token),
                "Update wiki clone");

            var target = repository.Head.TrackedBranch
                ?? repository.Branches.FirstOrDefault(branch =>
                    branch.IsRemote &&
                    string.Equals(branch.FriendlyName, "origin/main", StringComparison.OrdinalIgnoreCase))
                ?? repository.Branches.FirstOrDefault(branch =>
                    branch.IsRemote &&
                    string.Equals(branch.FriendlyName, "origin/master", StringComparison.OrdinalIgnoreCase))
                ?? repository.Branches.FirstOrDefault(branch => branch.IsRemote && branch.Tip != null);

            if (target?.Tip != null)
            {
                repository.Reset(ResetMode.Hard, target.Tip);
            }
        }

        private static CloneOptions CreateCloneOptions(string? token)
        {
            var options = new CloneOptions();
            ApplyCredentials(options.FetchOptions, token);
            return options;
        }

        private static FetchOptions CreateFetchOptions(string? token)
        {
            var options = new FetchOptions();
            ApplyCredentials(options, token);
            return options;
        }

        private static void ApplyCredentials(FetchOptions options, string? token)
        {
            if (string.IsNullOrWhiteSpace(token))
            {
                return;
            }

            options.CredentialsProvider = (_, _, _) =>
                new UsernamePasswordCredentials
                {
                    Username = "x-access-token",
                    Password = token
                };
        }

        private static string SanitizeKey(string key)
        {
            foreach (var c in Path.GetInvalidFileNameChars())
            {
                key = key.Replace(c, '_');
            }
            return key.Replace('/', '_').Replace(':', '_');
        }

        private static void ForceDelete(string dir)
        {
            // .git contains read-only objects; clear the attribute before delete.
            foreach (var file in Directory.EnumerateFiles(dir, "*", SearchOption.AllDirectories))
            {
                try { File.SetAttributes(file, FileAttributes.Normal); } catch { /* best effort */ }
            }
            Directory.Delete(dir, recursive: true);
        }
    }
}
