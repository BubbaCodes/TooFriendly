using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface.Windowing;

namespace TooFriendly.Windows;

public sealed class FriendListWindow : Window
{
    private readonly Configuration configuration;
    private readonly WorldCatalog worldCatalog;
    private readonly Func<MeetingLocation> getMeetingLocation;
    private readonly Action<FriendRecord> queueCharacterCheck;
    private readonly HashSet<Guid> nearbyFriendIds;
    private string name = string.Empty;
    private string region = string.Empty;
    private string dataCenter = string.Empty;
    private string world = string.Empty;
    private string city = string.Empty;
    private string metNote = string.Empty;
    private string validationMessage = string.Empty;
    private string syncWarning = string.Empty;
    private FriendRecord? selectedFriend;
    private FriendRecord? editingFriend;
    private bool wasSocialVisible;
    private bool openedBySocial;

    public FriendListWindow(
        Configuration configuration,
        WorldCatalog worldCatalog,
        Func<MeetingLocation> getMeetingLocation,
        Action<FriendRecord> queueCharacterCheck,
        HashSet<Guid> nearbyFriendIds)
        : base("TooFriendly##Friend List")
    {
        this.configuration = configuration;
        this.worldCatalog = worldCatalog;
        this.getMeetingLocation = getMeetingLocation;
        this.queueCharacterCheck = queueCharacterCheck;
        this.nearbyFriendIds = nearbyFriendIds;
        Size = new Vector2(560, 480);
        SizeCondition = ImGuiCond.FirstUseEver;
    }

    public void UpdateSocialDock(bool visible, Vector2? position)
    {
        if (visible && position.HasValue)
        {
            Position = position.Value;
            PositionCondition = ImGuiCond.Always;
            if (!wasSocialVisible)
            {
                openedBySocial = !IsOpen;
                IsOpen = true;
            }
        }
        else if (wasSocialVisible && openedBySocial)
        {
            IsOpen = false;
            openedBySocial = false;
        }

        wasSocialVisible = visible;
    }

    public void SetSyncWarning(int omitted) =>
        syncWarning = omitted == 0
            ? string.Empty
            : $"{omitted} game friend(s) were not added because the configured limit is full.";

    public override void Draw()
    {
        var friendLimit = configuration.FriendLimit;
        if (ImGui.InputInt("Local friend limit", ref friendLimit))
        {
            configuration.FriendLimit = Math.Clamp(friendLimit, 1, 1000);
            configuration.Save();
        }

        var nearbyRange = configuration.NearbyRangeYalms;
        if (ImGui.InputFloat("Nearby range (yalms)", ref nearbyRange, 5f, 10f, "%.0f"))
        {
            configuration.NearbyRangeYalms = float.IsFinite(nearbyRange)
                ? Math.Clamp(nearbyRange, 5f, 200f)
                : 100f;
            configuration.Save();
        }

        var showNameplateHearts = configuration.ShowNameplateHearts;
        if (ImGui.Checkbox("Show nearby hearts on nameplates", ref showNameplateHearts))
        {
            configuration.ShowNameplateHearts = showNameplateHearts;
            configuration.Save();
        }

        var color = ImGui.ColorConvertU32ToFloat4(configuration.StoredOnlyFriendColor);
        if (ImGui.ColorEdit4("Stored-only friend color", ref color))
        {
            configuration.StoredOnlyFriendColor = ImGui.ColorConvertFloat4ToU32(color);
            configuration.Save();
        }

        ImGui.Separator();
        var gameFriends = 0;
        foreach (var friend in configuration.Friends)
        {
            if (friend.IsGameFriend)
                gameFriends++;
        }

        ImGui.Text($"Friends: {configuration.Friends.Count} / {configuration.FriendLimit} ({gameFriends} in-game)");
        if (!string.IsNullOrEmpty(syncWarning))
            ImGui.TextColored(new Vector4(1f, 0.65f, 0.2f, 1f), syncWarning);
        ImGui.TextDisabled("Nearby markers only appear for players visible in your current instance.");

        if (ImGui.BeginChild("FriendRecords", new Vector2(0, 230), true))
        {
            var friendRemoved = false;
            if (ImGui.BeginTable(
                    "FriendRecordsTable",
                    5,
                    ImGuiTableFlags.SizingFixedFit | ImGuiTableFlags.NoSavedSettings))
            {
                ImGui.TableSetupColumn("Character", ImGuiTableColumnFlags.WidthStretch);
                ImGui.TableSetupColumn("Status", ImGuiTableColumnFlags.WidthFixed, 145f);
                ImGui.TableSetupColumn("Check", ImGuiTableColumnFlags.WidthFixed, 48f);
                ImGui.TableSetupColumn("Edit", ImGuiTableColumnFlags.WidthFixed, 40f);
                ImGui.TableSetupColumn("Remove", ImGuiTableColumnFlags.WidthFixed, 58f);

                var clipper = new ImGuiListClipper();
                clipper.Begin(configuration.Friends.Count);
                while (clipper.Step())
                {
                    for (var i = clipper.DisplayStart; i < clipper.DisplayEnd; i++)
                    {
                        var friend = configuration.Friends[i];
                        ImGui.PushID(friend.Id.ToString());
                        ImGui.TableNextRow();

                        ImGui.TableSetColumnIndex(0);
                        var selected = ReferenceEquals(selectedFriend, friend);
                        if (!friend.IsGameFriend)
                        {
                            ImGui.PushStyleColor(
                                ImGuiCol.Text,
                                ImGui.ColorConvertU32ToFloat4(configuration.StoredOnlyFriendColor));
                        }

                        if (ImGui.Selectable($"{friend.Name} - Home: {friend.World}##name", selected))
                            selectedFriend = friend;

                        if (!friend.IsGameFriend)
                            ImGui.PopStyleColor();

                        ImGui.TableSetColumnIndex(1);
                        if (nearbyFriendIds.Contains(friend.Id))
                        {
                            ImGui.TextColored(new Vector4(0.35f, 1f, 0.45f, 1f), "♥");
                            if (ImGui.IsItemHovered())
                                ImGui.SetTooltip("Nearby in the same instance.");
                        }

                        if (friend.VerificationStatus == CharacterVerificationStatus.NotFound)
                        {
                            if (nearbyFriendIds.Contains(friend.Id))
                                ImGui.SameLine();
                            ImGui.TextColored(new Vector4(1f, 0.85f, 0.15f, 1f), "Not found");
                            if (ImGui.IsItemHovered())
                                ImGui.SetTooltip(friend.VerificationMessage);
                        }
                        else if (friend.VerificationStatus == CharacterVerificationStatus.Checking)
                        {
                            if (nearbyFriendIds.Contains(friend.Id))
                                ImGui.SameLine();
                            ImGui.TextDisabled("Checking...");
                        }
                        else if (!friend.IsGameFriend &&
                                 friend.VerificationStatus == CharacterVerificationStatus.Found)
                        {
                            if (nearbyFriendIds.Contains(friend.Id))
                                ImGui.SameLine();
                            ImGui.TextColored(new Vector4(0.35f, 1f, 0.45f, 1f), "Found");
                        }

                        ImGui.TableSetColumnIndex(2);
                        if (!friend.IsGameFriend &&
                            friend.VerificationStatus != CharacterVerificationStatus.Checking)
                        {
                            if (ImGui.SmallButton("Check"))
                                queueCharacterCheck(friend);
                            if (ImGui.IsItemHovered())
                                ImGui.SetTooltip("Rechecks this character on the official Lodestone.");
                        }

                        ImGui.TableSetColumnIndex(3);
                        if (ImGui.SmallButton("Edit"))
                            BeginEditing(friend);
                        if (ImGui.IsItemHovered())
                            ImGui.SetTooltip("Edit this saved record.");

                        ImGui.TableSetColumnIndex(4);
                        if (ImGui.SmallButton("Remove"))
                        {
                            configuration.Friends.RemoveAt(i);
                            if (ReferenceEquals(selectedFriend, friend))
                                selectedFriend = null;
                            if (ReferenceEquals(editingFriend, friend))
                                CancelEditing();
                            configuration.Save();
                            friendRemoved = true;
                            ImGui.PopID();
                            break;
                        }
                        if (ImGui.IsItemHovered())
                        {
                            ImGui.SetTooltip(
                                "Removes this record from TooFriendly only; it does not change your in-game friends list.");
                        }

                        ImGui.PopID();
                    }

                    if (friendRemoved)
                        break;
                }

                clipper.End();
                ImGui.EndTable();
            }
        }
        ImGui.EndChild();

        ImGui.TextDisabled(
            "Check rechecks a character on Lodestone. Remove deletes only the local record, not your in-game friend.");

        if (selectedFriend != null)
        {
            ImGui.TextDisabled($"Home World: {selectedFriend.World} ({selectedFriend.DataCenter})");
            var meetingDetails = string.Join(
                " / ",
                new[] { selectedFriend.MetDataCenter, selectedFriend.MetWorld, selectedFriend.City }
                    .Where(value => !string.IsNullOrWhiteSpace(value)));
            if (!string.IsNullOrWhiteSpace(meetingDetails))
                ImGui.TextDisabled($"Met: {meetingDetails}");
            else if (!string.IsNullOrWhiteSpace(selectedFriend.DataCenter))
                ImGui.TextDisabled($"Home data center: {selectedFriend.DataCenter}");

            if (!string.IsNullOrWhiteSpace(selectedFriend.MetNote))
                ImGui.TextWrapped(selectedFriend.MetNote);

            if (selectedFriend.VerificationStatus == CharacterVerificationStatus.LookupFailed &&
                !string.IsNullOrWhiteSpace(selectedFriend.VerificationMessage))
            {
                ImGui.TextColored(
                    new Vector4(1f, 0.65f, 0.2f, 1f),
                    selectedFriend.VerificationMessage);
            }
        }
        else
        {
            ImGui.TextDisabled("Select a friend to see saved notes and meeting details.");
        }

        ImGui.Separator();
        var isEditing = editingFriend != null;
        var isEditingGameFriend = editingFriend?.IsGameFriend == true;
        ImGui.Text(isEditing ? "Edit record" : "Add a local record");
        if (isEditingGameFriend)
        {
            ImGui.Text($"Character name: {name}");
            ImGui.Text($"Home World: {world}");
            ImGui.TextDisabled("In-game friend names and Home Worlds are synchronized from FFXIV.");
        }
        else
        {
            ImGui.InputText("Character name", ref name, 64);

            var regions = worldCatalog.DataCenters
                .Select(metadata => metadata.Region)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToArray();
            if (DrawCombo("Region", "Select a region", region, regions, out var selectedRegion))
            {
                region = selectedRegion;
                dataCenter = string.Empty;
                world = string.Empty;
                validationMessage = string.Empty;
            }

            var dataCenters = worldCatalog.DataCenters
                .Where(metadata => string.Equals(
                    metadata.Region,
                    region,
                    StringComparison.OrdinalIgnoreCase))
                .Select(metadata => metadata.Name)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToArray();
            ImGui.BeginDisabled(string.IsNullOrWhiteSpace(region));
            if (DrawCombo(
                    "Data Center",
                    "Select a data center",
                    dataCenter,
                    dataCenters,
                    out var selectedDataCenter))
            {
                dataCenter = selectedDataCenter;
                world = string.Empty;
                validationMessage = string.Empty;
            }
            ImGui.EndDisabled();

            var selectedDataCenterMetadata = worldCatalog.DataCenters.FirstOrDefault(metadata =>
                string.Equals(metadata.Region, region, StringComparison.OrdinalIgnoreCase) &&
                string.Equals(metadata.Name, dataCenter, StringComparison.OrdinalIgnoreCase));
            var worlds = selectedDataCenterMetadata == null
                ? Array.Empty<string>()
                : worldCatalog.Worlds
                    .Where(metadata => metadata.DataCenterId == selectedDataCenterMetadata.Id)
                    .Select(metadata => metadata.Name)
                    .ToArray();
            ImGui.BeginDisabled(worlds.Length == 0);
            if (DrawCombo(
                    "World (Server)",
                    worlds.Length == 0 ? "No worlds available" : "Select a world",
                    world,
                    worlds,
                    out var selectedWorld))
            {
                world = selectedWorld;
                validationMessage = string.Empty;
            }
            ImGui.EndDisabled();
        }

        if (!isEditing)
        {
            var currentMeetingLocation = getMeetingLocation();
            var currentMeetingDetails = string.Join(
                " / ",
                new[] { currentMeetingLocation.DataCenter, currentMeetingLocation.World, currentMeetingLocation.Place }
                    .Where(value => !string.IsNullOrWhiteSpace(value)));
            ImGui.TextDisabled(
                string.IsNullOrWhiteSpace(currentMeetingDetails)
                    ? "Meeting location: unavailable"
                    : $"Meeting location: {currentMeetingDetails}");
        }

        ImGui.InputText("Location (optional override)", ref city, 64);
        ImGui.InputText("When/how you met", ref metNote, 256);

        if (!string.IsNullOrEmpty(validationMessage))
            ImGui.TextColored(new Vector4(1f, 0.65f, 0.2f, 1f), validationMessage);

        if (ImGui.Button(isEditing ? "Save changes" : "Add friend"))
            SaveRecord();

        if (isEditing)
        {
            ImGui.SameLine();
            if (ImGui.Button("Cancel edit"))
                CancelEditing();
        }

        ImGui.Separator();
        ImGui.TextWrapped("Character checks query the official Lodestone directly from this computer. TooFriendly does not send records to its own server.");
    }

    private void BeginEditing(FriendRecord friend)
    {
        editingFriend = friend;
        selectedFriend = friend;
        name = friend.Name;
        if (worldCatalog.TryGetByName(friend.World, out var metadata))
        {
            region = metadata.Region;
            dataCenter = metadata.DataCenter;
            world = metadata.Name;
        }
        else
        {
            region = string.Empty;
            dataCenter = string.Empty;
            world = string.Empty;
        }

        city = friend.City;
        metNote = friend.MetNote;
        validationMessage = string.Empty;
    }

    private void CancelEditing()
    {
        editingFriend = null;
        name = string.Empty;
        region = string.Empty;
        dataCenter = string.Empty;
        world = string.Empty;
        city = string.Empty;
        metNote = string.Empty;
        validationMessage = string.Empty;
    }

    private void SaveRecord()
    {
        var currentEdit = editingFriend;
        var canEditIdentity = currentEdit?.IsGameFriend != true;
        var trimmedName = name.Trim();
        var trimmedWorld = world.Trim();
        if ((canEditIdentity && trimmedName.Length == 0) || trimmedWorld.Length == 0)
        {
            validationMessage = "Character name and Home World are required.";
            return;
        }

        var selectedDataCenter = worldCatalog.DataCenters.FirstOrDefault(dataCenterMetadata =>
            string.Equals(dataCenterMetadata.Region, region, StringComparison.OrdinalIgnoreCase) &&
            string.Equals(dataCenterMetadata.Name, dataCenter, StringComparison.OrdinalIgnoreCase));
        if (selectedDataCenter == null ||
            !worldCatalog.TryGetByName(trimmedWorld, out var metadata) ||
            metadata.DataCenterId != selectedDataCenter.Id)
        {
            validationMessage = "Select a Region, Data Center, and World.";
            return;
        }

        if (currentEdit == null && configuration.Friends.Count >= configuration.FriendLimit)
        {
            validationMessage = "The local friend limit has been reached.";
            return;
        }

        foreach (var friend in configuration.Friends)
        {
            if (ReferenceEquals(friend, currentEdit))
                continue;

            var sameContentId = currentEdit?.ContentId is > 0 &&
                                friend.ContentId == currentEdit.ContentId;
            var sameHomeIdentity = canEditIdentity &&
                                   string.Equals(friend.Name, trimmedName, StringComparison.OrdinalIgnoreCase) &&
                                   (friend.HomeWorldId == metadata.Id ||
                                    string.Equals(friend.World, metadata.Name, StringComparison.OrdinalIgnoreCase));
            if (sameContentId || sameHomeIdentity)
            {
                validationMessage = "That character already has a record in TooFriendly.";
                return;
            }
        }

        if (currentEdit != null)
        {
            var identityChanged = canEditIdentity &&
                                  (!string.Equals(
                                       currentEdit.Name,
                                       trimmedName,
                                       StringComparison.OrdinalIgnoreCase) ||
                                   currentEdit.HomeWorldId != metadata.Id);
            if (canEditIdentity)
            {
                currentEdit.Name = trimmedName;
                currentEdit.World = metadata.Name;
                currentEdit.DataCenter = metadata.DataCenter;
                currentEdit.HomeWorldId = metadata.Id;
                currentEdit.LodestoneRegionId = metadata.RegionId;
            }

            if (identityChanged)
            {
                currentEdit.ContentId = 0;
                currentEdit.VerificationStatus = CharacterVerificationStatus.NotChecked;
                currentEdit.LastCheckedUtc = null;
                currentEdit.VerificationMessage = string.Empty;
            }

            currentEdit.City = city.Trim();
            currentEdit.MetNote = metNote.Trim();
            selectedFriend = currentEdit;
            CancelEditing();
        }
        else
        {
            var meetingLocation = getMeetingLocation();
            configuration.Friends.Add(new FriendRecord
            {
                Name = trimmedName,
                World = metadata.Name,
                DataCenter = metadata.DataCenter,
                MetDataCenter = meetingLocation.DataCenter,
                MetWorld = meetingLocation.World,
                City = string.IsNullOrWhiteSpace(city) ? meetingLocation.Place : city.Trim(),
                MetNote = metNote.Trim(),
                HomeWorldId = metadata.Id,
                LodestoneRegionId = metadata.RegionId,
            });

            CancelEditing();
        }

        configuration.Save();
    }

    private static bool DrawCombo(
        string label,
        string placeholder,
        string selected,
        IReadOnlyList<string> options,
        out string selection)
    {
        selection = selected;
        var preview = options.FirstOrDefault(option =>
            string.Equals(option, selected, StringComparison.OrdinalIgnoreCase)) ?? placeholder;
        if (!ImGui.BeginCombo(label, preview))
            return false;

        var changed = false;
        for (var i = 0; i < options.Count; i++)
        {
            var option = options[i];
            var isSelected = string.Equals(option, selected, StringComparison.OrdinalIgnoreCase);
            if (ImGui.Selectable($"{option}##{label}-{i}", isSelected) && !isSelected)
            {
                selection = option;
                changed = true;
            }

            if (isSelected)
                ImGui.SetItemDefaultFocus();
        }

        ImGui.EndCombo();
        return changed;
    }
}
