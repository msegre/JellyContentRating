using System;
using System.IO;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using MediaBrowser.Controller;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.ContentRating
{
    /// <summary>
    /// Unsupported hack: patches the web client's index.html on startup so it loads
    /// our injector.js, which adds "Mark as Kid/Teen/All" buttons to the item menu.
    ///
    /// There is no official Jellyfin plugin API for adding items to the built-in
    /// web client's three-dot menu, so this does it by editing the served HTML
    /// directly (the same technique used by plugins like Intro Skipper). It will
    /// be reverted whenever jellyfin-web is reinstalled/updated, and this service
    /// will simply re-apply the patch the next time the server starts.
    ///
    /// If this ever breaks, tagging still works fine via the plugin's own
    /// dashboard page under Dashboard > Plugins > Content Rating.
    /// </summary>
    public class IndexHtmlPatcher : IHostedService
    {
        // Matches any previously-injected ContentRating script tag, regardless of
        // the exact src path used by an older build of this plugin, so upgrades
        // replace the old tag instead of stacking a new one alongside it.
        private static readonly Regex OldTagPattern = new Regex(
            "<script plugin=\"ContentRating\"[^>]*></script>\\s*",
            RegexOptions.Compiled);

        private const string InjectedTag =
            "<script plugin=\"ContentRating\" src=\"/ContentRating/script.js\" defer></script>";

        // IServerApplicationPaths is registered as a singleton by Jellyfin's own
        // ApplicationHost.RegisterServices, so plugins can just ask for it directly
        // instead of trying to pull it off IServerApplicationHost (which doesn't
        // expose it -- ApplicationPaths there is a protected member, not part of
        // the public interface).
        private readonly IServerApplicationPaths _applicationPaths;
        private readonly ILogger<IndexHtmlPatcher> _logger;

        public IndexHtmlPatcher(IServerApplicationPaths applicationPaths, ILogger<IndexHtmlPatcher> logger)
        {
            _applicationPaths = applicationPaths;
            _logger = logger;
        }

        public Task StartAsync(CancellationToken cancellationToken)
        {
            try
            {
                var indexPath = Path.Combine(_applicationPaths.WebPath, "index.html");

                if (!File.Exists(indexPath))
                {
                    _logger.LogWarning("ContentRating: could not find index.html at {Path}; skipping menu injection. Use the plugin's dashboard page instead.", indexPath);
                    return Task.CompletedTask;
                }

                var original = File.ReadAllText(indexPath);
                var stripped = OldTagPattern.Replace(original, string.Empty);
                var hadOldTag = !string.Equals(stripped, original, StringComparison.Ordinal);

                if (Plugin.Instance?.Configuration.EnableMenuInjection != true)
                {
                    // Injection disabled: make sure any tag from a previous run is removed too.
                    if (hadOldTag)
                    {
                        File.WriteAllText(indexPath, stripped);
                        _logger.LogInformation("ContentRating: menu injection disabled; removed previously injected script tag.");
                    }

                    return Task.CompletedTask;
                }

                if (!hadOldTag && original.Contains(InjectedTag, StringComparison.Ordinal))
                {
                    // Already patched with the current tag, nothing to do.
                    return Task.CompletedTask;
                }

                var lastBodyClose = stripped.LastIndexOf("</body>", StringComparison.Ordinal);
                if (lastBodyClose < 0)
                {
                    _logger.LogWarning("ContentRating: index.html did not contain </body>; skipping menu injection.");
                    return Task.CompletedTask;
                }

                // Insert only at the LAST occurrence of </body>, not every occurrence.
                // string.Replace() replaces every match -- if this minified bundle
                // happens to contain the literal text "</body>" anywhere else (e.g.
                // as sample/template text embedded in an inline script or JSON blob,
                // not actual markup), a blind Replace() would have corrupted that
                // script too. The real closing tag is reliably the last one in the
                // file, so only touch that one.
                var updated = stripped.Substring(0, lastBodyClose)
                    + InjectedTag
                    + stripped.Substring(lastBodyClose);
                File.WriteAllText(indexPath, updated);
                _logger.LogInformation(
                    hadOldTag
                        ? "ContentRating: replaced outdated injected script tag in index.html"
                        : "ContentRating: injected menu script into index.html");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "ContentRating: failed to patch index.html. Tagging still works via the plugin's dashboard page.");
            }

            return Task.CompletedTask;
        }

        public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
    }
}
