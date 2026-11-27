# TooFriendly

![TooFriendly plugin icon](icon.png)

TooFriendly is a Dalamud plugin for keeping a local companion list of FFXIV friends, including notes about when and where you met them.

## Install in-game

1. In-game, enter `/xlsettings` and open the **Experimental** tab.
2. Under **Custom Plugin Repositories**, paste this URL into an empty row:
   `https://raw.githubusercontent.com/BubbaCodes/TooFriendly/main/repo.json`
3. Click **+**, enable the repository, then click **Save and Close**.
4. Enter `/xlplugins`, search for **TooFriendly**, and click **Install**.

TooFriendly keeps a local companion list with a configurable limit (1-1,000), notes, and meeting locations. Choose a physical region, data center, and Home World/server from separate cascading selectors populated from the installed game data, rather than a hardcoded server list. Existing records refresh their world metadata from that catalog when the plugin starts.

When the game's **Social → Friends** list is open, TooFriendly reads the native list and displays its companion panel beside it. Native friends are refreshed into local records, while locally stored-only friends use your chosen color. TooFriendly never writes to the game's server-backed friends list or bypasses its 200-friend limit.

Use a player character's context menu and select **Add to TooFriendly** to save their name and Home World locally. The option is disabled if they are already saved or the configured list is full. A character's Home World remains their identity while they visit another World or data center; TooFriendly uses the Home World for matching and Lodestone checks, and records the place you met separately. Record details show when the friend was added in your local time; older records without a saved date show that the date is unavailable.

Use **Check** to request a fresh check of a stored-only record against the official Lodestone. Use **Edit** to update a record's saved details; a native in-game friend's name and Home World stay synchronized with FFXIV. **Remove** deletes only the local TooFriendly record and does not change the game's Friends list. Checks run directly from your computer, one at a time with a delay, and only the character name and Home World are sent to Square Enix. TooFriendly has no companion server. A character not appearing in Lodestone search is marked for review, not automatically removed; private profiles or changes to Lodestone can prevent confirmation.

The official [Data Center Travel guide](https://na.finalfantasyxiv.com/lodestone/playguide/contentsguide/datacentertravel/) currently describes Oceania travel. Square Enix announced temporary cross-region travel for the Patch 8.0 congestion period beginning December 1, 2026: Japan to Oceania, North America to Europe/Oceania, and Europe to North America/Oceania. Permanent NA/EU travel remains under evaluation. Because travel does not change a character's Home World, TooFriendly matches and verifies against the Home World region, not the region being visited. See the [official announcement](https://na.finalfantasyxiv.com/lodestone/topics/detail/3e0f3cebb70090699b4e20503d5494d8e33e7df5) and [current World Status](https://na.finalfantasyxiv.com/lodestone/worldstatus/).

The nearby heart appears above a visible nameplate when a friend is present in your current game instance and within the configurable range. Toggle **Show nearby hearts on nameplates** in the friend-list window to hide or show these markers. Hearts do not appear for friends who are offline or in another instance.

The plugin builds against Dalamud SDK 15. Addon placement and live game behavior still need validation in an active FFXIV client.

In game, the `/toofriendly` command opens the plugin window.
