using System;
using System.Collections.Generic;
using System.Linq;
using Dalamud.Plugin.Services;
using Lumina.Excel.Sheets;

namespace TooFriendly;

public sealed record WorldMetadata(
    ushort Id,
    string Name,
    string DataCenter,
    uint RegionId,
    string Region);

public sealed class WorldCatalog
{
    private readonly Dictionary<ushort, WorldMetadata> worldsById = new();
    private readonly Dictionary<string, WorldMetadata> worldsByName = new(StringComparer.OrdinalIgnoreCase);
    private readonly IReadOnlyList<WorldMetadata> worlds;

    public WorldCatalog(IDataManager dataManager)
    {
        foreach (var world in dataManager.GetExcelSheet<World>())
        {
            var name = world.Name.ToString().Trim();
            if (string.IsNullOrWhiteSpace(name) || world.DataCenter.Value.RowId == 0)
                continue;

            var dataCenter = world.DataCenter.Value;
            var regionName = dataCenter.Region.Value.Name.ToString().Trim();
            var metadata = new WorldMetadata(
                checked((ushort)world.RowId),
                name,
                dataCenter.Name.ToString().Trim(),
                dataCenter.Region.RowId,
                string.IsNullOrWhiteSpace(regionName)
                    ? $"Region {dataCenter.Region.RowId}"
                    : regionName);

            worldsById[metadata.Id] = metadata;
            worldsByName[metadata.Name] = metadata;
        }

        worlds = worldsById.Values
            .OrderBy(world => world.RegionId)
            .ThenBy(world => world.DataCenter, StringComparer.OrdinalIgnoreCase)
            .ThenBy(world => world.Name, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    public IReadOnlyList<WorldMetadata> Worlds => worlds;

    public bool TryGetById(ushort id, out WorldMetadata metadata) =>
        worldsById.TryGetValue(id, out metadata!);

    public bool TryGetByName(string? name, out WorldMetadata metadata)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            metadata = null!;
            return false;
        }

        return worldsByName.TryGetValue(name.Trim(), out metadata!);
    }
}
