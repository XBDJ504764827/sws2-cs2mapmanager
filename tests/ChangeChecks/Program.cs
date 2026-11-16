using SwiftlyS2.Shared;
using Sws2Cs2MapManager.Configuration;
using Sws2Cs2MapManager.Contracts;
using Sws2Cs2MapManager.Services;

var checks = 0;
var core = new FakeCore();
core.Engine.Installed.Add("de_dust2");
using var service = new MapManagerService(core, new MapManagerSettings(),
    [new ConfiguredMap { MapName = "de_dust2" }, new ConfiguredMap { Name = "KZ", WorkshopId = "123" }]);
var events = new List<MapChangeProgress>();
service.MapChangeProgressed += (_, args) => events.Add(args.Progress);
Check(service.ScheduleChange("map:de_dust2", 5, "rtv").Status == MapChangeRequestStatus.Scheduled, "RTV countdown is owned by mapmanager");
Check(core.Engine.Commands.Count == 0 && service.GetChangeState().Source == "rtv", "No map command before the deadline");
Check(service.CancelPendingChange(), "Administrator cancels pending countdown");
core.Scheduler.RunNext(ignoreCancellation: true);
Check(core.Engine.Commands.Count == 0, "Even an already-queued cancelled timer cannot change the map");
Check(events.Contains(MapChangeProgress.Cancelled), "Consumers receive cancellation for vote cleanup");
service.ScheduleChange("map:de_dust2", 3, "admin");
service.SetChangesPaused(true, "Owner");
core.Scheduler.RunNext(ignoreCancellation: true);
Check(core.Engine.Commands.Count == 0 && service.GetChangeState().IsPaused, "Pause cancels pending admin countdown");
Check(service.RequestChange("map:de_dust2").Status == MapChangeRequestStatus.Paused, "Pause blocks direct requests from other plugins");
Check(service.ScheduleChange("workshop:123", 3, "rtv").Status == MapChangeRequestStatus.Paused, "Pause blocks new RTV countdowns");
service.SetChangesPaused(false, "Owner");
Check(service.GetChangeState().PendingMap is null, "Resume never revives a cancelled change");
Check(service.RequestChange("workshop:123").Status == MapChangeRequestStatus.DownloadStarted, "Missing map starts a managed download");
Check(core.Engine.Commands.Single() == "mm_download_addon 123" && service.GetChangeState().IsDownloading, "Download stage remains cancellable");
service.CancelPendingChange();
core.Event.Downloaded("123");
core.Scheduler.RunNext(ignoreCancellation: true);
Check(core.Engine.Commands.Count == 1, "Completion of a cancelled download cannot switch maps");
service.RequestChange("workshop:123");
core.Event.Downloaded("123"); // Queued callback for old request.
service.CancelPendingChange();
service.RequestChange("workshop:123");
core.Scheduler.RunNext(ignoreCancellation: true); // old polling timer
core.Scheduler.RunNext(ignoreCancellation: true); // old forced completion
Check(core.Engine.Commands.Count == 3, "Old completion callback cannot execute a replacement request");
service.CancelPendingChange();
core.Scheduler.Work.Clear();
service.ScheduleChange("map:de_dust2", 2, "rtv");
core.Scheduler.RunNext();
Check(core.Engine.Commands.Last() == "changelevel de_dust2", "Uncancelled countdown changes exactly once");
Check(service.GetChangeState().PendingMap is null, "Issued map command clears pending state");
Check(!service.CancelPendingChange(), "An engine command already issued is not reported as cancelled");
service.SetChangesPaused(true, "Owner");
core.Event.Unload();
Check(!service.GetChangeState().IsPaused, "Map unload resets temporary pause");
service.ScheduleChange("map:de_dust2", 5, "rtv");
service.Dispose();
var before = core.Engine.Commands.Count;
core.Scheduler.RunNext(ignoreCancellation: true);
Check(core.Engine.Commands.Count == before, "Plugin unload prevents queued map changes");
// Isolate replacement requests to verify stale pollers cannot disturb their timer.
var replacementCore = new FakeCore();
using var replacement = new MapManagerService(replacementCore, new MapManagerSettings(),
    [new ConfiguredMap { WorkshopId = "123" }]);
replacement.RequestChange("workshop:123");
replacement.CancelPendingChange();
replacement.RequestChange("workshop:123");
var replacementTimer = replacementCore.Scheduler.Work[1].Token;
replacementCore.Scheduler.RunNext(ignoreCancellation: true);
Check(replacementCore.Scheduler.Work.Count == 1 && !replacementTimer.IsCancellationRequested,
    "A stale poll cannot clear, cancel or duplicate the replacement polling timer");
replacement.CancelPendingChange();
Check(replacementTimer.IsCancellationRequested, "Cancellation still reaches the replacement timer");
replacementCore.Scheduler.Work.Clear();
replacement.RequestChange("workshop:123");
replacementCore.Event.Downloaded("1123");
Check(replacementCore.Scheduler.Work.Count == 1, "Another workshop ID cannot trigger completion by substring");
replacementCore.Event.Downloaded("123");
var pollingToken = replacementCore.Scheduler.Work[0].Token;
replacementCore.Scheduler.Work[1].Action();
Check(pollingToken.IsCancellationRequested, "Forced completion retires the existing polling timer");
Check(replacementCore.Engine.Commands.Last() == "host_workshop_map 123", "Matching download completion changes maps normally");
var delivered = 0;
replacement.MapChangeControlChanged += (_, _) => throw new Exception("Consumer failure");
replacement.MapChangeControlChanged += (_, _) => delivered++;
replacement.SetChangesPaused(true, "Owner");
replacement.SetChangesPaused(false, "Owner");
Check(delivered == 2 && !replacement.GetChangeState().IsPaused,
    "A throwing consumer cannot prevent pause/resume or notifications to other consumers");
var removedCore = new FakeCore();
using var removed = new MapManagerService(removedCore, new MapManagerSettings(), [new ConfiguredMap { MapName = "de_dust2" }]);
var failures = 0;
removed.MapChangeProgressed += (_, args) => { if (args.Progress == MapChangeProgress.Failed) failures++; };
removed.ScheduleChange("map:de_dust2", 5, "rtv");
removed.ApplySettings(new MapManagerSettings(), []);
removedCore.Scheduler.RunNext();
Check(failures == 1 && removed.GetChangeState().PendingMap is null && removedCore.Engine.Commands.Count == 0,
    "Removing a map during countdown reports failure and releases the request");
removed.ApplySettings(new MapManagerSettings(), [new ConfiguredMap { MapName = "de_dust2" }]);
removedCore.Scheduler.ThrowOnSchedule = true;
Check(removed.ScheduleChange("map:de_dust2", 5, "rtv").Status == MapChangeRequestStatus.Failed
    && removed.GetChangeState().PendingMap is null, "Scheduler failure reports failure without leaving a stuck countdown");
replacementCore.Scheduler.ThrowOnSchedule = true;
Check(replacement.RequestChange("workshop:123").Status == MapChangeRequestStatus.Failed
    && replacement.GetChangeState().PendingMap is null, "Failed download polling releases the request and returns failure");
var delayedCore = new FakeCore();
using var delayed = new MapManagerService(delayedCore, new MapManagerSettings(),
    [new ConfiguredMap { WorkshopId = "456" }, new ConfiguredMap { MapName = "de_dust2" }]);
delayed.ScheduleChange("workshop:456", 5, "rtv");
Check(delayedCore.Scheduler.Work[0].Seconds == 5 && delayedCore.Engine.Commands.Count == 0,
    "Five-second RTV countdown does not start downloading early");
delayedCore.Scheduler.RunNext();
Check(delayedCore.Engine.Commands.SequenceEqual(["mm_download_addon 456"]) && delayed.GetChangeState().IsDownloading,
    "Countdown expiry starts the missing-map download without changing maps");
delayedCore.Event.Downloaded("456");
delayedCore.Scheduler.RunNext(); // Poll without a file available yet.
delayedCore.Scheduler.RunNext(); // Workshop completion notification.
Check(delayedCore.Engine.Commands.SequenceEqual(["mm_download_addon 456", "host_workshop_map 456"]),
    "Map switch happens only after download completion");
delayedCore.Scheduler.Work.Clear();
delayedCore.Engine.Installed.Add("de_dust2");
delayed.ScheduleChange("map:de_dust2", 3, "admin");
Check(delayedCore.Scheduler.Work[0].Seconds == 3, "Administrator countdown can independently remain three seconds");
delayedCore.Scheduler.RunNext();
Check(delayedCore.Engine.Commands.Last() == "changelevel de_dust2", "Installed administrator map switches at its countdown deadline");
Console.WriteLine($"Passed {checks} shared map change checks.");
void Check(bool condition, string message)
{
    if (!condition) throw new Exception(message);
    checks++; Console.WriteLine("PASS: " + message);
}
