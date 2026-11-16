using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace SwiftlyS2.Shared.FileSystem { public enum GetSearchPathTypes_t { GET_SEARCH_PATH_ALL } }
namespace SwiftlyS2.Shared.Events
{
    public interface IOnMapUnloadEvent { }
    public interface IOnConsoleOutputEvent { string Message { get; } }
    public sealed record ConsoleOutput(string Message) : IOnConsoleOutputEvent;
    public sealed record MapUnload : IOnMapUnloadEvent;
}
namespace SwiftlyS2.Shared
{
    public interface ISwiftlyCore
    {
        ILogger Logger { get; }
        FakeEngine Engine { get; }
        FakeScheduler Scheduler { get; }
        FakeEvents Event { get; }
        FakeFiles GameFileSystem { get; }
        string GameDirectory { get; }
        string GameFilesDirectory { get; }
    }
    public sealed class FakeCore : ISwiftlyCore
    {
        public ILogger Logger => NullLogger.Instance;
        public FakeEngine Engine { get; } = new();
        public FakeScheduler Scheduler { get; } = new();
        public FakeEvents Event { get; } = new();
        public FakeFiles GameFileSystem { get; } = new();
        public string GameDirectory => "/missing-test-server";
        public string GameFilesDirectory => GameDirectory;
    }
    public sealed class FakeEngine
    {
        public List<string> Commands { get; } = [];
        public HashSet<string> Installed { get; } = [];
        public bool IsMapValid(string value) => Installed.Contains(value);
        public void ExecuteCommand(string command) => Commands.Add(command);
    }
    public sealed class FakeFiles
    {
        public string GetSearchPath(FileSystem.GetSearchPathTypes_t unused) => "";
        public string GetSearchPath(string path, FileSystem.GetSearchPathTypes_t unused, int count) => "";
    }
    public sealed class FakeEvents
    {
        public event Action<Events.IOnMapUnloadEvent>? OnMapUnload;
        public event Action<Events.IOnConsoleOutputEvent>? OnConsoleOutput;
        public void Unload() => OnMapUnload?.Invoke(new Events.MapUnload());
        public void Downloaded(string id) => OnConsoleOutput?.Invoke(new Events.ConsoleOutput($"Addon {id} downloaded successfully"));
    }
    public sealed class FakeScheduler
    {
        public bool ThrowOnSchedule { get; set; }
        public List<(Action Action, CancellationTokenSource Token, int Seconds)> Work { get; } = [];
        public CancellationTokenSource DelayBySeconds(int seconds, Action action)
        {
            if (ThrowOnSchedule) throw new InvalidOperationException("Scheduler unavailable");
            var token = new CancellationTokenSource(); Work.Add((action, token, seconds)); return token;
        }
        public void NextTick(Action action) => DelayBySeconds(0, action);
        // Force stale callbacks too: cancellation checks must also work by request identity.
        public void RunNext(bool ignoreCancellation = false)
        {
            var item = Work[0]; Work.RemoveAt(0);
            if (ignoreCancellation || !item.Token.IsCancellationRequested) item.Action();
        }
    }
}
