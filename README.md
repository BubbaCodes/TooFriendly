# TooFriendly

TooFriendly is a Dalamud plugin concept for keeping a local companion list of FFXIV friends, including notes about when and where you met them.

## Initial foundation

The initial project shell provides a local friend list with a configurable limit (1–1,000), notes, and meeting locations (data center, world, and city). Records are stored in the plugin configuration on this computer.

The following game integrations are not implemented yet:

- Refreshing records from the game's friends list when its Social tab is opened.
- Checking whether a character still exists and warning about unfindable characters.
- Distinguishing the game's actual friends from locally stored-only entries and coloring the latter.
- Showing a nearby-friend icon only when the friend is in range and in the same instance.
- Rendering the companion list inside the game's Social tab.

These features need to be implemented against supported Dalamud/game APIs and validated in-game. The local companion list will not add characters to the game's friends list or bypass its limit.

## Development

Build with the Dalamud SDK installed:

```powershell
dotnet build TooFriendly.slnx
```

In game, the `/toofriendly` command opens the plugin window.
