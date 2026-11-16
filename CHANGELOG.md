# Changelog

## [Unreleased]

### Cancellable map changes

- Centralize countdowns, cancellation and temporary pause/resume in the shared map service. Expose pending state and control notifications.
- Guard all delayed and download callbacks by request identity, isolate consumer failures and clear requests when configuration or scheduling fails.
- Add shared-service regression checks for cancellation, pause, downloads and stale callbacks.

### NUB / PRO ratings

- Support nested CKZ/VNL NUB/PRO ratings in configuration and expose four independent nullable fields through MapInfo. Preserve legacy scalar metadata without inferring a run type.

### Added

- Add independent nullable CKZ/VNL tiers to extended map configuration and the shared MapInfo contract; keep compact entries and the original constructor compatible.
- Add a ready-to-fill catalog for the 15 requested maps and regression checks for legacy formats and independent ratings.
- Align build dependencies with SwiftlyS2 1.5.2.

### Fixed

- Use only the `Maps.Available` catalog from `config.jsonc`; generated fallback maps no longer appear in admin or RTV menus.
- Detect completed MultiAddonManager downloads from the installed Workshop addon files, so `host_workshop_map` runs after the download finishes.

## 1.0.0

- Add a shared map catalog contract for SwiftlyS2 plugins.
- Support local map changes through `changelevel`.
- Support Workshop maps through MultiAddonManager download requests and `host_workshop_map`.
- Add hot-reloadable map configuration and progress notifications.
