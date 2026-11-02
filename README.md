# SWS2 CS2 Map Manager

Shared configured map catalog and map-change service for SwiftlyS2 plugins. The admin plugin and future RTV plugin use this catalog instead of maintaining duplicate map lists.

## Map configuration

Edit `configs/plugins/sws2-cs2mapmanager/config.jsonc`:

```jsonc
"Maps": {
  "WorkshopDownloadCommand": "mm_download_addon",
  "WorkshopDownloadTimeoutSeconds": 600,
  "Available": [
    { "Name": "Dust II", "MapName": "de_dust2" },
    { "Name": "KZ Grotto", "WorkshopId": "3121168339" }
  ]
}
```

Each entry must set exactly one of `MapName` or `WorkshopId`. `Name` is the label shown by menus and votes. Map names are checked against the server; Workshop IDs must be numeric.

## Workshop maps

Workshop downloads require MultiAddonManager. The map manager requests a server download with `mm_download_addon <id>`, polls until SwiftlyS2 recognizes the installed map, then runs `host_workshop_map <id>`. MultiAddonManager detects the active Workshop map and requests that clients download it during connection. The plugin does not add every configured map to `mm_extra_addons`.

Keep MultiAddonManager loaded and the server logged in to Steam. If Workshop downloading is disabled by setting `WorkshopDownloadCommand` to an empty string, install the map on the server before requesting it. Failed or timed out downloads are logged and are not passed to `host_workshop_map`.

## Shared API

Other plugins resolve `IMapManagerService` with `IMapManagerService.InterfaceKey`, call `GetMaps()` for `MapInfo` entries, and request changes using the map's stable `Id`. `RequestChange` reports whether a change started, a download began, or the request failed. `MapChangeProgressed` reports download, change, and failure stages.

## Build

Requires .NET 10 and the SwiftlyS2 NuGet package.

```sh
dotnet restore sws2-cs2mapmanager.csproj
dotnet build sws2-cs2mapmanager.csproj -c Release -warnaserror
dotnet publish sws2-cs2mapmanager.csproj -c Release --no-restore
```
