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
- Build → Defense → Result → Rebuild, plus browser-persistent Continue after a page reload
- production `InvasionSimulation` playable flow: Scouting / Formation → section battle → Result
- Black Iron Mine canonical formation plus manual deployment, Mend/Ward support, retreat, and 1x/2x/3x speed
- start / pause / replay
- 1x / 2x / 3x simulation speed
- freeze / push spell commands
- live Core HP / MP / wave / tick / recent event presentation
- ja/en Web-demo localization with production build terminology
- paid Theme Pack preview using synchronized production assets; Web billing stays disabled by capability
- responsive static hosting suitable for GitHub Pages

It is **not** a port of the Godot UI tree. The browser host stays intentionally thin: placement rules, Defense rules, and the spatial hostile-dungeon Invasion runtime remain in production Application/Core, while shared Product Presentation supplies the host-neutral view state and combat motion consumed by both Godot and Web. Web owns HTTP transport, HTML/SVG/CSS rendering, browser input, and responsive layout—not a second gameplay model. Full Campaign progression persistence, audio, native billing, and the full native mobile shell remain outside the demo. The Web host persists the current production `PlayerDungeonSaveFile` in browser `localStorage`, so the playable Run survives reload without inventing a Web-only gameplay save model.

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

The browser smoke publishes the app to a temporary directory and drives headless Chrome through the DevTools Protocol without third-party Node packages. It first verifies the real `Rite of the Azure Core` production-asset Shop preview and disabled Web billing boundary, then starts a Run, edits the dungeon, completes a defense, persists the production player-dungeon save to `localStorage`, reloads the page and verifies Continue restoration. It also switches ja/en, verifies narrow mobile containment, and runs the canonical Black Iron invasion through deployment, Ward support, Result, and return to Defense. `WEB_SMOKE_BASE_URL` can point the same flow at the deployed GitHub Pages site.

## Source provenance

See `SOURCE_REVISION.txt`. The shared C# projects are intentionally copied into this separate repository so GitHub Pages CI can build independently of the private/local game workspace.

## Refreshing the production snapshot

The sibling `2dTD` source remains authoritative. Refresh the copied projects, playable content, and promoted art with one command:

```bash
./scripts/sync-from-2dtd.sh
```

The command synchronizes `Core`, `Contracts`, `Application`, and `Presentation`, removes stale snapshot files, refreshes playable content including `cosmetics.json` and production art (including paid Theme assets), records the source Git revision, and performs a Release build. It rejects a dirty `2dTD` source tree by default so `SOURCE_REVISION.txt` stays reproducible.

Useful validation modes:

```bash
# Show what would change without modifying the Web repository.
./scripts/sync-from-2dtd.sh --dry-run --allow-dirty --no-build

# After a clean source snapshot has been synchronized, prove there is no drift.
./scripts/sync-from-2dtd.sh --verify-only
```

`--allow-dirty` is intended only for local preview work; snapshots produced that way are explicitly marked as non-reproducible in `SOURCE_REVISION.txt`. Godot `.import` metadata, build output, and other host-only files are excluded from the Web snapshot.
