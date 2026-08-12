# Content Rating — Jellyfin Plugin

Tags movies/shows as **Kid**, **Teen**, or **All**, and lets you filter what
each Jellyfin user can see using Jellyfin's own built-in **Allowed Tags**
parental control feature. Everything -- the tagging UI, the API, and the
access control -- is self-contained in this one plugin. There's no separate
app to deploy, no API key to manage, and no extra port to expose.

## How the filtering works

Each movie/show gets at most one tag: `kid`, `teen`, or `all`.

Then, per user, under **Dashboard → Users → (user) → Parental Control →
Allowed tags**:

- **Kid profile**: Allowed tags = `kid`
- **Teen profile**: Allowed tags = `kid`, `teen`
- **Adult profile**: leave Allowed tags **empty** (sees everything, tagged
  or not)

Anything you haven't tagged yet stays invisible to Kid/Teen profiles but
visible to Adults, so nothing is accidentally hidden from adults while
you're still tagging your library, and untagged new content doesn't leak
to Kid/Teen profiles either.

## What the plugin adds

1. **A built-in tagging app**, served at `/ContentRating/App` -- search your
   library, filter by current tag, and set/change a movie's tag with one
   click. Opens as a popup from inside Jellyfin; nothing to install or run
   separately.
2. **A sidebar link** ("Content Tagging"), added via Jellyfin's own
   supported `config.json` `menuLinks` mechanism -- not another DOM-injection
   hack. One limitation worth knowing: `menuLinks` entries are static and
   show to *every* user, regardless of whether they're an allowed editor --
   there's no per-user conditional for this. That's a UX limitation, not a
   security one: the linked page still enforces the real access check
   server-side, so a non-editor who clicks it just sees "you don't have
   permission" rather than being able to do anything.
3. **A floating button and a three-dot menu item** on every movie, injected
   into the web client, letting you tag while browsing without leaving the
   page ("Mark as Kid/Teen/All") or jump into the full app ("Tag in app…").
4. **Access control that isn't tied to Jellyfin admin rights.** The plugin's
   own settings page (Dashboard → Plugins → Content Rating) lets an admin
   pick specific non-admin users who are allowed to tag content, enforced
   server-side on every request -- not just a hidden button.

## Menu injection is an unsupported technique

There's no official Jellyfin plugin API for adding items to the built-in web
client's three-dot menu or its own pages, so the floating button and menu
item are added by patching `index.html` on startup to load extra JS (see
`IndexHtmlPatcher.cs`) -- the same general technique used by plugins like
Intro Skipper. If a future jellyfin-web update breaks this, the underlying
access control and the tagging app itself (`/ContentRating/App`) keep
working regardless; you'd just need to navigate there directly rather than
via the injected button. You can also disable injection entirely via
`EnableMenuInjection` in the plugin config.

## A note on the popup's authentication

`/ContentRating/App` can be opened two ways, each with a different way of
identifying you:

- **From the floating button / three-dot menu**: the injected script passes
  your current session token as a URL query parameter, since the popup isn't
  part of the Jellyfin web client bundle and can't rely on
  `window.ApiClient`. This means the token briefly appears in that popup
  window's address bar/history rather than only in request headers -- a
  reasonable tradeoff for a personal/trusted-network tool, worth knowing if
  that matters for your setup.
- **From the sidebar link**: since `config.json` menu links are static URLs
  with no way to inject a fresh token per click, the page instead reads the
  same `jellyfin_credentials` entry from browser localStorage that the
  Jellyfin web app itself uses to stay logged in (safe to do since this page
  is served from the same origin). No token ever appears in the URL this
  way.

## Building

.NET 9 SDK, packages pinned to `10.11.11`. Bump both if you upgrade Jellyfin
later.

```bash
cd Jellyfin.Plugin.ContentRating
dotnet restore
dotnet build -c Release
```

## Installing via a local repository (recommended)

```powershell
cd scripts
.\Build-Repo.ps1 -SourceBaseUrl "http://<host-serving-repo-folder>:8080" -Version "1.0.1.0"
```

Then see `repo\README.md` for the remaining steps (serve the folder, add
the repository URL under Dashboard → Plugins → Repositories, install from
Catalog). Bump `-Version` on each rebuild so Jellyfin treats it as an update.

## Setup after installing

1. Restart Jellyfin.
2. Dashboard → Plugins → Content Rating → check off any non-admin users who
   should be allowed to tag content, then Save.
3. Set up **Allowed Tags** per user as described above.
4. Browse to any movie, use the three-dot menu or the floating button.

## Notes

- Movies and Series are both covered by the tagging/search UI.
- Filtering itself is entirely native Jellyfin behavior (Allowed Tags) --
  this plugin only manages the tags and who's allowed to set them.
