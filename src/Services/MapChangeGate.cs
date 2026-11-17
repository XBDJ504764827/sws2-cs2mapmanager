using Sws2Cs2MapManager.Contracts;

namespace Sws2Cs2MapManager.Services;

/// <summary>Owns request identities so cancelled timers/download callbacks can never execute a newer request.</summary>
internal sealed class MapChangeGate
{
    public MapChangeState State { get; private set; } = new(false, null, null, null, null, false);
    private Guid? _request;

    public MapChangeRequestStatus Begin(MapInfo map, int delaySeconds, string source, out Guid request)
    {
        request = Guid.Empty;
        if (State.IsPaused) return MapChangeRequestStatus.Paused;
        if (_request.HasValue) return MapChangeRequestStatus.Busy;
        _request = request = Guid.NewGuid();
        State = State with { PendingMap = map, Source = source, ScheduledAt = DateTimeOffset.UtcNow.AddSeconds(delaySeconds), IsDownloading = false };
        return MapChangeRequestStatus.Scheduled;
    }

    public bool IsCurrent(Guid request) => !State.IsPaused && _request == request;

    public void Downloading(Guid request)
    {
        if (IsCurrent(request)) State = State with { IsDownloading = true, ScheduledAt = null };
    }

    public MapInfo? Cancel()
    {
        var map = State.PendingMap;
        _request = null;
        State = State with { PendingMap = null, Source = null, ScheduledAt = null, IsDownloading = false };
        return map;
    }

    public void Finish(Guid request) { if (IsCurrent(request)) Cancel(); }

    public MapInfo? Pause(bool paused, string actor)
    {
        var cancelled = Cancel();
        State = State with { IsPaused = paused, PausedBy = paused ? actor : null };
        return cancelled;
    }
}
