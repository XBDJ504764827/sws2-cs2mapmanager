using System.Text.Json;
using Microsoft.Extensions.Logging;

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

/// <summary>Reads both the compact map-name-to-Workshop-ID object and legacy map arrays.</summary>
public static class ConfiguredMapReader
{
    private const string PluginSection = "sws2-cs2mapmanager";

    public static IReadOnlyList<ConfiguredMap?> ReadAvailable(string configPath, ILogger logger)
    {
        try
        {
            using var document = JsonDocument.Parse(
                File.ReadAllText(configPath),
                new JsonDocumentOptions { CommentHandling = JsonCommentHandling.Skip, AllowTrailingCommas = true });

            if (!TryGetProperty(document.RootElement, PluginSection, out var plugin)
                || !TryGetProperty(plugin, "Maps", out var maps)
                || !TryGetProperty(maps, "Available", out var available))
            {
                logger.LogWarning(
                    "[sws2-cs2mapmanager] Maps.Available is missing from {ConfigPath}; no maps will be available",
                    configPath);
                return [];
            }

            var result = new List<ConfiguredMap?>();
            if (available.ValueKind == JsonValueKind.Object)
            {
                foreach (var entry in available.EnumerateObject())
                {
                    if (entry.Value.ValueKind is JsonValueKind.String or JsonValueKind.Number)
                    {
                        var value = entry.Value.ToString().Trim();
                        if (value.StartsWith("local:", StringComparison.OrdinalIgnoreCase))
                        {
                            result.Add(new ConfiguredMap { Name = entry.Name, MapName = value[6..] });
                        }
                        else
                        {
                            result.Add(new ConfiguredMap { Name = entry.Name, WorkshopId = value });
                        }
                    }
                    else if (entry.Value.ValueKind == JsonValueKind.Object
                        && TryReadMap(entry.Value, entry.Name, out var configured))
                    {
                        result.Add(configured);
                    }
                    else
                    {
                        logger.LogWarning(
                            "[sws2-cs2mapmanager] Ignoring invalid Available entry '{MapName}'",
                            entry.Name);
                    }
                }

                return result;
            }

            if (available.ValueKind == JsonValueKind.Array)
            {
                foreach (var entry in available.EnumerateArray())
                {
                    if (entry.ValueKind == JsonValueKind.Object && TryReadMap(entry, null, out var configured))
                    {
                        result.Add(configured);
                    }
                    else
                    {
                        logger.LogWarning("[sws2-cs2mapmanager] Ignoring invalid legacy Available entry");
                    }
                }

                return result;
            }

            logger.LogWarning("[sws2-cs2mapmanager] Maps.Available must be an object or an array");
            return [];
        }
        catch (Exception exception)
        {
            logger.LogError(exception, "[sws2-cs2mapmanager] Failed to read the map catalog configuration");
            return [];
        }
    }

    private static bool TryReadMap(JsonElement element, string? fallbackName, out ConfiguredMap configured)
    {
        configured = new ConfiguredMap { Name = fallbackName };
        if (TryGetProperty(element, "Name", out var name) && name.ValueKind == JsonValueKind.String)
        {
            configured.Name = name.GetString();
        }

        if (TryGetProperty(element, "MapName", out var mapName) && mapName.ValueKind == JsonValueKind.String)
        {
            configured.MapName = mapName.GetString();
        }

        if (TryGetProperty(element, "WorkshopId", out var workshopId)
            && workshopId.ValueKind is JsonValueKind.String or JsonValueKind.Number)
        {
            configured.WorkshopId = workshopId.ToString();
        }

        return !string.IsNullOrWhiteSpace(configured.MapName)
            || !string.IsNullOrWhiteSpace(configured.WorkshopId);
    }

    private static bool TryGetProperty(JsonElement element, string name, out JsonElement value)
    {
        if (element.ValueKind == JsonValueKind.Object)
        {
            foreach (var property in element.EnumerateObject())
            {
                if (property.Name.Equals(name, StringComparison.OrdinalIgnoreCase))
                {
                    value = property.Value;
                    return true;
                }
            }
        }

        value = default;
        return false;
    }
}
