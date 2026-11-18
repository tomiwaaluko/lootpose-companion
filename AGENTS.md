# Rules for every agent

This repository is the Windows app of LootPose. It is built from the plan
`docs/superpowers/plans/2026-11-15-companion-app.md` in `tomiwaaluko/chest-tracker`, and follows that repository's
`AGENTS.md`, with these differences:

- Task issues, the project board, the Handoff log (issue #11) and the owner's checklist (issue #12) are in
  `tomiwaaluko/chest-tracker`. Run `tools/board.mjs` from a chest-tracker checkout.
- Branches and pull requests are here. A pull request body says `Closes tomiwaaluko/chest-tracker#<N>`.
- The full check is `dotnet build src/LootPose.Companion/LootPose.Companion.csproj`, then
  `dotnet test tests/LootPose.Companion.Tests/LootPose.Companion.Tests.csproj`. The workflow runs both on every push.
- Never tag a release and never set a repository variable. Both are the owner's.

Project rules:

- The app reads only the replay files in `%LOCALAPPDATA%\FortniteGame\Saved\Demos`, read-only. It may check whether
  a process named `FortniteClient-Win64-Shipping.exe` exists, and never opens it. Nothing reads the game's memory or
  network traffic, injects into the game, or changes a game file.
- Only the device id, the secret (to Supabase, over HTTPS) and chest counts per bucket leave the PC.
- Never commit a `.replay` file. Replays are large and hold other players' names.
- No Epic artwork, logos or fonts. The app's name does not include "Fortnite".
- Nothing under `vendor/` is edited. A local change there is a new file, listed in `vendor/UPSTREAM.md`.
- `tests/fixtures/bucketing.json` is a copy of chest-tracker's `docs/fixtures/bucketing.json`. Before every release,
  and after any change to the week or Treasure Hour rules, these two commands must print the same hash:
  `gh api repos/tomiwaaluko/chest-tracker/contents/docs/fixtures/bucketing.json --jq .sha` and
  `git hash-object tests/fixtures/bucketing.json`. If they differ, copy the file again and run the tests.
