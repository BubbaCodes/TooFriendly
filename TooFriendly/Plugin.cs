using System;
using Dalamud.Game.Command;
using Dalamud.IoC;
using Dalamud.Interface.Windowing;
using Dalamud.Plugin;
using Dalamud.Plugin.Services;
using TooFriendly.Windows;

namespace TooFriendly;

public sealed class Plugin : IDalamudPlugin
{
    [PluginService]
    internal static IDalamudPluginInterface PluginInterface { get; private set; } = null!;

    [PluginService]
    internal static ICommandManager CommandManager { get; private set; } = null!;

    private const string CommandName = "/toofriendly";
    private readonly WindowSystem windowSystem = new("TooFriendly");
    private readonly FriendListWindow friendListWindow;

    public Configuration Configuration { get; }

    public Plugin()
    {
        Configuration = PluginInterface.GetPluginConfig() as Configuration ?? new Configuration();
        Configuration.FriendLimit = Math.Clamp(Configuration.FriendLimit, 1, 1000);
        Configuration.Friends ??= new();

        friendListWindow = new FriendListWindow(Configuration);
        windowSystem.AddWindow(friendListWindow);

        CommandManager.AddHandler(CommandName, new CommandInfo((_, _) => friendListWindow.Toggle())
        {
            HelpMessage = "Open the TooFriendly local friends list.",
        });

        PluginInterface.UiBuilder.Draw += windowSystem.Draw;
        PluginInterface.UiBuilder.OpenMainUi += friendListWindow.Toggle;
    }

    public void Dispose()
    {
        PluginInterface.UiBuilder.Draw -= windowSystem.Draw;
        PluginInterface.UiBuilder.OpenMainUi -= friendListWindow.Toggle;
        CommandManager.RemoveHandler(CommandName);
        windowSystem.RemoveAllWindows();
    }
}
