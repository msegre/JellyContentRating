using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Jellyfin.Data.Enums;
using Jellyfin.Database.Implementations.Enums;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Library;
using MediaBrowser.Controller.Net;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Jellyfin.Plugin.ContentRating.Api
{
    public class CanEditResult
    {
        /// <summary>Whether the calling user is allowed to open the tagging app / use quick-tag.</summary>
        public bool CanEdit { get; set; }
    }

    public class SetTagRequest
    {
        public Guid ItemId { get; set; }

        /// <summary>One of "kid", "teen", "all", or empty string to clear.</summary>
        public string Tag { get; set; } = string.Empty;
    }

    public class SetTagResult
    {
        public Guid ItemId { get; set; }

        public string Tag { get; set; } = string.Empty;
    }

    public class UserSummary
    {
        public Guid Id { get; set; }

        public string Name { get; set; } = string.Empty;

        public bool IsAdministrator { get; set; }
    }

    public class TagInfo
    {
        public Guid ItemId { get; set; }

        /// <summary>"kid", "teen", "all", or empty if untagged.</summary>
        public string Tag { get; set; } = string.Empty;
    }

    public class MovieSearchResult
    {
        public Guid Id { get; set; }

        public string Name { get; set; } = string.Empty;

        public int? ProductionYear { get; set; }

        /// <summary>"kid", "teen", "all", or empty if untagged.</summary>
        public string Tag { get; set; } = string.Empty;

        /// <summary>MPAA/content rating from metadata (e.g. "PG-13"), if known.</summary>
        public string? OfficialRating { get; set; }
    }

    public class SearchMoviesResult
    {
        public List<MovieSearchResult> Items { get; set; } = new();

        public bool HasMore { get; set; }

        public int TotalCount { get; set; }
    }

    [ApiController]
    [Route("ContentRating")]
    public class ContentRatingController : ControllerBase
    {
        // Must match the tags the standalone tagging app writes -- keep these in
        // sync if that ever changes, or movies end up tagged inconsistently
        // depending on which tool was used.
        private static readonly string[] RatingTags = { "kid", "teen", "all" };

        private readonly IAuthorizationContext _authContext;
        private readonly ILibraryManager _libraryManager;
        private readonly IUserManager _userManager;

        public ContentRatingController(IAuthorizationContext authContext, ILibraryManager libraryManager, IUserManager userManager)
        {
            _authContext = authContext;
            _libraryManager = libraryManager;
            _userManager = userManager;
        }

        /// <summary>
        /// Serves the menu-injection script. Anonymous because it's loaded on every
        /// page (including the login screen); the actual permission check happens
        /// in CanEdit/SetTag below, which the script calls before doing anything.
        /// </summary>
        [HttpGet("script.js")]
        [AllowAnonymous]
        public ActionResult GetScript()
        {
            var assembly = GetType().Assembly;
            var resourceName = $"{GetType().Namespace!.Replace(".Api", string.Empty)}.Web.injector.js";
            var stream = assembly.GetManifestResourceStream(resourceName);
            if (stream == null)
            {
                return NotFound();
            }

            return File(stream, "application/javascript");
        }

        /// <summary>
        /// Whether the calling (authenticated) user is allowed to quick-tag movies
        /// and open the tagging app -- true for Jellyfin administrators, and also
        /// for anyone in the plugin's EditorUserIds allow-list, configured on the
        /// plugin's dashboard page. This is the "some other means than admin
        /// rights" mechanism.
        /// </summary>
        [HttpGet("CanEdit")]
        [Authorize]
        public async Task<ActionResult<CanEditResult>> CanEdit()
        {
            var allowed = await IsCallerAllowed().ConfigureAwait(false);
            return Ok(new CanEditResult { CanEdit = allowed });
        }

        /// <summary>
        /// Serves the self-contained tagging app, opened as a popup from the
        /// injected floating button / three-dot menu. This isn't part of the
        /// Jellyfin web bundle, so it can't rely on window.ApiClient existing --
        /// the caller passes an access token as a URL query parameter instead,
        /// which this page's own JS reads and uses for its own API calls.
        /// </summary>
        [HttpGet("App")]
        [AllowAnonymous]
        public ActionResult GetApp()
        {
            var assembly = GetType().Assembly;
            var resourceName = $"{GetType().Namespace!.Replace(".Api", string.Empty)}.Web.app.html";
            var stream = assembly.GetManifestResourceStream(resourceName);
            if (stream == null)
            {
                return NotFound();
            }

            return File(stream, "text/html");
        }

        /// <summary>
        /// Searches movies/series by title and optional tag filter, for the
        /// tagging app's search UI. Same allow-list as CanEdit/SetTag.
        /// Paginated -- results are sorted/paged in C# after fetching rather
        /// than via InternalItemsQuery.OrderBy, which has proven unstable
        /// across Jellyfin versions.
        /// </summary>
        [HttpGet("SearchMovies")]
        [Authorize]
        public async Task<ActionResult<SearchMoviesResult>> SearchMovies(
            [FromQuery] string? q,
            [FromQuery] string? tags,
            [FromQuery] bool untaggedOnly = false,
            [FromQuery] string? nameStartsWithOrGreater = null,
            [FromQuery] int startIndex = 0,
            [FromQuery] int pageSize = 100)
        {
            if (!await IsCallerAllowed().ConfigureAwait(false))
            {
                return Forbid();
            }

            pageSize = Math.Clamp(pageSize, 1, 500);
            startIndex = Math.Max(startIndex, 0);

            var tagFilter = (tags ?? string.Empty)
                .Split(',', StringSplitOptions.RemoveEmptyEntries)
                .Select(t => t.Trim().ToLowerInvariant())
                .Where(t => RatingTags.Contains(t))
                .ToArray();

            var query = new InternalItemsQuery
            {
                IncludeItemTypes = new[] { BaseItemKind.Movie, BaseItemKind.Series },
                Recursive = true
            };
            if (!string.IsNullOrWhiteSpace(q))
            {
                query.NameContains = q;
            }

            if (!string.IsNullOrWhiteSpace(nameStartsWithOrGreater))
            {
                query.NameStartsWithOrGreater = nameStartsWithOrGreater;
            }

            if (untaggedOnly)
            {
                // "All" in the UI means "no kid/teen/all tag" -- exclude at the
                // query level (not a post-fetch filter), since that has to be
                // correct before pagination is applied, not after.
                query.ExcludeTags = RatingTags;
            }
            else if (tagFilter.Length > 0)
            {
                query.Tags = tagFilter;
            }

            var allMatches = _libraryManager.GetItemList(query)
                .Select(i => new MovieSearchResult
                {
                    Id = i.Id,
                    Name = i.Name,
                    ProductionYear = i.ProductionYear,
                    OfficialRating = i.OfficialRating,
                    Tag = i.Tags.FirstOrDefault(t => RatingTags.Contains(t, StringComparer.OrdinalIgnoreCase))?.ToLowerInvariant() ?? string.Empty
                })
                .OrderBy(r => r.Name, StringComparer.OrdinalIgnoreCase)
                .ToList();

            var pageItems = allMatches.Skip(startIndex).Take(pageSize).ToList();

            return Ok(new SearchMoviesResult
            {
                Items = pageItems,
                HasMore = startIndex + pageSize < allMatches.Count,
                TotalCount = allMatches.Count
            });
        }

        /// <summary>
        /// Quick-tags a single movie from the three-dot menu -- sets it to
        /// exactly one of kid/teen/all (clearing any previous rating tag first,
        /// so a movie is never tagged with more than one tier at once), or
        /// clears it entirely if Tag is empty. Same allow-list check as CanEdit.
        /// </summary>
        [HttpPost("SetTag")]
        [Authorize]
        public async Task<ActionResult<SetTagResult>> SetTag([FromBody] SetTagRequest request)
        {
            if (!await IsCallerAllowed().ConfigureAwait(false))
            {
                return Forbid();
            }

            var tag = (request.Tag ?? string.Empty).Trim().ToLowerInvariant();
            if (tag.Length > 0 && !RatingTags.Contains(tag))
            {
                return BadRequest($"Tag must be one of: {string.Join(", ", RatingTags)}, or empty.");
            }

            var item = _libraryManager.GetItemById(request.ItemId);
            if (item == null)
            {
                return NotFound();
            }

            var newTags = item.Tags
                .Where(t => !RatingTags.Contains(t, StringComparer.OrdinalIgnoreCase))
                .ToList();

            if (tag.Length > 0)
            {
                newTags.Add(tag);
            }

            item.Tags = newTags.ToArray();
            await item.UpdateToRepositoryAsync(ItemUpdateType.MetadataEdit, CancellationToken.None)
                .ConfigureAwait(false);

            return Ok(new SetTagResult { ItemId = item.Id, Tag = tag });
        }

        /// <summary>
        /// Lists all Jellyfin users, for the plugin's own settings page to build
        /// its "allowed editors" checklist from. Admin-only -- this endpoint is
        /// only used by the settings page itself, not by CanEdit-allowed editors.
        /// Implemented server-side rather than relying on the web client's own
        /// (undocumented, version-unstable) ApiClient.getUsers() method.
        /// </summary>
        [HttpGet("Users")]
        [Authorize(Policy = "RequiresElevation")]
        public ActionResult<IEnumerable<UserSummary>> GetUsers()
        {
            var users = _userManager.GetUsers()
                .Select(u => new UserSummary
                {
                    Id = u.Id,
                    Name = u.Username,
                    IsAdministrator = u.Permissions.Any(p => p.Kind == PermissionKind.IsAdministrator && p.Value)
                })
                .OrderBy(u => u.Name, StringComparer.OrdinalIgnoreCase);

            return Ok(users);
        }

        /// <summary>
        /// Batch-looks-up the current rating tag (if any) for a set of items, for
        /// the injected script to render Kid/Teen badges on browsed movie cards
        /// without one API call per card. Same allow-list as CanEdit/SetTag.
        /// </summary>
        [HttpGet("Tags")]
        [Authorize]
        public async Task<ActionResult<IEnumerable<TagInfo>>> GetTags([FromQuery] string ids)
        {
            if (!await IsCallerAllowed().ConfigureAwait(false))
            {
                return Forbid();
            }

            var results = new List<TagInfo>();
            foreach (var idText in (ids ?? string.Empty).Split(',', StringSplitOptions.RemoveEmptyEntries))
            {
                if (!Guid.TryParse(idText.Trim(), out var itemId))
                {
                    continue;
                }

                var item = _libraryManager.GetItemById(itemId);
                if (item == null)
                {
                    continue;
                }

                var tag = item.Tags.FirstOrDefault(t => RatingTags.Contains(t, StringComparer.OrdinalIgnoreCase)) ?? string.Empty;
                results.Add(new TagInfo { ItemId = itemId, Tag = tag.ToLowerInvariant() });
            }

            return Ok(results);
        }

        private async Task<bool> IsCallerAllowed()
        {
            var config = Plugin.Instance!.Configuration;
            var authInfo = await _authContext.GetAuthorizationInfo(Request).ConfigureAwait(false);

            var isAdmin = authInfo.User?.Permissions.Any(p => p.Kind == PermissionKind.IsAdministrator && p.Value) ?? false;
            var isEditor = config.EditorUserIds.Contains(authInfo.UserId);
            return isAdmin || isEditor;
        }
    }
}
