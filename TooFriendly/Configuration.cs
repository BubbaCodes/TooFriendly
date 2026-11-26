using System;
using System.Collections.Generic;
using Dalamud.Configuration;

namespace TooFriendly;

[Serializable]
public sealed class Configuration : IPluginConfiguration
{
    public int Version { get; set; } = 1;
    public int FriendLimit { get; set; } = 1000;
    public uint StoredOnlyFriendColor { get; set; } = 0xFF80D0FF;
    public List<FriendRecord> Friends { get; set; } = new();

    public void Save()
    {
        Plugin.PluginInterface.SavePluginConfig(this);
    }
}

[Serializable]
public sealed class FriendRecord
{
    public string Name { get; set; } = string.Empty;
    public string World { get; set; } = string.Empty;
    public string DataCenter { get; set; } = string.Empty;
    public string City { get; set; } = string.Empty;
    public string MetNote { get; set; } = string.Empty;
}
