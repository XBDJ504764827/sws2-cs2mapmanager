# Changelog

## [Unreleased]

### Fixed

- Use only the `Maps.Available` catalog from `config.jsonc`; generated fallback maps no longer appear in admin or RTV menus.
- Detect completed MultiAddonManager downloads from the installed Workshop addon files, so `host_workshop_map` runs after the download finishes.

## 1.0.0

- Add a shared map catalog contract for SwiftlyS2 plugins.
- Support local map changes through `changelevel`.
- Support Workshop maps through MultiAddonManager download requests and `host_workshop_map`.
- Add hot-reloadable map configuration and progress notifications.
