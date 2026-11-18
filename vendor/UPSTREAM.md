# Vendored code

Copied from https://github.com/LED-N/Fortnite-ChestCounter (MIT, see `LICENSE-ChestCounter`) at commit
`c4ca1400ff1f79dbee4ea3ecb3f8eb72c8e6bb82` (2026-11-14).

`lib/` is LED-N's patched copy of https://github.com/Shiqan/FortniteReplayDecompressor (MIT, see
`lib/LICENSE-FortniteReplayDecompressor`). LED-N's changes to it are listed in `lib/PATCHES.md`.

## Copied unchanged

Paths are the same as in LED-N's repository, so a newer copy can be laid over the top.

- `lib/`, all of it (202 files)
- `src/ChestCounter/ReplayAnalyzer.cs`
- `src/ChestCounter/Models.cs`
- `src/ChestCounter/GameModes.cs`
- `src/ChestCounter/Lang.cs`
- `Directory.Build.props`
- `LICENSE`, renamed `LICENSE-ChestCounter`

No copied file is edited.

## Local changes

1. `src/ChestCounter/ReplayHeaderProbe.cs` (new): the `ReplayHeaderProbe` class, copied unchanged from LED-N's
   `src/ChestCounter/Tracker.cs` (lines 670 to 779). The rest of `Tracker.cs` is LED-N's app and needs files that are
   not copied here.
2. `src/ChestCounter/TreasureCatalogStub.cs` (new): stands in for LED-N's `TreasureCatalog.cs`, which loads
   `content.json` to name treasure prizes. `TreasureNames` in `ReplayAnalyzer.cs` calls it; every prize is unknown.
   The companion only counts chests.
3. `src/ChestCounter/ChestCounter.Analysis.csproj` (new): builds the four analyzer files and the two new files as a
   library. LED-N's `ChestCounter.csproj` builds their whole app and is not copied.

## Updating to a newer LED-N commit

1. In `../led-n` (a clone of LED-N's repository): `git fetch`, then `git checkout --detach <new commit>`.
2. Copy the files under "Copied unchanged" over the top, with the same `git archive` and `tar` commands as Task 2
   Step 4 of the plan, and the same rename.
3. If LED-N changed `ReplayHeaderProbe` in `Tracker.cs`, copy the new class into `src/ChestCounter/ReplayHeaderProbe.cs`.
4. If one of the four analyzer files now uses another LED-N file, copy that file too, or add a stub here, and list it.
5. Run the tests, run `LootPoseCompanion.exe --analyze` on a real replay, and change the commit at the top of this file.
