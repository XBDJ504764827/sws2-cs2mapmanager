namespace Sws2Cs2MapManager.Configuration;

/// <summary>Plugin configuration bound from config.jsonc.</summary>
public sealed class MapManagerSettings
{
    public bool Debug { get; set; }

    public MapCatalogSettings Maps { get; set; } = new();
}

public sealed class MapCatalogSettings
{
    /// <summary>Maximum time to wait for MultiAddonManager to install a Workshop map.</summary>
    public int WorkshopDownloadTimeoutSeconds { get; set; } = 600;

    /// <summary>Server command supplied by MultiAddonManager to download Workshop items.</summary>
    public string WorkshopDownloadCommand { get; set; } = "mm_download_addon";

    /// <summary>Configured maps offered to admin tools and voting plugins.</summary>
    public List<ConfiguredMap?> Available { get; set; } =
    [
        new() { Name = "de_dust2", MapName = "de_dust2" },
        new() { Name = "de_inferno", MapName = "de_inferno" },
        new() { Name = "de_mirage", MapName = "de_mirage" },
        new() { Name = "de_ancient", MapName = "de_ancient" },
        new() { Name = "de_nuke", MapName = "de_nuke" },
        new() { Name = "de_anubis", MapName = "de_anubis" },
        new() { Name = "de_overpass", MapName = "de_overpass" }
    ];
}

public sealed class ConfiguredMap
{
    /// <summary>Optional label displayed in menus and votes.</summary>
    public string? Name { get; set; }

    /// <summary>Installed game map name. Set this or WorkshopId, not both.</summary>
    public string? MapName { get; set; }

    /// <summary>Steam Workshop PublishedFileId for a CS2 map addon.</summary>
    public string? WorkshopId { get; set; }
}
