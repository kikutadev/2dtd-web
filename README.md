# 2dTD Web Playable Demo

Public browser demo for playing the production 2dTD dungeon-build and defense rules directly in WebAssembly on GitHub Pages.

## What this is

Godot 4 C# projects cannot currently be exported directly to Web. This repository therefore uses a thin **Blazor WebAssembly** presentation host while keeping a source snapshot of the production:

- `DungeonDefense.Core`
- `DungeonDefense.Contracts`
- `DungeonDefense.Application`
- `DungeonDefense.Presentation`（shared motion timeline → immutable `CombatVisualState`）
- promoted production art
- `vertical-slice.json` defense content

The browser demo runs the real `DefenseSimulation`; it is not a JavaScript reimplementation of the combat rules.

## Current playable scope

- production dungeon tiles / units / traps / facilities / core art
- actual C# defense simulation in browser WASM
- shared `CombatVisualState`によるMove / Attack / Hit / Death / Push / projectile motion
- 20Hz Core simulation clockと約60Hz browser render clockの分離
- production `DefenseEditCommandService` placement preview / place / remove
- Build → Defense → Result → Rebuild without a page reload
- production `InvasionSimulation` playable flow: Scouting / Formation → section battle → Result
- Black Iron Mine canonical formation plus manual deployment, Mend/Ward support, retreat, and 1x/2x/3x speed
- start / pause / replay
- 1x / 2x / 3x simulation speed
- freeze / push spell commands
- live Core HP / MP / wave / tick / recent event presentation
- ja/en Web-demo localization with production build terminology
- responsive static hosting suitable for GitHub Pages

It is **not** a port of the Godot editor UI. The browser host stays intentionally thin: placement rules, Defense rules, and the section-based Invasion rules remain in the production Application/Core, while the Web layer only renders and translates the minimum playable interactions. The Invasion view intentionally does not convert its section model into DefenseSimulation. Campaign persistence, audio, and the full native mobile shell remain outside the demo.

## Local build

```bash
dotnet restore src/DungeonDefense.Web/DungeonDefense.Web.csproj
dotnet build src/DungeonDefense.Web/DungeonDefense.Web.csproj -c Release
dotnet run --project src/DungeonDefense.Web/DungeonDefense.Web.csproj -c Release
```

## Acceptance smoke

```bash
dotnet test tests/DungeonDefense.Web.Tests/DungeonDefense.Web.Tests.csproj -c Release
node scripts/browser-smoke.mjs
```

The browser smoke publishes the app to a temporary directory and drives headless Chrome through the DevTools Protocol without third-party Node packages. It places a trap, guard, and facility through real UI clicks, completes a 3× defense, returns to the same editable build, removes a placement, switches to English, verifies a 390px mobile viewport, then runs the canonical Black Iron invasion through deployment, Ward support, section completion, Result, ja/en switching, and return to Defense. `WEB_SMOKE_BASE_URL` can point the same flow at the deployed GitHub Pages site.

## Source provenance

See `SOURCE_REVISION.txt`. The shared C# projects are intentionally copied into this separate repository so GitHub Pages CI can build independently of the private/local game workspace.

## Refreshing the production snapshot

The sibling `2dTD` source remains authoritative. Refresh the copied projects, playable content, and promoted art with one command:

```bash
./scripts/sync-from-2dtd.sh
```

The command synchronizes `Core`, `Contracts`, `Application`, and `Presentation`, removes stale snapshot files, refreshes `vertical-slice.json` and production art, records the source Git revision, and performs a Release build. It rejects a dirty `2dTD` source tree by default so `SOURCE_REVISION.txt` stays reproducible.

Useful validation modes:

```bash
# Show what would change without modifying the Web repository.
./scripts/sync-from-2dtd.sh --dry-run --allow-dirty --no-build

# After a clean source snapshot has been synchronized, prove there is no drift.
./scripts/sync-from-2dtd.sh --verify-only
```

`--allow-dirty` is intended only for local preview work; snapshots produced that way are explicitly marked as non-reproducible in `SOURCE_REVISION.txt`. Godot `.import` metadata, build output, and other host-only files are excluded from the Web snapshot.
