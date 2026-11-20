# Jellyfin All-In-One: Collections & YouTube Metadata

A single Jellyfin plugin that does two things well:

1. **Automatic collections** — keeps `All Movies`, `All TV Shows`, and optional genre/decade collections in sync with your library.
2. **YouTube metadata** — when a YouTube video is added, the plugin finds the video online and fills in its **title, description, channel, and thumbnails**. No YouTube API key is required.

## Features

### Collections

- Creates and maintains an **All Movies** and **All TV Shows** collection.
- Optional per-genre collections (`Action Movies`, `Drama TV Shows`, ...).
- Optional per-decade collections (`Movies from the 2020s`, ...).
- Rebuilds automatically after library changes (debounced) and on a schedule.
- Managed collections are tagged with the provider id `JellyfinAllInOne`, so the plugin never touches collections you created yourself.

### YouTube metadata

- Fetches metadata using YouTube's public oEmbed endpoint, with a watch-page fallback for descriptions.
- Picks the best available thumbnail (`maxresdefault` → `sddefault` → `hqdefault` → `mqdefault`) and sets both **Primary** and **Backdrop** images.
- Recognizes video ids from:
  - filenames such as `Funny Cats [dQw4w9WgXcQ].mp4`
  - `[youtube:dQw4w9WgXcQ]`, `[yt:...]`, `(YouTube dQw4w9WgXcQ)`
  - full URLs (`https://youtu.be/...`, `https://www.youtube.com/watch?v=...`, `/shorts/`, `/embed/`)
  - a bare 11-character id at the start of the filename
  - a `.youtube` or `.nfo` sidecar file next to the video
- Processes new videos automatically and can re-scan the whole library via the scheduled task.
- Rate-limits requests and caches results for 12 hours. Failed lookups are retried after 3 days.

## Requirements

- Jellyfin **12.4** or newer (`targetAbi: 12.4.0.0`)
- Built for `net10.0`

> Running an older Jellyfin release? The code compiles against the current Jellyfin 12 API. Let me know your server version and I can produce a build for 10.11 / net9.0 as well.

## Installation

### Option A — Jellyfin plugin repository (recommended)

1. In Jellyfin, go to **Dashboard → Plugins → Repositories**.
2. Add a repository with this URL:
   ```
   https://raw.githubusercontent.com/ekdvyb/jellyfin-all-in-one/main/manifest.json
   ```
3. Go to the **Catalog** tab, find **All-In-One: Collections & YouTube Metadata**, and install it.
4. Restart Jellyfin.

### Option B — Manual install

1. Download `jellyfin-plugin-all-in-one-1.0.0.0.zip` from the [latest release](https://github.com/ekdvyb/jellyfin-all-in-one/releases/latest).
2. Extract `Jellyfin.Plugin.AllInOne.dll` into:
   ```
   <jellyfin data folder>/plugins/AllInOne/
   ```
3. Restart Jellyfin.

## Configuration

Open **Dashboard → Plugins → All-In-One: Collections & YouTube Metadata**.

| Setting | Default | Description |
| --- | --- | --- |
| Enable automatic collections | on | Master switch for collection syncing |
| Create an "All Movies" collection | on | One collection containing every movie |
| Create an "All TV Shows" collection | on | One collection containing every series |
| Create genre collections | off | One collection per genre |
| Create decade collections | off | One collection per decade |
| Sync after library changes | on | Debounced 15-second rebuild |
| Collection name prefix | `⭐ ` | Prepended to managed collection names |
| Minimum items per collection | `2` | Smaller groups are skipped |
| Enable YouTube metadata fetching | on | Master switch for YouTube enrichment |
| Process new videos automatically | on | Handle videos as they are added |
| Replace name with YouTube title | on | Rename the library item |
| Write description to overview | on | Fetch the video description |
| Download thumbnails | on | Set Primary and Backdrop images |
| Re-check processed videos | off | Refresh videos already tagged by the plugin |
| Preferred thumbnail size | Max resolution | Falls back automatically |
| Delay between requests | `700` ms | Be gentle with YouTube |

You can also run **All-In-One: Sync collections & YouTube metadata** under **Dashboard → Scheduled Tasks**. It runs daily at 03:00 and at startup by default.

## Naming tips for YouTube videos

For the most reliable results, put the id in the filename:

```
Rick Astley - Never Gonna Give You Up [dQw4w9WgXcQ].mp4
My Tutorial [youtube:abcdEFG1234].mkv
```

Or drop a small `video.youtube` text file next to `video.mp4` containing either the full URL or the 11-character id.

## Build from source

```bash
git clone https://github.com/ekdvyb/jellyfin-all-in-one.git
cd jellyfin-all-in-one
dotnet build Jellyfin.Plugin.AllInOne/Jellyfin.Plugin.AllInOne.csproj -c Release
```

The output DLL is in `Jellyfin.Plugin.AllInOne/bin/Release/net10.0/`.

## License

GPL-3.0-only, matching the Jellyfin plugin ecosystem.
