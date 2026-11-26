using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using Dalamud.Bindings.ImGui;
using Dalamud.Game.Command;
using Dalamud.Game.Gui.ContextMenu;
using Dalamud.Game.Gui.NamePlate;
using Dalamud.IoC;
using Dalamud.Interface.Windowing;
using Dalamud.Plugin;
using Dalamud.Plugin.Services;
using FFXIVClientStructs.FFXIV.Component.GUI;
using FFXIVClientStructs.FFXIV.Client.Game.Character;
using FFXIVClientStructs.FFXIV.Client.UI;
using FFXIVClientStructs.FFXIV.Client.UI.Info;
using Lumina.Excel.Sheets;
using TooFriendly.Windows;
using NativeBounds = FFXIVClientStructs.FFXIV.Common.Math.Bounds;

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
    internal static IClientState ClientState { get; private set; } = null!;

    [PluginService]
    internal static IDataManager DataManager { get; private set; } = null!;

    [PluginService]
    internal static IFramework Framework { get; private set; } = null!;

    [PluginService]
    internal static IGameGui GameGui { get; private set; } = null!;

    [PluginService]
    internal static IObjectTable ObjectTable { get; private set; } = null!;

    [PluginService]
    internal static INamePlateGui NamePlateGui { get; private set; } = null!;

    [PluginService]
    internal static IPluginLog PluginLog { get; private set; } = null!;

    private const string CommandName = "/toofriendly";
    private const uint NotFoundStatusFlag = 1u << 9;
    private const float FriendNameplateIconSize = 14f;
    private const float FriendNameplateIconGap = 2f;
    private readonly WindowSystem windowSystem = new("TooFriendly");
    private readonly FriendListWindow friendListWindow;
    private readonly WorldCatalog worldCatalog;
    private readonly LodestoneVerifier lodestoneVerifier;
    private readonly HashSet<Guid> nearbyFriendIds = new();
    private readonly Dictionary<ulong, Guid> friendIdsByContentId = new();
    private readonly Dictionary<string, Guid> friendIdsByIdentity = new(StringComparer.OrdinalIgnoreCase);
    private readonly List<Vector2> friendNameplateIconsToDraw = new();
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
            GetCurrentMeetingLocation,
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
        PluginInterface.UiBuilder.Draw += DrawFriendNameplateIcons;
        PluginInterface.UiBuilder.OpenMainUi += friendListWindow.Toggle;
        NamePlateGui.OnDataUpdate += OnNamePlateDataUpdate;
        NamePlateGui.OnPostDataUpdate += OnPostNamePlateDataUpdate;
    }

    public void Dispose()
    {
        Framework.Update -= OnFrameworkUpdate;
        ContextMenuService.OnMenuOpened -= OnMenuOpened;
        PluginInterface.UiBuilder.Draw -= windowSystem.Draw;
        PluginInterface.UiBuilder.Draw -= DrawFriendNameplateIcons;
        PluginInterface.UiBuilder.OpenMainUi -= friendListWindow.Toggle;
        NamePlateGui.OnDataUpdate -= OnNamePlateDataUpdate;
        NamePlateGui.OnPostDataUpdate -= OnPostNamePlateDataUpdate;
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

        var meetingLocation = GetCurrentMeetingLocation();
        Configuration.Friends.Add(new FriendRecord
        {
            Name = name,
            World = world.Name,
            DataCenter = world.DataCenter,
            MetDataCenter = meetingLocation.DataCenter,
            MetWorld = meetingLocation.World,
            City = meetingLocation.Place,
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
            friend.MetDataCenter ??= string.Empty;
            friend.MetWorld ??= string.Empty;
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
        friendIdsByContentId.Clear();
        friendIdsByIdentity.Clear();
        foreach (var friend in Configuration.Friends)
        {
            if (friend.ContentId != 0)
                friendIdsByContentId[friend.ContentId] = friend.Id;
            if (friend.HomeWorldId != 0)
                friendIdsByIdentity[GetIdentityKey(friend.Name, friend.HomeWorldId)] = friend.Id;
        }

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

    private MeetingLocation GetCurrentMeetingLocation()
    {
        var localPlayer = ObjectTable.LocalPlayer;
        if (localPlayer == null)
            return new MeetingLocation(string.Empty, string.Empty, string.Empty);

        var currentWorld = localPlayer.CurrentWorld;
        var currentWorldId = checked((ushort)currentWorld.RowId);
        var dataCenter = string.Empty;
        var world = currentWorld.Value.Name.ToString().Trim();
        if (worldCatalog.TryGetById(currentWorldId, out var metadata))
        {
            dataCenter = metadata.DataCenter;
            world = metadata.Name;
        }

        var place = string.Empty;
        var territoryType = ClientState.TerritoryType;
        if (territoryType != 0)
        {
            var territory = DataManager.GetExcelSheet<TerritoryType>().GetRow(territoryType);
            place = territory.PlaceName.Value.Name.ToString().Trim();
        }

        return new MeetingLocation(dataCenter, world, place);
    }

    private unsafe void OnPostNamePlateDataUpdate(
        INamePlateUpdateContext _,
        IReadOnlyList<INamePlateUpdateHandler> handlers)
    {
        friendNameplateIconsToDraw.Clear();
        foreach (var handler in handlers)
        {
            var player = handler.PlayerCharacter;
            if (player == null)
                continue;

            var character = (BattleChara*)player.Address;
            if (character == null)
                continue;

            var contentId = character->ContentId;
            Guid friendId;
            if (contentId == 0 || !friendIdsByContentId.TryGetValue(contentId, out friendId))
            {
                var identity = GetIdentityKey(
                    player.Name.ToString(),
                    checked((ushort)character->HomeWorld));
                if (!friendIdsByIdentity.TryGetValue(identity, out friendId))
                    continue;
            }

            if (!nearbyFriendIds.Contains(friendId))
                continue;

            var plate = (AddonNamePlate.NamePlateObject*)handler.NamePlateObjectAddress;
            if (plate == null || plate->RootComponentNode == null || !plate->IsVisible ||
                !plate->IsPlayerCharacter || plate->NameText == null ||
                !plate->NameText->IsVisible())
                continue;

            var nameNode = &plate->NameText->AtkResNode;
            var scale = GetAccumulatedScale(nameNode);
            ushort textWidth = 0;
            ushort textHeight = 0;
            plate->NameText->GetTextDrawSize(&textWidth, &textHeight, null, 0, -1, false);

            NativeBounds bounds;
            nameNode->GetBounds(&bounds);
            var left = MathF.Min(bounds.Pos1.X, bounds.Pos2.X);
            var top = MathF.Min(bounds.Pos1.Y, bounds.Pos2.Y);
            var right = MathF.Max(bounds.Pos1.X, bounds.Pos2.X);
            var bottom = MathF.Max(bounds.Pos1.Y, bounds.Pos2.Y);
            var boxWidth = right - left;
            var drawnWidth = MathF.Min(textWidth * scale, boxWidth);
            if (drawnWidth <= 0 || bottom - top <= 0)
                continue;

            var textLeft = left + (boxWidth - drawnWidth) / 2f;
            friendNameplateIconsToDraw.Add(new Vector2(
                textLeft + (drawnWidth - FriendNameplateIconSize) / 2f,
                top - FriendNameplateIconGap - FriendNameplateIconSize));
        }
    }

    private static unsafe float GetAccumulatedScale(AtkResNode* node)
    {
        var scale = 1f;
        for (var current = node; current != null; current = current->ParentNode)
            scale *= current->ScaleX > 0f ? current->ScaleX : 1f;
        return scale;
    }

    private static void OnNamePlateDataUpdate(
        INamePlateUpdateContext _,
        IReadOnlyList<INamePlateUpdateHandler> __)
    {
    }

    private void DrawFriendNameplateIcons()
    {
        var drawList = ImGui.GetForegroundDrawList();
        var color = ImGui.ColorConvertFloat4ToU32(new Vector4(0.35f, 1f, 0.45f, 1f));
        foreach (var position in friendNameplateIconsToDraw)
            drawList.AddText(ImGui.GetFont(), FriendNameplateIconSize, position, color, "♥");
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
