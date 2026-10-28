# 100% Vibecoded Slop

# Anime Planning Sync

A Jellyfin (12.3+) plugin that reads a user's AniList **Planning** (plan-to-watch) list and marks every matching series or movie in the library as a **favourite**.

- **Opt-in per user.** Nothing happens for a user until an admin enables them on the plugin's settings page.
- **Two ways to log in:**
  - Reuse the AniList login stored by the [Ani-Sync](https://github.com/vosmiic/jellyfin-ani-sync) plugin. Its configuration is only read, never written.
  - Enter an AniList username. This works only if that user's list is publicly visible.

  Either choice is checked against AniList before it is saved.
- **Never removes favourites.** If a user un-favourites an item the plugin added, the plugin will not add it again.

## Matching

1. An `AniList` provider id on a series, season or movie. A match on a season favourites its parent series.
2. Optionally, for items without an AniList id, the AniDB, TVDB-season and TMDB ids are looked up in [Fribb/anime-lists](https://github.com/Fribb/anime-lists). The mapping file is downloaded once a day and matched locally. Specials (season 0) are not matched.

## Syncing

AniList offers no webhooks, so the plugin polls:

| Task | Default | AniList requests |
|---|---|---|
| Check AniList Planning lists for changes | every 10 min | 1 per user; a full fetch only if the list changed |
| Full sync of AniList Planning lists | hourly | 1 per 500 list entries per user |
| Refresh anime id mapping | daily | none (GitHub download, conditional) |

When series or movies are added to the library, they are matched against the cached lists about 2 minutes later, without any AniList requests.

All AniList requests go through one queue. The plugin sends at most *Maximum requests per minute* requests, 20 by default. If AniList reports a lower limit in `X-RateLimit-Limit`, that limit is used instead (it is currently 30). The plugin also honours `X-RateLimit-Remaining`, and on a 429 response it waits for `Retry-After` / `X-RateLimit-Reset`.

## Installation

In Jellyfin, open *Dashboard → Plugins → Repositories* and add:

```
https://raw.githubusercontent.com/paulmiro/jellyfin-anime-planning-sync/main/manifest.json
```

The plugin then shows up in the plugin catalogue.

## Releases

Every push to `main` creates a release, using the `version` in `build.yaml`. That is the only place the version is set; the csproj and `package.nix` read it from there. If a release for that version already exists, the workflow skips the release and still succeeds, so **bump the version to publish a new release**.

The workflow does the following:

1. Builds the plugin with Nix, which runs the tests.
2. Adds the version to `manifest.json` in a `release version X` commit.
3. Tags that commit `vX`.
4. Publishes a GitHub release. It attaches the plugin zip and lists the commits made since the previous release.

## Build

```sh
nix-shell -p dotnet-sdk_10 --run "dotnet test && dotnet build -c Release"
```

Or with Nix, which runs the tests too. The output goes to `result/lib/jellyfin-plugin-anime-planning-sync/`.

```sh
nix build                                                  # pinned nixpkgs from flake.lock
nix-build -E 'with import <nixpkgs> {}; callPackage ./package.nix {}'   # your nixpkgs
```

After changing NuGet package references, regenerate `deps.json`:

```sh
$(nix-build --no-out-link -E 'with import <nixpkgs> {}; (callPackage ./package.nix {}).passthru.fetch-deps') deps.json
```

To install, copy `src/Jellyfin.Plugin.AnimePlanningSync/bin/Release/net10.0/Jellyfin.Plugin.AnimePlanningSync.dll` into a folder such as `<config>/plugins/AnimePlanningSync_1.0.0.0/` and restart Jellyfin.
