# TooFriendly

![TooFriendly plugin icon](icon.png)

TooFriendly is a Dalamud plugin for keeping a local companion list of FFXIV friends, including notes about when and where you met them.

## Install in-game

1. In-game, enter `/xlsettings` and open the **Experimental** tab.
2. Under **Custom Plugin Repositories**, paste this URL into an empty row:
   `https://raw.githubusercontent.com/BubbaCodes/TooFriendly/main/repo.json`
3. Click **+**, enable the repository, then click **Save and Close**.
4. Enter `/xlplugins`, search for **TooFriendly**, and click **Install**.

TooFriendly keeps a local companion list with a configurable limit (1-1,000), notes, and meeting locations (data center, world, and city). Records are stored in the plugin configuration on this computer.

When the game's **Social → Friends** list is open, TooFriendly reads the native list and displays its companion panel beside it. Native friends are refreshed into local records, while locally stored-only friends use your chosen color. TooFriendly never writes to the game's server-backed friends list or bypasses its 200-friend limit.

Stored-only friends are checked against the official Lodestone when needed. Requests are made directly from your computer, one at a time with a delay, and only the character name and Home World are sent to Square Enix. TooFriendly has no companion server. A character not appearing in Lodestone search is marked for review, not automatically removed; private profiles or changes to Lodestone can prevent confirmation.

The nearby heart appears when a friend is present in your current game instance and within the configurable range. It does not appear for friends who are offline or in another instance.

The plugin builds against Dalamud SDK 15. Addon placement and live game behavior still need validation in an active FFXIV client.

In game, the `/toofriendly` command opens the plugin window.
