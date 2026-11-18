# LootPose Companion

A small Windows app that counts the chests you open in Fortnite Battle Royale and adds them to your
[LootPose](https://lootpose.vercel.app) account a few seconds after each match. Unofficial.

LootPose Companion is not affiliated with, endorsed by or connected to Epic Games. Fortnite is a trademark of Epic
Games. It is also not affiliated with LED-N's
[Fortnite-ChestCounter](https://github.com/LED-N/Fortnite-ChestCounter) or with Shiqan's
[FortniteReplayDecompressor](https://github.com/Shiqan/FortniteReplayDecompressor), whose code it uses (see Credits).

## Getting it

Download `LootPoseCompanion.exe` from the
[latest release](https://github.com/tomiwaaluko/lootpose-companion/releases/latest). There is no installer. The file is
not code-signed, so Windows shows "Windows protected your PC" the first time: choose **More info**, then **Run anyway**.

1. The window shows a six-character code and **Link this PC**. Press it.
2. LootPose opens in your browser and shows the same code. Sign in if asked, check the code, and press **Link**.
3. Within a few seconds the companion says "Linked".
4. In Fortnite: Settings, Game, Replays, Record Replays: On.

From then on, each Battle Royale match you play is counted a few seconds after it ends. The app sits in the
notification area. Closing the window keeps it running; **Quit** is in the tray icon's menu.

## What it counts

The same chests as LED-N's Fortnite-ChestCounter: every chest you open or destroy in regular Battle Royale (build or
zero build) on the current season's map. Not ranked, OG, Reload, Blitz, limited-time modes, Creative, LEGO, Festival
or private matches. Not ammo boxes, food boxes or loot cannons. Not your squad mates' chests.

Only matches that start after you linked the PC are counted, each match once, and nothing after the treasure hunt
ends. Matches on a linked PC are counted automatically: don't also tap those chests in LootPose.

## What it reads and what it sends

- It reads only the replay files Fortnite saves in `%LOCALAPPDATA%\FortniteGame\Saved\Demos`, and opens them
  read-only. It checks whether a program named `FortniteClient-Win64-Shipping.exe` is running, to show its status, and
  never opens it. It never reads the game's memory or network traffic, never injects into the game and never changes
  a game file.
- It sends only this PC's random device id, its secret (to LootPose's database, over HTTPS) and chest counts per week
  and Treasure Hour. Never a replay file, a file path, a player name, a match id or anything about other players.
- It downloads LootPose's public schedule (`https://lootpose.vercel.app/data/treasures.json`) and checks this
  repository's latest release once a day. It never downloads or replaces itself.
- It never signs in to anything. No password or sign-in token is ever in it.

Its own files are in `%APPDATA%\LootPose Companion`: `ledger.json` (the matches it handled, this PC's id, its secret
encrypted for your Windows user, and any count waiting to be sent) and `treasures.json` (the last copy of the
schedule).

## Checking one replay

```
LootPoseCompanion.exe --analyze "%LOCALAPPDATA%\FortniteGame\Saved\Demos\<file>.replay" | more
```

It prints the match's start time, the mode, every chest with its time, whether the match counts and why not, and the
chests per bucket. It sends nothing. `| more` makes the command prompt wait for the output.

## Building

Needs the .NET 10 SDK.

```
dotnet test tests/LootPose.Companion.Tests/LootPose.Companion.Tests.csproj
dotnet publish src/LootPose.Companion/LootPose.Companion.csproj -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -p:EnableCompressionInSingleFile=true -p:DebugType=none -o dist
```

The exe needs LootPose's database address and publishable key, which are public by design. Release builds take them
from this repository's variables `LOOTPOSE_SUPABASE_URL` and `LOOTPOSE_SUPABASE_KEY`. For a local run, set environment
variables with the same names. Without them the app runs but cannot link.

## Credits

- [LED-N/Fortnite-ChestCounter](https://github.com/LED-N/Fortnite-ChestCounter) (MIT): the replay analyzer and the
  rules for what counts, copied unchanged into `vendor/`. See `vendor/LICENSE-ChestCounter` and `vendor/UPSTREAM.md`.
- [Shiqan/FortniteReplayDecompressor](https://github.com/Shiqan/FortniteReplayDecompressor) (MIT): the replay reader,
  in LED-N's patched copy in `vendor/lib/`. See `vendor/lib/LICENSE-FortniteReplayDecompressor` and
  `vendor/lib/PATCHES.md`.

## Licence

MIT. See `LICENSE`.
