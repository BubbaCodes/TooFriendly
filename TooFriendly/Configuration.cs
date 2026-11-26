using System;
using System.Collections.Generic;
using Dalamud.Configuration;

namespace TooFriendly;

[Serializable]
public enum CharacterVerificationStatus
{
    NotChecked,
    Checking,
    Found,
    NotFound,
    LookupFailed,
}

[Serializable]
public sealed class Configuration : IPluginConfiguration
{
    public int Version { get; set; } = 2;
    public int FriendLimit { get; set; } = 1000;
    public uint StoredOnlyFriendColor { get; set; } = 0xFF80D0FF;
    public float NearbyRangeYalms { get; set; } = 100f;
    public List<FriendRecord> Friends { get; set; } = new();

    public void Save()
    {
        Plugin.PluginInterface.SavePluginConfig(this);
    }
}

[Serializable]
public sealed class FriendRecord
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Name { get; set; } = string.Empty;
    public string World { get; set; } = string.Empty;
    public string DataCenter { get; set; } = string.Empty;
    public string MetWorld { get; set; } = string.Empty;
    public string MetDataCenter { get; set; } = string.Empty;
    public string City { get; set; } = string.Empty;
    public string MetNote { get; set; } = string.Empty;
    public ushort HomeWorldId { get; set; }
    public uint LodestoneRegionId { get; set; }
    public ulong ContentId { get; set; }
    public bool IsGameFriend { get; set; }
    public CharacterVerificationStatus VerificationStatus { get; set; }
    public DateTime? LastCheckedUtc { get; set; }
    public string VerificationMessage { get; set; } = string.Empty;
}

public sealed record MeetingLocation(string DataCenter, string World, string Place);
