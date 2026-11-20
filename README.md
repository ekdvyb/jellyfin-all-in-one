# Jellyfin All-In-One: Smart Collections & YouTube Metadata

A single Jellyfin plugin that:

1. **Builds smart collections** from what is actually in your library — franchises/trilogies, studios, duplicate names, TV series, genres and YouTube channels — instead of one giant "All Movies" list.
2. **Enriches YouTube videos** with their real **title, channel, description and thumbnails**, with no YouTube API key required.

## What the collections look like

Collection names are prefixed with `⭐ ` by default and use a `Category - Value` shape so they are easy to scan:

| Setting | Example collection | Contains |
| --- | --- | --- |
| Franchise detection | `⭐ Franchise - Batman` | Batman, Batman Returns, Batman Begins, The Dark Knight* |
| Studio | `⭐ Studio - Warner Bros.` | Releases from that studio |
| Name | `⭐ Name - True Grit` | Alternate cuts / duplicate editions with the same title |
| Series | `⭐ Series - Breaking Bad` | Episodes grouped by their series |
| Genre | `⭐ Genre - Action` | Items tagged Action |
| YouTube channel | `⭐ YouTube Channel - Rick Astley` | Videos from that channel |
| Type (optional) | `⭐ Type - Movies` | Broad type buckets, off by default |

\* `The Dark Knight` is grouped when Jellyfin providers expose a shared collection/TMDB collection id; title-only detection handles the obvious prefixes such as Batman, Rocky, The Matrix, Harry Potter, Mission: Impossible and Star Wars.

Only collections marked with the provider id `JellyfinAllInOne` are updated or removed. Collections you created yourself are never touched.

## Franchise / trilogy detection

The plugin normalises titles before comparing them:

- leading articles are ignored (`The Matrix` → `matrix`)
- punctuation and bracketed text are ignored (`Mission: Impossible - Ghost Protocol` (2020) → `mission impossible ghost protocol`)
- sequel markers are stripped (`Rocky II` → `rocky`, `Harry Potter ... Part 2` → `harry potter ...`)
- provider collection ids are honoured when available

Two titles are grouped when they share a meaningful prefix, so `Batman` / `Batman Returns` group but `Star Wars` / `Star Trek` and `The Incredibles` / `The Incredible Hulk` do not. You can tune the sensitivity in the plugin settings.

## YouTube metadata

- Fetches metadata from YouTube's public oEmbed endpoint, with a watch-page fallback for descriptions.
- Stores the channel as `⭐ YouTube Channel - ...` grouping data and (optionally) as the video studio.
- Picks the best available thumbnail (`maxresdefault` → `sddefault` → `hqdefault` → `mqdefault`) and sets both **Primary** and **Backdrop** images.
- Recognises video ids from:
  - filenames such as `Funny Cats [dQw4w9WgXcQ].mp4`
  - `[youtube:dQw4w9WgXcQ]`, `[yt:...]`, `(YouTube dQw4w9WgXcQ)`
  - full URLs (`https://youtu.be/...`, `/watch?v=...`, `/shorts/`, `/embed/`)
  - a bare 11-character id at the start of the filename
  - a `.youtube` or `.nfo` sidecar file next to the video
- Rate-limits requests and caches results for 12 hours. Failed lookups are retried after 3 days.

## Requirements

The manifest contains a build for each supported server line; Jellyfin automatically picks the right one:

| Jellyfin server | Plugin version | Target framework |
| --- | --- | --- |
| **12.4+** | `1.1.0.0` | `net10.0` |
| **10.11.x** | `1.1.0.0` | `net9.0` |

Older servers (10.10 and below) are not supported by this release.

## Installation

### Option A — Jellyfin plugin repository (recommended)

1. In Jellyfin, go to **Dashboard → Plugins → Repositories**.
2. Add a repository with this URL:
   ```
   https://raw.githubusercontent.com/ekdvyb/jellyfin-all-in-one/refs/heads/main/manifest.json?version=1.1
   ```
3. Go to **Catalog**, find **Jellyfin All-In-One**, and install it.
4. Restart Jellyfin.
5. Open the plugin settings and run the scheduled task once to build the smart collections.

If the plugin does not appear in the catalog, check the version compatibility table above — Jellyfin hides versions whose `targetAbi` is newer than your server.

### Option B — Manual install

1. Download the ZIP matching your server from the [latest release](https://github.com/ekdvyb/jellyfin-all-in-one/releases/latest).
2. Extract `Jellyfin.Plugin.AllInOne.dll` into:
   ```
   <jellyfin data folder>/plugins/AllInOne/
   ```
3. Restart Jellyfin.

## Configuration

Open **Dashboard → Plugins → Jellyfin All-In-One**.

### Collections

| Setting | Default | Description |
| --- | --- | --- |
| Enable smart collections | on | Master switch |
| Group trilogies and franchises by title | on | Detects shared title prefixes and provider collection ids |
| Group releases by studio | on | One collection per studio |
| Group items with the same name | on | Alternate cuts / duplicates |
| Group TV episodes by series | on | One collection per series |
| Group by genre | on | One collection per genre |
| Group YouTube videos by channel | on | Uses the fetched channel name |
| Create broad type collections | off | Movies/TV Shows/Episodes/etc. — the old behaviour |
| Create decade collections | off | Optional |
| Sync after library changes | on | Debounced 15-second rebuild |
| Collection name prefix | `⭐ ` | Prepended to managed names |
| Minimum items per collection | `2` | Smaller groups are skipped |
| Franchise detection sensitivity | `2` | Lower is more aggressive |

### YouTube

| Setting | Default | Description |
| --- | --- | --- |
| Enable YouTube metadata fetching | on | Master switch |
| Process new videos automatically | on | Handle videos as they are added |
| Replace name with YouTube title | on | Rename the library item |
| Write description to overview | on | Fetch the video description |
| Store channel as studio | on | Makes studio grouping aware of YouTube channels |
| Download thumbnails | on | Set Primary and Backdrop images |
| Re-check processed videos | off | Refresh videos already tagged by the plugin |
| Preferred thumbnail size | Max resolution | Falls back automatically |
| Delay between requests | `700` ms | Be gentle with YouTube |

Run **All-In-One: Sync smart collections & YouTube metadata** under **Dashboard → Scheduled Tasks**. It refreshes YouTube metadata first, then rebuilds collections, and runs daily at 03:00 and at startup by default.

## Build from source

```bash
git clone https://github.com/ekdvyb/jellyfin-all-in-one.git
cd jellyfin-all-in-one
dotnet build Jellyfin.Plugin.AllInOne/Jellyfin.Plugin.AllInOne.csproj -c Release
```

Output DLLs:

- `Jellyfin.Plugin.AllInOne/bin/Release/net10.0/` (Jellyfin 12.4)
- `Jellyfin.Plugin.AllInOne/bin/Release/net9.0/` (Jellyfin 10.11)

## License

GPL-3.0-only, matching the Jellyfin plugin ecosystem.
