using Microsoft.Extensions.Logging.Abstractions;
using Sws2Cs2MapManager.Configuration;
using Sws2Cs2MapManager.Contracts;

var checks = 0;
var path = Path.GetTempFileName();
try
{
    File.WriteAllText(path, """
        {
          // Existing short entries and rated objects can coexist.
          "sws2-cs2mapmanager": { "Maps": { "Available": {
            "old_map": "123",
            "rated": { "WorkshopId": "456", "CKZ": 2, "VNL": 7 },
            "ckz_only": { "WorkshopId": "789", "CKZ": 3, "VNL": null },
            "vnl_only": { "WorkshopId": "111", "CKZ": null, "VNL": "5" },
            "unknown": { "WorkshopId": "222", "CKZ": "", "VNL": "未定级" },
            "invalid_tier": { "WorkshopId": "333", "CKZ": -1, "VNL": 1.5 },
            "overflow": { "WorkshopId": "444", "CKZ": 999999999999, "VNL": true },
            "local": "local:de_dust2",
            "rated_local": { "MapName": "de_mirage", "ckz": 1, "vnl": 4 },
          }}}
        }
        """);
    var maps = ConfiguredMapReader.ReadAvailable(path, NullLogger.Instance);
    Check(maps.Count == 9, "Compact and extended entries coexist in configuration order");
    Check(maps[0] is { WorkshopId: "123", CkzTier: null, VnlTier: null }, "Old entries remain unrated");
    Check(maps[1] is { CkzTier: 2, VnlTier: 7 }, "Distinct CKZ and VNL values are preserved");
    Check(maps[2] is { CkzTier: 3, VnlTier: null }, "Missing VNL does not borrow CKZ's tier");
    Check(maps[3] is { CkzTier: null, VnlTier: 5 }, "Missing CKZ does not borrow VNL; quoted numbers work");
    Check(maps.Skip(4).Take(3).All(map => map is { CkzTier: null, VnlTier: null }),
        "Blank, placeholder, negative, fractional, boolean and overflowing tiers remain unknown");
    Check(maps[7] is { MapName: "de_dust2" } && maps[8] is { MapName: "de_mirage", CkzTier: 1, VnlTier: 4 },
        "Local maps and case-insensitive mode keys work");

    File.WriteAllText(path, """
        {"sws2-cs2mapmanager":{"Maps":{"Available":[
          {"Name":"legacy", "WorkshopId":123, "CKZ":4, "VNL":8}
        ]}}}
        """);
    maps = ConfiguredMapReader.ReadAvailable(path, NullLogger.Instance);
    Check(maps.Single() is { Name: "legacy", WorkshopId: "123", CkzTier: 4, VnlTier: 8 },
        "Legacy arrays retain both mode tiers");

    var sharedMap = new MapInfo("workshop:456", "rated", MapKind.Workshop, "456") { CkzTier = 2, VnlTier = 7 };
    Check((sharedMap with { Name = "renamed" }) is { CkzTier: 2, VnlTier: 7 }, "Shared records preserve mode tiers");
    Check(new MapInfo("workshop:123", "old", MapKind.Workshop, "123") is { CkzTier: null, VnlTier: null },
        "Existing four-argument API consumers remain compatible");
    Console.WriteLine($"Passed {checks} map catalog checks.");
}
finally
{
    File.Delete(path);
}

void Check(bool condition, string message)
{
    if (!condition)
    {
        throw new InvalidOperationException(message);
    }
    checks++;
    Console.WriteLine($"PASS: {message}");
}
