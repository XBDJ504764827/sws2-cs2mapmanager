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

Object property order is the map rotation order. Each key is the map name shown to players and each value is its numeric Workshop ID. Quote both keys and IDs because `config.jsonc` uses JSON syntax. Local maps can use values such as `"de_dust2": "local:de_dust2"`. The previous array format with `Name`, `MapName`, and `WorkshopId` remains supported. This `Available` object is the only map catalog; no fallback maps are generated when it is missing.

## Workshop maps

Workshop downloads require MultiAddonManager. The map manager requests a server download with `mm_download_addon <id>`, polls the installed Workshop addon files until the download finishes, then runs `host_workshop_map <id>`. MultiAddonManager tracks the active Workshop map and includes it in its client addon list; clients are prompted to download required addons when they connect or reconnect. The plugin does not add every configured map to `mm_extra_addons`.

Keep MultiAddonManager loaded and the server logged in to Steam. If Workshop downloading is disabled by setting `WorkshopDownloadCommand` to an empty string, install the map on the server before requesting it. Failed or timed out downloads are logged and are not passed to `host_workshop_map`.

## Deployment

For development builds, download the CI artifact from the latest workflow run and extract the `sws2-cs2mapmanager` directory into `csgo/addons/swiftlys2/plugins/`. Load `sws2-cs2mapmanager` before plugins that consume `IMapManagerService`.

The first load creates `csgo/addons/swiftlys2/configs/plugins/sws2-cs2mapmanager/config.jsonc` from the template. Install MultiAddonManager to enable server-side downloads of Workshop maps; without it, install the Workshop map on the server first and set `WorkshopDownloadCommand` to an empty string. Clients receive the active Workshop map through MultiAddonManager's normal connection download flow.

## Shared API

Other plugins resolve `IMapManagerService` with `IMapManagerService.InterfaceKey`, call `GetMaps()` for `MapInfo` entries, and request changes using the map's stable `Id`. `RequestChange` reports whether a change started, a download began, or the request failed. `MapChangeProgressed` reports download, change, and failure stages.

## Build

Requires .NET 10 and the SwiftlyS2 NuGet package.

```sh
dotnet restore sws2-cs2mapmanager.csproj
dotnet build sws2-cs2mapmanager.csproj -c Release -warnaserror
dotnet publish sws2-cs2mapmanager.csproj -c Release --no-restore
```
