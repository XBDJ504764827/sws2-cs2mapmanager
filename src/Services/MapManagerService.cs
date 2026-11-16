using System.Globalization;
using Microsoft.Extensions.Logging;
using SwiftlyS2.Shared;
using SwiftlyS2.Shared.FileSystem;
using Sws2Cs2MapManager.Configuration;
using Sws2Cs2MapManager.Contracts;

namespace Sws2Cs2MapManager.Services;

/// <summary>Owns the configured map catalog and coordinates map changes.</summary>
internal sealed class MapManagerService : IMapManagerService, IDisposable
{
    private sealed record PendingWorkshopChange(Guid Request, MapInfo Map, DateTimeOffset Deadline, int TimeoutSeconds);

    private readonly ISwiftlyCore _core;
    private readonly ILogger _logger;
    private readonly object _sync = new();
    private readonly Dictionary<string, MapInfo> _maps = new(StringComparer.OrdinalIgnoreCase);
    private readonly List<MapInfo> _mapOrder = [];

    private MapManagerSettings _settings;
    private IReadOnlyList<ConfiguredMap?> _configuredMaps;
    private PendingWorkshopChange? _pendingWorkshopChange;
    private CancellationTokenSource? _pendingCheck;
    private bool _disposed;
    private readonly MapChangeGate _gate = new();
    private CancellationTokenSource? _changeTimer;

    public MapManagerService(
        ISwiftlyCore core,
        MapManagerSettings settings,
        IReadOnlyList<ConfiguredMap?> configuredMaps)
    {
        _core = core;
        _logger = core.Logger;
        _settings = settings;
        _configuredMaps = configuredMaps;
        RebuildMapCatalog();
        _core.Event.OnMapUnload += OnMapUnload;
        _core.Event.OnConsoleOutput += OnConsoleOutput;
    }

    public event EventHandler<MapChangeProgressEventArgs>? MapChangeProgressed;

    public void ApplySettings(MapManagerSettings settings, IReadOnlyList<ConfiguredMap?> configuredMaps)
    {
        lock (_sync)
        {
            _settings = settings;
            _configuredMaps = configuredMaps;
            RebuildMapCatalogLocked();
        }
    }

    public IReadOnlyList<MapInfo> GetMaps()
    {
        lock (_sync)
        {
            return _mapOrder.ToArray();
        }
    }

    public event EventHandler? MapChangeControlChanged;

    public MapChangeState GetChangeState() { lock (_sync) return _gate.State; }

    public bool CancelPendingChange()
    {
        MapInfo? cancelled;
        lock (_sync)
        {
            cancelled = _gate.Cancel();
            _changeTimer?.Cancel(); _changeTimer = null;
            ClearPendingWorkshopChange();
        }
        if (cancelled is not null) RaiseProgress(cancelled, MapChangeProgress.Cancelled);
        RaiseControlChanged();
        return cancelled is not null;
    }

    public void SetChangesPaused(bool paused, string actor)
    {
        lock (_sync)
        {
            if (_gate.State.IsPaused == paused) return;
            var cancelled = _gate.Pause(paused, actor);
            _changeTimer?.Cancel(); _changeTimer = null;
            ClearPendingWorkshopChange();
            if (cancelled is not null) RaiseProgress(cancelled, MapChangeProgress.Cancelled);
        }
        _logger.LogInformation("[sws2-cs2mapmanager] Map changes paused={Paused} by {Actor}", paused, actor);
        RaiseControlChanged();
    }

    public MapChangeRequestResult RequestChange(string mapId) => ScheduleChange(mapId, 0, "plugin");

    public MapChangeRequestResult ScheduleChange(string mapId, int delaySeconds, string source)
    {
        MapInfo? map;
        Guid request;
        lock (_sync)
        {
            if (_disposed) return new(MapChangeRequestStatus.Failed, Error: "Map manager is unloading.");
            if (!_maps.TryGetValue(mapId?.Trim() ?? "", out map)) return new(MapChangeRequestStatus.MapNotFound);
            var status = _gate.Begin(map, Math.Clamp(delaySeconds, 0, 300), source, out request);
            if (status != MapChangeRequestStatus.Scheduled) return new(status, map);
            if (delaySeconds > 0)
            {
                delaySeconds = Math.Clamp(delaySeconds, 1, 300);
                try
                {
                    _changeTimer = _core.Scheduler.DelayBySeconds(delaySeconds, () => ExecuteScheduled(request));
                }
                catch (Exception exception)
                {
                    _gate.Finish(request);
                    return Fail(map, MapChangeRequestStatus.Failed, exception.Message, exception);
                }
                RaiseProgress(map, MapChangeProgress.CountdownStarted, delaySeconds: delaySeconds);
                return new(MapChangeRequestStatus.Scheduled, map);
            }
        }
        return ExecuteScheduled(request);
    }

    private MapChangeRequestResult ExecuteScheduled(Guid request)
    {
        lock (_sync)
        {
            if (_disposed || !_gate.IsCurrent(request)) return new(MapChangeRequestStatus.Failed, Error: "Request cancelled.");
            _changeTimer = null;
            var map = _gate.State.PendingMap!;
            try
            {
                var result = StartChange(map.Id, request);
                if (result.Status == MapChangeRequestStatus.MapNotFound)
                    result = Fail(map, result.Status, "The map was removed from the catalog during the countdown.");
                if (result.Status is not (MapChangeRequestStatus.DownloadStarted or MapChangeRequestStatus.DownloadAlreadyPending))
                    _gate.Finish(request);
                return result;
            }
            catch (Exception exception)
            {
                ClearPendingWorkshopChange();
                _gate.Finish(request);
                return Fail(map, MapChangeRequestStatus.Failed, exception.Message, exception);
            }
        }
    }

    private MapChangeRequestResult StartChange(string mapId, Guid request)
    {
        if (string.IsNullOrWhiteSpace(mapId))
        {
            return new MapChangeRequestResult(MapChangeRequestStatus.MapNotFound);
        }

        MapInfo? map;
        string downloadCommand;
        int downloadTimeout;
        lock (_sync)
        {
            if (!_maps.TryGetValue(mapId.Trim(), out map))
            {
                return new MapChangeRequestResult(MapChangeRequestStatus.MapNotFound);
            }

            if (_pendingWorkshopChange is not null)
            {
                return new MapChangeRequestResult(
                    _pendingWorkshopChange.Map.Id.Equals(map.Id, StringComparison.OrdinalIgnoreCase)
                        ? MapChangeRequestStatus.DownloadAlreadyPending
                        : MapChangeRequestStatus.Busy,
                    _pendingWorkshopChange.Map);
            }

            downloadCommand = _settings.Maps.WorkshopDownloadCommand?.Trim() ?? string.Empty;
            downloadTimeout = Math.Clamp(_settings.Maps.WorkshopDownloadTimeoutSeconds, 30, 3600);
        }

        if (map.Kind == MapKind.Local)
        {
            if (!TryCheckMapAvailable(map, out var error))
            {
                return Fail(map, MapChangeRequestStatus.InvalidMap, error);
            }

            return IssueMapChange(map, $"changelevel {map.Value}", request);
        }

        if (TryCheckMapAvailable(map, out _))
        {
            return IssueMapChange(map, $"host_workshop_map {map.Value}", request);
        }

        if (string.IsNullOrWhiteSpace(downloadCommand)
            || downloadCommand.Any(character => !char.IsAsciiLetterOrDigit(character) && character != '_'))
        {
            return Fail(
                map,
                MapChangeRequestStatus.WorkshopDownloadDisabled,
                "Workshop downloads are disabled. Install the map on the server first.");
        }

        lock (_sync)
        {
            if (_pendingWorkshopChange is not null)
            {
                return new MapChangeRequestResult(MapChangeRequestStatus.Busy, map);
            }

            _pendingWorkshopChange = new PendingWorkshopChange(
                request, map,
                DateTimeOffset.UtcNow.AddSeconds(downloadTimeout),
                downloadTimeout);
        }

        _gate.Downloading(request);
        try
        {
            _core.Engine.ExecuteCommand($"{downloadCommand} {map.Value}");
        }
        catch (Exception exception)
        {
            ClearPendingWorkshopChange();
            return Fail(map, MapChangeRequestStatus.Failed, exception.Message, exception);
        }

        _logger.LogInformation(
            "[sws2-cs2mapmanager] Requested Workshop download for {MapName} ({WorkshopId})",
            map.Name,
            map.Value);
        RaiseProgress(map, MapChangeProgress.DownloadStarted);
        return SchedulePendingCheck()
            ? new MapChangeRequestResult(MapChangeRequestStatus.DownloadStarted, map)
            : new MapChangeRequestResult(MapChangeRequestStatus.Failed, map, "Unable to schedule Workshop download checks.");
    }

    public void Dispose()
    {
        lock (_sync)
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            _gate.Cancel();
            _changeTimer?.Cancel();
            _changeTimer = null;
            _pendingCheck?.Cancel();
            _pendingCheck = null;
            _pendingWorkshopChange = null;
        }

        _core.Event.OnMapUnload -= OnMapUnload;
        _core.Event.OnConsoleOutput -= OnConsoleOutput;
        MapChangeProgressed = null;
        MapChangeControlChanged = null;
    }

    private void RebuildMapCatalog()
    {
        lock (_sync)
        {
            RebuildMapCatalogLocked();
        }
    }

    private void RebuildMapCatalogLocked()
    {
        _maps.Clear();
        _mapOrder.Clear();
        foreach (var configured in _configuredMaps)
        {
            if (configured is null)
            {
                continue;
            }

            var hasMapName = !string.IsNullOrWhiteSpace(configured.MapName);
            var hasWorkshopId = !string.IsNullOrWhiteSpace(configured.WorkshopId);
            if (hasMapName == hasWorkshopId)
            {
                _logger.LogWarning(
                    "[sws2-cs2mapmanager] Ignoring map entry '{Name}': set exactly one of MapName or WorkshopId",
                    configured.Name);
                continue;
            }

            MapInfo map;
            if (hasMapName)
            {
                var mapName = configured.MapName!.Trim();
                if (!IsSafeMapName(mapName))
                {
                    _logger.LogWarning("[sws2-cs2mapmanager] Ignoring unsafe map name '{MapName}'", mapName);
                    continue;
                }

                map = new MapInfo(
                    $"map:{mapName.ToLowerInvariant()}",
                    string.IsNullOrWhiteSpace(configured.Name) ? mapName : configured.Name.Trim(),
                    MapKind.Local,
                    mapName);
            }
            else
            {
                if (!ulong.TryParse(configured.WorkshopId!.Trim(), NumberStyles.None, CultureInfo.InvariantCulture, out var workshopId)
                    || workshopId == 0)
                {
                    _logger.LogWarning(
                        "[sws2-cs2mapmanager] Ignoring invalid Workshop ID '{WorkshopId}'",
                        configured.WorkshopId);
                    continue;
                }

                var workshopIdText = workshopId.ToString(CultureInfo.InvariantCulture);
                map = new MapInfo(
                    $"workshop:{workshopIdText}",
                    string.IsNullOrWhiteSpace(configured.Name) ? workshopIdText : configured.Name.Trim(),
                    MapKind.Workshop,
                    workshopIdText);
            }

            map = map with
            {
                CkzTier = configured.CkzTier is > 0 ? configured.CkzTier : null,
                VnlTier = configured.VnlTier is > 0 ? configured.VnlTier : null,
                CkzNubTier = configured.CkzNubTier is > 0 ? configured.CkzNubTier : null,
                CkzProTier = configured.CkzProTier is > 0 ? configured.CkzProTier : null,
                VnlNubTier = configured.VnlNubTier is > 0 ? configured.VnlNubTier : null,
                VnlProTier = configured.VnlProTier is > 0 ? configured.VnlProTier : null
            };
            if (!_maps.TryAdd(map.Id, map))
            {
                _logger.LogWarning("[sws2-cs2mapmanager] Ignoring duplicate map entry '{MapId}'", map.Id);
            }
            else
            {
                _mapOrder.Add(map);
            }
        }

        _logger.LogInformation(
            "[sws2-cs2mapmanager] Loaded {Count} map(s): {Maps}",
            _maps.Count,
            string.Join(", ", _mapOrder.Select(map => $"{map.Name} ({map.Value})")));
    }

    private static bool IsSafeMapName(string mapName)
    {
        return mapName.Length is > 0 and <= 128
            && mapName.All(character => char.IsAsciiLetterOrDigit(character) || character is '_' or '-');
    }

    private bool TryCheckMapAvailable(MapInfo map, out string error)
    {
        try
        {
            if (_core.Engine.IsMapValid(map.Value)
                || (map.Kind == MapKind.Workshop && IsWorkshopAddonInstalled(map.Value)))
            {
                error = string.Empty;
                return true;
            }

            error = map.Kind == MapKind.Workshop
                ? $"Workshop map is not installed: {map.Value}"
                : $"Local map is not installed: {map.Value}";
            return false;
        }
        catch (Exception exception)
        {
            _logger.LogWarning(exception, "[sws2-cs2mapmanager] Failed to validate map {MapId}", map.Id);
            error = exception.Message;
            return false;
        }
    }

    /// <summary>
    /// MultiAddonManager stores downloads below the executable search path. SwiftlyS2's
    /// <c>IsMapValid</c> only handles installed game maps reliably, so check the downloaded
    /// addon files directly before issuing <c>host_workshop_map</c>.
    /// </summary>
    private bool IsWorkshopAddonInstalled(string workshopId)
    {
        try
        {
            var searchPath = _core.GameFileSystem.GetSearchPath(
                "EXECUTABLE_PATH",
                GetSearchPathTypes_t.GET_SEARCH_PATH_ALL,
                1);

            foreach (var basePath in new[] { searchPath, _core.GameDirectory, _core.GameFilesDirectory })
            {
                if (string.IsNullOrWhiteSpace(basePath))
                {
                    continue;
                }

                var addonDirectory = Path.Combine(
                    basePath.TrimEnd('/', '\\'),
                    "steamapps",
                    "workshop",
                    "content",
                    "730",
                    workshopId);

                if (File.Exists(Path.Combine(addonDirectory, $"{workshopId}_dir.vpk"))
                    || File.Exists(Path.Combine(addonDirectory, $"{workshopId}.vpk")))
                {
                    return true;
                }
            }

            return false;
        }
        catch (Exception exception)
        {
            _logger.LogDebug(
                exception,
                "[sws2-cs2mapmanager] Failed to inspect Workshop addon {WorkshopId}",
                workshopId);
            return false;
        }
    }

    private MapChangeRequestResult IssueMapChange(MapInfo map, string command, Guid request)
    {
        try
        {
            if (_disposed || !_gate.IsCurrent(request)) return new(MapChangeRequestStatus.Failed, map, "Request cancelled.");
            _gate.Finish(request);
            _core.Engine.ExecuteCommand(command);
            _logger.LogInformation(
                "[sws2-cs2mapmanager] Issued map change to {MapName} ({MapId})",
                map.Name,
                map.Id);
            RaiseProgress(map, MapChangeProgress.ChangeIssued);
            return new MapChangeRequestResult(MapChangeRequestStatus.ChangeStarted, map);
        }
        catch (Exception exception)
        {
            return Fail(map, MapChangeRequestStatus.Failed, exception.Message, exception);
        }
    }

    private bool SchedulePendingCheck()
    {
        lock (_sync)
        {
            if (_disposed || _pendingWorkshopChange is null)
            {
                return false;
            }

            var request = _pendingWorkshopChange.Request;
            try
            {
                _pendingCheck = _core.Scheduler.DelayBySeconds(1, () => CheckPendingWorkshopChange(false, request));
                return true;
            }
            catch (Exception exception)
            {
                var map = _pendingWorkshopChange.Map;
                ClearPendingWorkshopChange();
                _gate.Finish(request);
                Fail(map, MapChangeRequestStatus.Failed, exception.Message, exception);
                return false;
            }
        }
    }

    private void CheckPendingWorkshopChange(bool force, Guid expected)
    {
        PendingWorkshopChange? pending;
        lock (_sync)
        {
            if (_disposed || _pendingWorkshopChange is null)
            {
                return;
            }

            pending = _pendingWorkshopChange;
            if (pending.Request != expected || !_gate.IsCurrent(pending.Request)) return;
            // A forced completion can overtake the polling timer. Retire that timer too.
            _pendingCheck?.Cancel();
            _pendingCheck = null;
        }

        bool isReady;
        if (force)
        {
            isReady = true;
        }
        else
        {
            try
            {
                isReady = _core.Engine.IsMapValid(pending.Map.Value)
                    || IsWorkshopAddonInstalled(pending.Map.Value);
            }
            catch (Exception exception)
            {
                _logger.LogWarning(exception, "[sws2-cs2mapmanager] Failed to check Workshop map {WorkshopId}", pending.Map.Value);
                isReady = false;
            }
        }

        if (isReady)
        {
            lock (_sync)
            {
                if (!ReferenceEquals(_pendingWorkshopChange, pending) || _disposed)
                {
                    return;
                }

                _pendingWorkshopChange = null;
                _logger.LogInformation(
                    "[sws2-cs2mapmanager] Workshop addon {WorkshopId} finished downloading; changing map to {MapName}",
                    pending.Map.Value,
                    pending.Map.Name);
                IssueMapChange(pending.Map, $"host_workshop_map {pending.Map.Value}", pending.Request);
                return;
            }
        }

        if (DateTimeOffset.UtcNow >= pending.Deadline)
        {
            lock (_sync)
            {
                if (!ReferenceEquals(_pendingWorkshopChange, pending))
                {
                    return;
                }

                _pendingWorkshopChange = null;
            }

            _gate.Finish(pending.Request);
            Fail(
                pending.Map,
                MapChangeRequestStatus.Failed,
                $"Workshop download timed out after {pending.TimeoutSeconds} seconds.");
            return;
        }

        lock (_sync)
        {
            if (ReferenceEquals(_pendingWorkshopChange, pending)) SchedulePendingCheck();
        }
    }

    private void OnMapUnload(SwiftlyS2.Shared.Events.IOnMapUnloadEvent @event)
    {
        PendingWorkshopChange? cancelled;
        lock (_sync)
        {
            _gate.Pause(false, "map unload");
            _changeTimer?.Cancel(); _changeTimer = null;
            cancelled = _pendingWorkshopChange;
            _pendingWorkshopChange = null;
            _pendingCheck?.Cancel();
            _pendingCheck = null;
        }

        if (cancelled is not null)
        {
            _logger.LogInformation(
                "[sws2-cs2mapmanager] Cancelled pending Workshop change for {WorkshopId} after map unload",
                cancelled.Map.Value);
        }
    }

    /// <summary>
    /// MultiAddonManager logs a completion line when Steam finishes a download. Use it as a
    /// fallback when the server layout stores Workshop content outside the usual search paths.
    /// </summary>
    private void OnConsoleOutput(SwiftlyS2.Shared.Events.IOnConsoleOutputEvent @event)
    {
        PendingWorkshopChange? pending;
        lock (_sync)
        {
            pending = _pendingWorkshopChange;
        }

        if (pending is null)
        {
            return;
        }

        var message = @event.Message;
        if (string.IsNullOrEmpty(message)
            || !message.Contains("downloaded successfully", StringComparison.OrdinalIgnoreCase)
            || !message.Contains($"Addon {pending.Map.Value} downloaded successfully", StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        _logger.LogInformation(
            "[sws2-cs2mapmanager] MultiAddonManager reported Workshop addon {WorkshopId} as downloaded",
            pending.Map.Value);
        _core.Scheduler.NextTick(() => CheckPendingWorkshopChange(force: true, expected: pending.Request));
    }

    private void ClearPendingWorkshopChange()
    {
        lock (_sync)
        {
            _pendingWorkshopChange = null;
            _pendingCheck?.Cancel();
            _pendingCheck = null;
        }
    }

    private MapChangeRequestResult Fail(
        MapInfo map,
        MapChangeRequestStatus status,
        string error,
        Exception? exception = null)
    {
        if (exception is null)
        {
            _logger.LogWarning("[sws2-cs2mapmanager] Map change request failed for {MapName}: {Error}", map.Name, error);
        }
        else
        {
            _logger.LogError(exception, "[sws2-cs2mapmanager] Map change request failed for {MapName}", map.Name);
        }

        RaiseProgress(map, MapChangeProgress.Failed, error);
        return new MapChangeRequestResult(status, map, error);
    }

    private void RaiseControlChanged()
    {
        var handlers = MapChangeControlChanged;
        if (handlers is null) return;
        foreach (EventHandler handler in handlers.GetInvocationList())
        {
            try { handler(this, EventArgs.Empty); }
            catch (Exception exception)
            {
                _logger.LogError(exception, "[sws2-cs2mapmanager] MapChangeControlChanged handler threw");
            }
        }
    }

    private void RaiseProgress(MapInfo map, MapChangeProgress progress, string? error = null, int delaySeconds = 0)
    {
        var handlers = MapChangeProgressed;
        if (handlers is null)
        {
            return;
        }

        var args = new MapChangeProgressEventArgs(map, progress, error, delaySeconds);
        foreach (EventHandler<MapChangeProgressEventArgs> handler in handlers.GetInvocationList())
        {
            try
            {
                handler(this, args);
            }
            catch (Exception exception)
            {
                _logger.LogError(exception, "[sws2-cs2mapmanager] MapChangeProgressed handler threw");
            }
        }
    }
}
