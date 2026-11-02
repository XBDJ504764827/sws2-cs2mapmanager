using System.Globalization;
using Microsoft.Extensions.Logging;
using SwiftlyS2.Shared;
using Sws2Cs2MapManager.Configuration;
using Sws2Cs2MapManager.Contracts;

namespace Sws2Cs2MapManager.Services;

/// <summary>Owns the configured map catalog and coordinates map changes.</summary>
internal sealed class MapManagerService : IMapManagerService, IDisposable
{
    private sealed record PendingWorkshopChange(MapInfo Map, DateTimeOffset Deadline, int TimeoutSeconds);

    private readonly ISwiftlyCore _core;
    private readonly ILogger _logger;
    private readonly object _sync = new();
    private readonly Dictionary<string, MapInfo> _maps = new(StringComparer.OrdinalIgnoreCase);

    private MapManagerSettings _settings;
    private PendingWorkshopChange? _pendingWorkshopChange;
    private CancellationTokenSource? _pendingCheck;
    private bool _disposed;

    public MapManagerService(ISwiftlyCore core, MapManagerSettings settings)
    {
        _core = core;
        _logger = core.Logger;
        _settings = settings;
        RebuildMapCatalog();
        _core.Event.OnMapUnload += OnMapUnload;
    }

    public event EventHandler<MapChangeProgressEventArgs>? MapChangeProgressed;

    public void ApplySettings(MapManagerSettings settings)
    {
        lock (_sync)
        {
            _settings = settings;
            RebuildMapCatalogLocked();
        }
    }

    public IReadOnlyList<MapInfo> GetMaps()
    {
        lock (_sync)
        {
            return _maps.Values.ToArray();
        }
    }

    public MapChangeRequestResult RequestChange(string mapId)
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

            downloadCommand = _settings.Maps.WorkshopDownloadCommand.Trim();
            downloadTimeout = Math.Clamp(_settings.Maps.WorkshopDownloadTimeoutSeconds, 30, 3600);
        }

        if (map.Kind == MapKind.Local)
        {
            if (!TryCheckMapAvailable(map, out var error))
            {
                return Fail(map, MapChangeRequestStatus.InvalidMap, error);
            }

            return IssueMapChange(map, $"changelevel {map.Value}");
        }

        if (TryCheckMapAvailable(map, out _))
        {
            return IssueMapChange(map, $"host_workshop_map {map.Value}");
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
                map,
                DateTimeOffset.UtcNow.AddSeconds(downloadTimeout),
                downloadTimeout);
        }

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
        SchedulePendingCheck();
        return new MapChangeRequestResult(MapChangeRequestStatus.DownloadStarted, map);
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
            _pendingCheck?.Cancel();
            _pendingCheck = null;
            _pendingWorkshopChange = null;
        }

        _core.Event.OnMapUnload -= OnMapUnload;
        MapChangeProgressed = null;
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
        var configuredMaps = _settings.Maps.Available ?? [];
        foreach (var configured in configuredMaps)
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

            if (!_maps.TryAdd(map.Id, map))
            {
                _logger.LogWarning("[sws2-cs2mapmanager] Ignoring duplicate map entry '{MapId}'", map.Id);
            }
        }

        _logger.LogInformation("[sws2-cs2mapmanager] Loaded {Count} map(s)", _maps.Count);
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
            if (_core.Engine.IsMapValid(map.Value))
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

    private MapChangeRequestResult IssueMapChange(MapInfo map, string command)
    {
        try
        {
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

    private void SchedulePendingCheck()
    {
        lock (_sync)
        {
            if (_disposed || _pendingWorkshopChange is null)
            {
                return;
            }

            _pendingCheck = _core.Scheduler.DelayBySeconds(1, CheckPendingWorkshopChange);
        }
    }

    private void CheckPendingWorkshopChange()
    {
        PendingWorkshopChange? pending;
        lock (_sync)
        {
            _pendingCheck = null;
            if (_disposed || _pendingWorkshopChange is null)
            {
                return;
            }

            pending = _pendingWorkshopChange;
        }

        bool isReady;
        try
        {
            isReady = _core.Engine.IsMapValid(pending.Map.Value);
        }
        catch (Exception exception)
        {
            _logger.LogWarning(exception, "[sws2-cs2mapmanager] Failed to check Workshop map {WorkshopId}", pending.Map.Value);
            isReady = false;
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
            }

            IssueMapChange(pending.Map, $"host_workshop_map {pending.Map.Value}");
            return;
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

            Fail(
                pending.Map,
                MapChangeRequestStatus.Failed,
                $"Workshop download timed out after {pending.TimeoutSeconds} seconds.");
            RaiseProgress(
                pending.Map,
                MapChangeProgress.Failed,
                $"Workshop download timed out after {pending.TimeoutSeconds} seconds.");
            return;
        }

        SchedulePendingCheck();
    }

    private void OnMapUnload(SwiftlyS2.Shared.Events.IOnMapUnloadEvent @event)
    {
        PendingWorkshopChange? cancelled;
        lock (_sync)
        {
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

        return new MapChangeRequestResult(status, map, error);
    }

    private void RaiseProgress(MapInfo map, MapChangeProgress progress, string? error = null)
    {
        var handlers = MapChangeProgressed;
        if (handlers is null)
        {
            return;
        }

        var args = new MapChangeProgressEventArgs(map, progress, error);
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
