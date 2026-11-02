namespace Sws2Cs2MapManager.Contracts;

/// <summary>Identifies where a map is loaded from.</summary>
public enum MapKind
{
    Local,
    Workshop
}

/// <summary>A configured map available for administration or voting.</summary>
public sealed record MapInfo(string Id, string Name, MapKind Kind, string Value);

/// <summary>Outcome of a request to change the server map.</summary>
public enum MapChangeRequestStatus
{
    ChangeStarted,
    DownloadStarted,
    DownloadAlreadyPending,
    Busy,
    MapNotFound,
    InvalidMap,
    WorkshopDownloadDisabled,
    Failed
}

/// <summary>Result returned immediately when requesting a map change.</summary>
public sealed record MapChangeRequestResult(
    MapChangeRequestStatus Status,
    MapInfo? Map = null,
    string? Error = null);

/// <summary>Stages reported while a map change is being processed.</summary>
public enum MapChangeProgress
{
    DownloadStarted,
    ChangeIssued,
    Failed
}

/// <summary>Progress notification for a map change request.</summary>
public sealed class MapChangeProgressEventArgs : EventArgs
{
    public MapChangeProgressEventArgs(MapInfo map, MapChangeProgress progress, string? error = null)
    {
        Map = map;
        Progress = progress;
        Error = error;
    }

    public MapInfo Map { get; }

    public MapChangeProgress Progress { get; }

    public string? Error { get; }
}

/// <summary>Shared map catalog and map-change service.</summary>
public interface IMapManagerService
{
    /// <summary>Shared-interface registration key.</summary>
    public const string InterfaceKey = "sws2-cs2mapmanager";

    /// <summary>Raised when a request starts downloading, issues a change, or fails.</summary>
    event EventHandler<MapChangeProgressEventArgs>? MapChangeProgressed;

    /// <summary>Returns the configured map catalog in configuration order.</summary>
    IReadOnlyList<MapInfo> GetMaps();

    /// <summary>Requests a map change using an ID returned by <see cref="GetMaps"/>.</summary>
    MapChangeRequestResult RequestChange(string mapId);
}
