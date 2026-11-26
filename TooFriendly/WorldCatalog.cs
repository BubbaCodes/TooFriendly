using System;
using System.Collections.Generic;
using Dalamud.Plugin.Services;
using Lumina.Excel.Sheets;

namespace TooFriendly;

public sealed record WorldMetadata(ushort Id, string Name, string DataCenter, uint RegionId);

public sealed class WorldCatalog
{
    private readonly Dictionary<ushort, WorldMetadata> worldsById = new();
    private readonly Dictionary<string, WorldMetadata> worldsByName = new(StringComparer.OrdinalIgnoreCase);

    public WorldCatalog(IDataManager dataManager)
    {
        foreach (var world in dataManager.GetExcelSheet<World>())
        {
            var name = world.Name.ToString().Trim();
            if (string.IsNullOrWhiteSpace(name) || world.DataCenter.Value.RowId == 0)
                continue;

            var dataCenter = world.DataCenter.Value;
            var metadata = new WorldMetadata(
                checked((ushort)world.RowId),
                name,
                dataCenter.Name.ToString().Trim(),
                dataCenter.Region.RowId);

            worldsById[metadata.Id] = metadata;
            worldsByName[metadata.Name] = metadata;
        }
    }

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
