using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using SwiftlyS2.Shared;
using SwiftlyS2.Shared.Plugins;
using Sws2Cs2MapManager.Configuration;
using Sws2Cs2MapManager.Contracts;
using Sws2Cs2MapManager.Services;

namespace Sws2Cs2MapManager;

public partial class Sws2Cs2MapManager : BasePlugin
{
    private MapManagerService? _maps;
    private ServiceProvider? _provider;
    private IDisposable? _settingsSubscription;
    private string _configPath = string.Empty;

    public Sws2Cs2MapManager(ISwiftlyCore core) : base(core)
    {
    }

    public override void Load(bool hotReload)
    {
        Core.Configuration
            .InitializeWithTemplate("config.jsonc", "config.template.jsonc")
            .Configure(builder => builder.AddJsonFile("config.jsonc", optional: false, reloadOnChange: true));
        _configPath = Core.Configuration.GetConfigPath("config.jsonc");

        var services = new ServiceCollection();
        services.AddSwiftly(Core)
            .AddOptionsWithValidateOnStart<MapManagerSettings>()
            .BindConfiguration("sws2-cs2mapmanager");

        _provider = services.BuildServiceProvider();
        var options = _provider.GetRequiredService<IOptionsMonitor<MapManagerSettings>>();
        var settings = options.CurrentValue;
        var configuredMaps = ConfiguredMapReader.ReadAvailable(_configPath, Core.Logger);
        _maps = new MapManagerService(Core, settings, configuredMaps);
        _settingsSubscription = options.OnChange(newSettings => Core.Scheduler.NextTick(() =>
        {
            var reloadedMaps = ConfiguredMapReader.ReadAvailable(_configPath, Core.Logger);
            _maps?.ApplySettings(newSettings, reloadedMaps);
            Core.Logger.LogInformation("[sws2-cs2mapmanager] Configuration reloaded");
        }));
        Core.Logger.LogInformation("[sws2-cs2mapmanager] Plugin loaded (hotReload: {HotReload})", hotReload);
    }

    public override void ConfigureSharedInterface(IInterfaceManager interfaceManager)
    {
        if (_maps is not null)
        {
            interfaceManager.AddSharedInterface<IMapManagerService, MapManagerService>(
                IMapManagerService.InterfaceKey,
                _maps);
        }
    }

    public override void Unload()
    {
        _settingsSubscription?.Dispose();
        _settingsSubscription = null;
        _maps?.Dispose();
        _maps = null;
        _configPath = string.Empty;
        _provider?.Dispose();
        _provider = null;
    }
}
