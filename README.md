# TooFriendly

TooFriendly is a Dalamud plugin for keeping a local companion list of FFXIV friends, including notes about when and where you met them.

## Install in-game

1. In-game, enter `/xlsettings` and open the **Experimental** tab.
2. Under **Custom Plugin Repositories**, paste this URL into an empty row:
   `https://raw.githubusercontent.com/BubbaCodes/TooFriendly/main/repo.json`
3. Click **+**, enable the repository, then click **Save and Close**.
4. Enter `/xlplugins`, search for **TooFriendly**, and click **Install**.

The initial release provides a local friend list with a configurable limit (1-1,000), notes, and meeting locations (data center, world, and city). Records are stored in the plugin configuration on this computer.

The following game integrations are not implemented yet:

- Refreshing records from the game's friends list when its Social tab is opened.
- Checking whether a character still exists and warning about unfindable characters.
- Distinguishing the game's actual friends from locally stored-only entries and coloring the latter.
- Showing a nearby-friend icon only when the friend is in range and in the same instance.
- Rendering the companion list inside the game's Social tab.

These features need to be implemented against supported Dalamud/game APIs and validated in-game. The local companion list will not add characters to the game's friends list or bypass its limit.

## Build and release

Build locally with the Dalamud SDK installed:

```powershell
dotnet build TooFriendly.slnx
```

Push a version tag matching `TooFriendly.csproj` (for example, `v0.1.0`) to build the plugin package and publish a GitHub release. The repository manifest installs the latest release asset.

In game, the `/toofriendly` command opens the plugin window.
