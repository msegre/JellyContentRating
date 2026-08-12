using System;
using System.IO;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;
using MediaBrowser.Controller;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.ContentRating
{
    /// <summary>
    /// Adds a "Content Tagging" entry to the web client's sidebar using
    /// Jellyfin's own supported mechanism for this (config.json's menuLinks
    /// array, in the web root), rather than another unsupported DOM-injection
    /// hack. Reads the existing file (if any), preserves everything else in
    /// it, and adds/updates only our own entry.
    ///
    /// Note: menuLinks entries are static and show to every user regardless
    /// of whether they're an allowed editor -- there's no per-user
    /// conditional mechanism for this. The linked page itself still enforces
    /// the real access check, so this is a UX limitation, not a security one:
    /// a non-editor who clicks it just sees "you don't have permission".
    /// </summary>
    public class MenuLinkPatcher : IHostedService
    {
        private const string LinkName = "Content Tagging";
        private const string LinkUrl = "/ContentRating/App";
        private const string LinkIcon = "shield";

        private readonly IServerApplicationPaths _applicationPaths;
        private readonly ILogger<MenuLinkPatcher> _logger;

        public MenuLinkPatcher(IServerApplicationPaths applicationPaths, ILogger<MenuLinkPatcher> logger)
        {
            _applicationPaths = applicationPaths;
            _logger = logger;
        }

        public Task StartAsync(CancellationToken cancellationToken)
        {
            try
            {
                if (Plugin.Instance?.Configuration.EnableMenuInjection != true)
                {
                    return Task.CompletedTask;
                }

                var configPath = Path.Combine(_applicationPaths.WebPath, "config.json");

                JsonNode root;
                if (File.Exists(configPath))
                {
                    var existingText = File.ReadAllText(configPath);
                    root = string.IsNullOrWhiteSpace(existingText)
                        ? new JsonObject()
                        : (JsonNode.Parse(existingText) ?? new JsonObject());
                }
                else
                {
                    root = new JsonObject();
                }

                var rootObj = root.AsObject();

                if (rootObj["menuLinks"] is not JsonArray menuLinks)
                {
                    menuLinks = new JsonArray();
                    rootObj["menuLinks"] = menuLinks;
                }

                var alreadyPresent = false;
                foreach (var entry in menuLinks)
                {
                    if (entry is JsonObject entryObj &&
                        entryObj["name"]?.GetValue<string>() == LinkName)
                    {
                        entryObj["url"] = LinkUrl;
                        entryObj["icon"] = LinkIcon;
                        alreadyPresent = true;
                        break;
                    }
                }

                if (!alreadyPresent)
                {
                    menuLinks.Add(new JsonObject
                    {
                        ["name"] = LinkName,
                        ["url"] = LinkUrl,
                        ["icon"] = LinkIcon
                    });
                }

                var options = new JsonSerializerOptions { WriteIndented = true };
                File.WriteAllText(configPath, rootObj.ToJsonString(options));
                _logger.LogInformation("ContentRating: added sidebar menu link to config.json");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "ContentRating: failed to patch config.json for the sidebar link. The floating button and three-dot menu item are unaffected.");
            }

            return Task.CompletedTask;
        }

        public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
    }
}
