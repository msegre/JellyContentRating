using System;
using System.Collections.Generic;
using MediaBrowser.Model.Plugins;

namespace Jellyfin.Plugin.ContentRating.Configuration
{
    /// <summary>
    /// Plugin configuration. Movie tagging happens through the plugin's own
    /// built-in tagging app (served at /ContentRating/App) -- this config just
    /// decides who's allowed to open it and use quick-tag, without requiring
    /// full Jellyfin administrator rights.
    /// </summary>
    public class PluginConfiguration : BasePluginConfiguration
    {
        /// <summary>
        /// Jellyfin user IDs (guid strings) allowed to open the tagging app,
        /// in addition to administrators (who are always allowed). Managed from
        /// the plugin's dashboard config page by picking from the user list --
        /// this is the "some other means than admin rights" mechanism.
        /// </summary>
        public List<Guid> EditorUserIds { get; set; } = new List<Guid>();

        /// <summary>
        /// If true, inject the three-dot menu item and floating button into the
        /// web client. Unsupported hack (see IndexHtmlPatcher) -- turn off if it
        /// ever causes problems; editors can still be granted access, they'd just
        /// need to navigate to /ContentRating/App directly with their own token.
        /// </summary>
        public bool EnableMenuInjection { get; set; } = true;
    }
}
