using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using Dalamud.Game.Command;
using Dalamud.Game.Gui.ContextMenu;
using Dalamud.IoC;
using Dalamud.Interface.Windowing;
using Dalamud.Plugin;
using Dalamud.Plugin.Services;
using FFXIVClientStructs.FFXIV.Client.Game.Character;
using FFXIVClientStructs.FFXIV.Client.UI;
using FFXIVClientStructs.FFXIV.Client.UI.Info;
using TooFriendly.Windows;

namespace TooFriendly;

public sealed class Plugin : IDalamudPlugin
{
    [PluginService]
    internal static IDalamudPluginInterface PluginInterface { get; private set; } = null!;

    [PluginService]
    internal static ICommandManager CommandManager { get; private set; } = null!;

    [PluginService]
    internal static IContextMenu ContextMenuService { get; private set; } = null!;

    [PluginService]
    internal static IChatGui ChatGui { get; private set; } = null!;

    [PluginService]
    internal static IDataManager DataManager { get; private set; } = null!;

    [PluginService]
    internal static IFramework Framework { get; private set; } = null!;

    [PluginService]
    internal static IGameGui GameGui { get; private set; } = null!;

    [PluginService]
    internal static IObjectTable ObjectTable { get; private set; } = null!;

    [PluginService]
    internal static IPluginLog PluginLog { get; private set; } = null!;

    private const string CommandName = "/toofriendly";
    private const uint NotFoundStatusFlag = 1u << 9;
    private readonly WindowSystem windowSystem = new("TooFriendly");
    private readonly FriendListWindow friendListWindow;
    private readonly WorldCatalog worldCatalog;
    private readonly LodestoneVerifier lodestoneVerifier;
    private readonly HashSet<Guid> nearbyFriendIds = new();
    private DateTime nextFriendSyncUtc;
    private DateTime nextNearbyUpdateUtc;
    private DateTime friendListOpenedUtc;
    private bool friendListWasVisible;

    public Configuration Configuration { get; }

    public Plugin()
    {
        Configuration = PluginInterface.GetPluginConfig() as Configuration ?? new Configuration();
        Configuration.FriendLimit = Math.Clamp(Configuration.FriendLimit, 1, 1000);
        Configuration.NearbyRangeYalms = float.IsFinite(Configuration.NearbyRangeYalms)
            ? Math.Clamp(Configuration.NearbyRangeYalms, 5f, 200f)
            : 100f;
        Configuration.Friends ??= new();
        worldCatalog = new WorldCatalog(DataManager);
        NormalizeRecords();

        lodestoneVerifier = new LodestoneVerifier(PluginLog);
        friendListWindow = new FriendListWindow(
            Configuration,
            worldCatalog,
            QueueManualCharacterCheck,
            nearbyFriendIds);
        windowSystem.AddWindow(friendListWindow);

        CommandManager.AddHandler(CommandName, new CommandInfo((_, _) => friendListWindow.Toggle())
        {
            HelpMessage = "Open the TooFriendly local friends list.",
        });

        Framework.Update += OnFrameworkUpdate;
        ContextMenuService.OnMenuOpened += OnMenuOpened;
        PluginInterface.UiBuilder.Draw += windowSystem.Draw;
        PluginInterface.UiBuilder.OpenMainUi += friendListWindow.Toggle;
    }

    public void Dispose()
    {
        Framework.Update -= OnFrameworkUpdate;
        ContextMenuService.OnMenuOpened -= OnMenuOpened;
        PluginInterface.UiBuilder.Draw -= windowSystem.Draw;
        PluginInterface.UiBuilder.OpenMainUi -= friendListWindow.Toggle;
        CommandManager.RemoveHandler(CommandName);
        windowSystem.RemoveAllWindows();
        lodestoneVerifier.Dispose();
    }

    private void OnMenuOpened(IMenuOpenedArgs args)
    {
        if (args.MenuType != ContextMenuType.Default ||
            args.Target is not MenuTargetDefault target ||
            string.IsNullOrWhiteSpace(target.TargetName))
        {
            return;
        }

        var targetName = target.TargetName.Trim();
        var homeWorldId = checked((ushort)target.TargetHomeWorld.RowId);
        if (homeWorldId == 0 || !worldCatalog.TryGetById(homeWorldId, out var world))
            return;

        var contentId = target.TargetContentId;
        var exists = Configuration.Friends.Any(friend =>
            (contentId != 0 && friend.ContentId == contentId) ||
            (friend.HomeWorldId == world.Id &&
             string.Equals(friend.Name, targetName, StringComparison.OrdinalIgnoreCase)));
        var atLimit = Configuration.Friends.Count >= Configuration.FriendLimit;

        args.AddMenuItem(new MenuItem
        {
            Name = exists
                ? "Already in TooFriendly"
                : atLimit
                    ? "TooFriendly list is full"
                    : "Add to TooFriendly",
            IsEnabled = !exists && !atLimit,
            OnClicked = _ => AddCharacterFromMenu(targetName, world, contentId),
        });
    }

    private void AddCharacterFromMenu(string name, WorldMetadata world, ulong contentId)
    {
        var exists = Configuration.Friends.Any(friend =>
            (contentId != 0 && friend.ContentId == contentId) ||
            (friend.HomeWorldId == world.Id &&
             string.Equals(friend.Name, name, StringComparison.OrdinalIgnoreCase)));
        if (exists)
        {
            ChatGui.PrintError($"{name} @ {world.Name} is already in TooFriendly.", "TooFriendly");
            return;
        }

        if (Configuration.Friends.Count >= Configuration.FriendLimit)
        {
            ChatGui.PrintError("The TooFriendly friend limit has been reached.", "TooFriendly");
            return;
        }

        Configuration.Friends.Add(new FriendRecord
        {
            Name = name,
            World = world.Name,
            DataCenter = world.DataCenter,
            HomeWorldId = world.Id,
            LodestoneRegionId = world.RegionId,
            ContentId = contentId,
        });
        Configuration.Save();
        ChatGui.Print($"Added {name} @ {world.Name} to TooFriendly.", "TooFriendly");
    }

    private unsafe void OnFrameworkUpdate(IFramework framework)
    {
        ProcessCharacterCheckResults();

        var addon = GameGui.GetAddonByName<AddonFriendList>("FriendList", 1);
        var isFriendListVisible = addon != null && addon->IsVisible;
        if (isFriendListVisible)
        {
            var position = new Vector2(
                addon->X + addon->GetScaledWidth(true) + 12f,
                addon->Y);
            friendListWindow.UpdateSocialDock(true, position);

            var now = DateTime.UtcNow;
            if (!friendListWasVisible)
            {
                friendListOpenedUtc = now;
                nextFriendSyncUtc = now.AddSeconds(2);
            }
            else if (now >= nextFriendSyncUtc)
            {
                SyncGameFriends();
                QueueStaleCharacterChecks();
                nextFriendSyncUtc = now.AddSeconds(5);
            }
        }
        else
        {
            friendListWindow.UpdateSocialDock(false, null);
        }

        friendListWasVisible = isFriendListVisible;
        if (DateTime.UtcNow >= nextNearbyUpdateUtc)
        {
            UpdateNearbyFriends();
            nextNearbyUpdateUtc = DateTime.UtcNow.AddMilliseconds(500);
        }
    }

    private void NormalizeRecords()
    {
        var changed = false;
        foreach (var friend in Configuration.Friends)
        {
            friend.Name ??= string.Empty;
            friend.World ??= string.Empty;
            friend.DataCenter ??= string.Empty;
            friend.City ??= string.Empty;
            friend.MetNote ??= string.Empty;
            friend.VerificationMessage ??= string.Empty;

            if (friend.Id == Guid.Empty)
            {
                friend.Id = Guid.NewGuid();
                changed = true;
            }

            if (friend.VerificationStatus == CharacterVerificationStatus.Checking)
            {
                friend.VerificationStatus = CharacterVerificationStatus.NotChecked;
                changed = true;
            }

            if (friend.HomeWorldId == 0 && worldCatalog.TryGetByName(friend.World, out var metadata))
            {
                friend.HomeWorldId = metadata.Id;
                friend.LodestoneRegionId = metadata.RegionId;
                friend.World = metadata.Name;
                friend.DataCenter = metadata.DataCenter;
                changed = true;
            }
        }

        if (changed)
            Configuration.Save();
    }

    private unsafe void SyncGameFriends()
    {
        var proxy = InfoProxyFriendList.Instance();
        if (proxy == null)
            return;

        var entries = proxy->CharDataSpan.ToArray();
        if (entries.Length == 0 &&
            Configuration.Friends.Exists(friend => friend.IsGameFriend) &&
            DateTime.UtcNow < friendListOpenedUtc.AddSeconds(10))
        {
            return;
        }

        var changed = false;
        var now = DateTime.UtcNow;
        var omitted = 0;

        foreach (var friend in Configuration.Friends)
        {
            if (friend.IsGameFriend)
            {
                friend.IsGameFriend = false;
                changed = true;
            }
        }

        for (var index = 0; index < entries.Length; index++)
        {
            var entry = entries[index];
            var name = entry.NameString?.Trim() ?? string.Empty;
            var homeWorldId = entry.HomeWorld;
            if (name.Length == 0 || !worldCatalog.TryGetById(homeWorldId, out var world))
                continue;

            var contentId = proxy->GetContentIdForEntry((uint)index);
            var friend = contentId == 0
                ? null
                : Configuration.Friends.FirstOrDefault(candidate => candidate.ContentId == contentId);
            friend ??= Configuration.Friends.FirstOrDefault(candidate =>
                candidate.HomeWorldId == world.Id &&
                string.Equals(candidate.Name, name, StringComparison.OrdinalIgnoreCase));

            if (friend == null)
            {
                if (Configuration.Friends.Count >= Configuration.FriendLimit)
                {
                    omitted++;
                    continue;
                }

                friend = new FriendRecord
                {
                    Id = Guid.NewGuid(),
                    Name = name,
                    World = world.Name,
                    DataCenter = world.DataCenter,
                    HomeWorldId = world.Id,
                    LodestoneRegionId = world.RegionId,
                };
                Configuration.Friends.Add(friend);
                changed = true;
            }

            var isNotFound = ((uint)entry.State & NotFoundStatusFlag) != 0;
            if (friend.Name != name ||
                friend.World != world.Name ||
                friend.DataCenter != world.DataCenter ||
                friend.HomeWorldId != world.Id ||
                friend.LodestoneRegionId != world.RegionId ||
                friend.ContentId != contentId ||
                !friend.IsGameFriend ||
                friend.VerificationStatus != (isNotFound
                    ? CharacterVerificationStatus.NotFound
                    : CharacterVerificationStatus.Found))
            {
                changed = true;
            }

            friend.Name = name;
            friend.World = world.Name;
            friend.DataCenter = world.DataCenter;
            friend.HomeWorldId = world.Id;
            friend.LodestoneRegionId = world.RegionId;
            friend.ContentId = contentId;
            friend.IsGameFriend = true;
            friend.VerificationStatus = isNotFound
                ? CharacterVerificationStatus.NotFound
                : CharacterVerificationStatus.Found;
            friend.LastCheckedUtc = now;
            friend.VerificationMessage = isNotFound
                ? "The game reports this character as not found."
                : string.Empty;
        }

        friendListWindow.SetSyncWarning(omitted);
        if (changed)
            Configuration.Save();
    }

    private void QueueStaleCharacterChecks()
    {
        var now = DateTime.UtcNow;
        var changed = false;
        foreach (var friend in Configuration.Friends)
        {
            if (friend.IsGameFriend || !ShouldCheckAgain(friend, now))
                continue;

            if (friend.HomeWorldId == 0 || friend.LodestoneRegionId == 0)
            {
                friend.VerificationStatus = CharacterVerificationStatus.LookupFailed;
                friend.LastCheckedUtc = now;
                friend.VerificationMessage = "The saved world is not recognized. Edit this record and select a current Home World.";
                changed = true;
                continue;
            }

            if (lodestoneVerifier.Enqueue(new LodestoneLookupRequest(
                    friend.Id,
                    friend.Name,
                    friend.World,
                    friend.LodestoneRegionId)))
            {
                friend.VerificationStatus = CharacterVerificationStatus.Checking;
                friend.VerificationMessage = string.Empty;
                changed = true;
            }
        }

        if (changed)
            Configuration.Save();
    }

    private void QueueManualCharacterCheck(FriendRecord friend)
    {
        if (friend.IsGameFriend)
            return;

        if (friend.HomeWorldId == 0 || friend.LodestoneRegionId == 0)
        {
            friend.VerificationStatus = CharacterVerificationStatus.LookupFailed;
            friend.VerificationMessage = "The saved world is not recognized. Add the record again using a current Home World.";
            friend.LastCheckedUtc = DateTime.UtcNow;
            Configuration.Save();
            return;
        }

        if (lodestoneVerifier.Enqueue(new LodestoneLookupRequest(
                friend.Id,
                friend.Name,
                friend.World,
                friend.LodestoneRegionId)))
        {
            friend.VerificationStatus = CharacterVerificationStatus.Checking;
            friend.VerificationMessage = string.Empty;
            Configuration.Save();
        }
    }

    private void ProcessCharacterCheckResults()
    {
        var changed = false;
        while (lodestoneVerifier.TryDequeueResult(out var result))
        {
            var friend = Configuration.Friends.FirstOrDefault(candidate => candidate.Id == result.FriendId);
            if (friend == null || friend.IsGameFriend)
                continue;

            friend.VerificationStatus = result.Status;
            friend.LastCheckedUtc = result.CompletedAtUtc;
            friend.VerificationMessage = result.Message;
            changed = true;
        }

        if (changed)
            Configuration.Save();
    }

    private unsafe void UpdateNearbyFriends()
    {
        nearbyFriendIds.Clear();
        var localPlayer = ObjectTable.LocalPlayer;
        if (localPlayer == null)
            return;

        var nearbyIdentities = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var nearbyContentIds = new HashSet<ulong>();
        var rangeSquared = Configuration.NearbyRangeYalms * Configuration.NearbyRangeYalms;
        foreach (var player in ObjectTable.PlayerObjects)
        {
            if (player.Address == localPlayer.Address)
                continue;

            var difference = player.Position - localPlayer.Position;
            if (!float.IsFinite(difference.X) ||
                !float.IsFinite(difference.Y) ||
                !float.IsFinite(difference.Z) ||
                difference.LengthSquared() > rangeSquared)
                continue;

            var character = (BattleChara*)player.Address;
            if (character == null)
                continue;

            var contentId = character->ContentId;
            if (contentId != 0)
                nearbyContentIds.Add(contentId);

            nearbyIdentities.Add(GetIdentityKey(player.Name.ToString(), checked((ushort)character->HomeWorld)));
        }

        foreach (var friend in Configuration.Friends)
        {
            if ((friend.ContentId != 0 && nearbyContentIds.Contains(friend.ContentId)) ||
                (friend.HomeWorldId != 0 &&
                 nearbyIdentities.Contains(GetIdentityKey(friend.Name, friend.HomeWorldId))))
            {
                nearbyFriendIds.Add(friend.Id);
            }
        }
    }

    private static bool ShouldCheckAgain(FriendRecord friend, DateTime now)
    {
        if (friend.VerificationStatus == CharacterVerificationStatus.Checking)
            return false;
        if (friend.LastCheckedUtc == null)
            return true;

        var age = now - friend.LastCheckedUtc.Value;
        var refreshInterval = friend.VerificationStatus switch
        {
            CharacterVerificationStatus.Found => TimeSpan.FromDays(30),
            CharacterVerificationStatus.NotFound => TimeSpan.FromDays(1),
            CharacterVerificationStatus.LookupFailed => TimeSpan.FromHours(6),
            _ => TimeSpan.Zero,
        };
        return age >= refreshInterval;
    }

    private static string GetIdentityKey(string name, ushort homeWorldId) =>
        $"{homeWorldId}:{name.Trim()}";
}
