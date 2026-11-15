# SWS2 CS2 Map Manager

Shared configured map catalog and map-change service for SwiftlyS2 plugins. The admin and rockthevote plugins use this catalog instead of maintaining duplicate map lists.

## Map configuration

Edit `configs/plugins/sws2-cs2mapmanager/config.jsonc`:

```jsonc
"Maps": {
  "WorkshopDownloadCommand": "mm_download_addon",
  "WorkshopDownloadTimeoutSeconds": 600,
  "Available": {
    "kz_func_detail": "3798986356",
    "kz_sxb2_cxz": "3083714192",
    "kz_sc_ruins": "3767757448"
  }
}
```

Object property order is the map rotation order. Existing `"map_name": "WorkshopID"` entries remain supported. To store separate difficulty tiers, use an object for each map:

```jsonc
"Available": {
  "kz_angina": {
    "WorkshopId": "3636272568",
    "CKZ": null,
    "VNL": null
  },
  "kz_angina_x": {
    "WorkshopId": "3746453872",
    "CKZ": null,
    "VNL": null
  },
  "de_dust2": { "MapName": "de_dust2", "CKZ": null, "VNL": null }
}
```

Replace each `null` with that mode's real positive numeric tier (for example `"CKZ": 2, "VNL": 5` when those are the actual ratings). CKZ and VNL are independent: a missing value in one mode never borrows the other mode's rating. Missing keys, `null`, blank/placeholder strings and invalid values mean unknown; quoted positive integers also work. No ratings are guessed from Workshop titles or map names.

JSONC uses commas between fields, and the fields must be nested inside each map object; semicolons and repeated top-level CKZ/VNL keys cannot associate tiers with individual maps. A ready-to-fill list of the 15 requested maps is included in [`resources/templates/map-catalog.example.jsonc`](resources/templates/map-catalog.example.jsonc), with all tiers set to `null`. Copy its `Available` object into your existing configuration, keeping your server settings.

Compact and extended objects can coexist. Local compact entries such as `"de_dust2": "local:de_dust2"` and legacy arrays with `Name`, `MapName`, `WorkshopId`, `CKZ`, `VNL` are supported. `Available` is the only map catalog; no fallback maps are generated when missing. Changes to ratings are hot-reloaded; reopen a menu to see them.

## Workshop maps

Workshop downloads require MultiAddonManager. The map manager requests a server download with `mm_download_addon <id>`, polls the installed Workshop addon files until the download finishes, then runs `host_workshop_map <id>`. MultiAddonManager tracks the active Workshop map and includes it in its client addon list; clients are prompted to download required addons when they connect or reconnect. The plugin does not add every configured map to `mm_extra_addons`.

Keep MultiAddonManager loaded and the server logged in to Steam. If Workshop downloading is disabled by setting `WorkshopDownloadCommand` to an empty string, install the map on the server before requesting it. Failed or timed out downloads are logged and are not passed to `host_workshop_map`.

## Deployment

For development builds, download the CI artifact from the latest workflow run and extract the `sws2-cs2mapmanager` directory into `csgo/addons/swiftlys2/plugins/`. Load `sws2-cs2mapmanager` before plugins that consume `IMapManagerService`.

The first load creates `csgo/addons/swiftlys2/configs/plugins/sws2-cs2mapmanager/config.jsonc` from the template. Install MultiAddonManager to enable server-side downloads of Workshop maps; without it, install the Workshop map on the server first and set `WorkshopDownloadCommand` to an empty string. Clients receive the active Workshop map through MultiAddonManager's normal connection download flow.

## Shared API

Other plugins resolve `IMapManagerService` with `IMapManagerService.InterfaceKey`, call `GetMaps()` for `MapInfo` entries, and request changes using the map's stable `Id`. `MapInfo.CkzTier` and `MapInfo.VnlTier` are nullable integers available to every consumer, including RTV and admin menus. The original four-argument constructor remains unchanged. `RequestChange` reports whether a change started, a download began, or the request failed. `MapChangeProgressed` reports download, change, and failure stages.

## Build

Requires .NET 10 and the SwiftlyS2 NuGet package.

```sh
dotnet restore sws2-cs2mapmanager.csproj
dotnet build sws2-cs2mapmanager.csproj -c Release -warnaserror
dotnet publish sws2-cs2mapmanager.csproj -c Release --no-restore
```

## Catalog regression checks

```sh
dotnet run --project tests/CatalogChecks/CatalogChecks.csproj -c Release
```

These check independent mode ratings, unknown/invalid values, compact entries, extended objects, local maps and legacy arrays without a running server.
