using System;
using System.Numerics;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface.Windowing;

namespace TooFriendly.Windows;

public sealed class FriendListWindow : Window
{
    private readonly Configuration configuration;
    private string name = string.Empty;
    private string world = string.Empty;
    private string dataCenter = string.Empty;
    private string city = string.Empty;
    private string metNote = string.Empty;
    private string validationMessage = string.Empty;

    public FriendListWindow(Configuration configuration)
        : base("TooFriendly##Friend List")
    {
        this.configuration = configuration;
        Size = new Vector2(560, 480);
        SizeCondition = ImGuiCond.FirstUseEver;
    }

    public override void Draw()
    {
        var friendLimit = configuration.FriendLimit;
        if (ImGui.InputInt("Local friend limit", ref friendLimit))
        {
            configuration.FriendLimit = Math.Clamp(friendLimit, 1, 1000);
            configuration.Save();
        }

        var color = ImGui.ColorConvertU32ToFloat4(configuration.StoredOnlyFriendColor);
        if (ImGui.ColorEdit4("Stored-only friend color", ref color))
        {
            configuration.StoredOnlyFriendColor = ImGui.ColorConvertFloat4ToU32(color);
            configuration.Save();
        }

        ImGui.Separator();
        ImGui.Text($"Local friends: {configuration.Friends.Count} / {configuration.FriendLimit}");

        if (ImGui.BeginChild("FriendRecords", new Vector2(0, 230), true))
        {
            for (var i = 0; i < configuration.Friends.Count; i++)
            {
                var friend = configuration.Friends[i];
                ImGui.PushID(i);
                ImGui.TextColored(
                    ImGui.ColorConvertU32ToFloat4(configuration.StoredOnlyFriendColor),
                    $"{friend.Name} @ {friend.World}");

                if (!string.IsNullOrWhiteSpace(friend.DataCenter) ||
                    !string.IsNullOrWhiteSpace(friend.City))
                    ImGui.TextDisabled($"{friend.DataCenter} - {friend.City}");

                if (!string.IsNullOrWhiteSpace(friend.MetNote))
                    ImGui.TextWrapped(friend.MetNote);

                ImGui.SameLine();
                if (ImGui.SmallButton("Remove"))
                {
                    configuration.Friends.RemoveAt(i);
                    configuration.Save();
                    ImGui.PopID();
                    break;
                }

                ImGui.Separator();
                ImGui.PopID();
            }
        }
        ImGui.EndChild();

        ImGui.Separator();
        ImGui.Text("Add a local record");
        ImGui.InputText("Character name", ref name, 64);
        ImGui.InputText("World", ref world, 64);
        ImGui.InputText("Data center", ref dataCenter, 64);
        ImGui.InputText("City", ref city, 64);
        ImGui.InputText("When/how you met", ref metNote, 256);

        if (!string.IsNullOrEmpty(validationMessage))
            ImGui.TextColored(new Vector4(1f, 0.65f, 0.2f, 1f), validationMessage);

        if (ImGui.Button("Add friend"))
            AddFriend();
    }

    private void AddFriend()
    {
        var trimmedName = name.Trim();
        var trimmedWorld = world.Trim();
        if (trimmedName.Length == 0 || trimmedWorld.Length == 0)
        {
            validationMessage = "Character name and world are required.";
            return;
        }

        if (configuration.Friends.Count >= configuration.FriendLimit)
        {
            validationMessage = "The local friend limit has been reached.";
            return;
        }

        foreach (var friend in configuration.Friends)
        {
            if (string.Equals(friend.Name, trimmedName, StringComparison.OrdinalIgnoreCase) &&
                string.Equals(friend.World, trimmedWorld, StringComparison.OrdinalIgnoreCase))
            {
                validationMessage = "That character is already in your local list.";
                return;
            }
        }

        configuration.Friends.Add(new FriendRecord
        {
            Name = trimmedName,
            World = trimmedWorld,
            DataCenter = dataCenter.Trim(),
            City = city.Trim(),
            MetNote = metNote.Trim(),
        });
        configuration.Save();

        name = string.Empty;
        world = string.Empty;
        dataCenter = string.Empty;
        city = string.Empty;
        metNote = string.Empty;
        validationMessage = string.Empty;
    }
}
