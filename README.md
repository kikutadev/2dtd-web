# 2dTD Web Playable Spike

Public browser build used to verify that the 2dTD gameplay core can run on WebAssembly and be hosted as static files on GitHub Pages.

## What this is

Godot 4 C# projects cannot currently be exported directly to Web. This repository therefore uses a thin **Blazor WebAssembly** presentation host while keeping a source snapshot of the production:

- `DungeonDefense.Core`
- `DungeonDefense.Contracts`
- `DungeonDefense.Application`
- promoted production art
- `vertical-slice.json` defense content

The browser demo runs the real `DefenseSimulation`; it is not a JavaScript reimplementation of the combat rules.

## Scope of the first spike

- production dungeon tiles / units / traps / facilities / core art
- actual C# defense simulation in browser WASM
- start / pause / reset
- 1x / 2x / 3x simulation speed
- freeze / push spell commands
- live Core HP / MP / wave / tick / recent event presentation
- responsive static hosting suitable for GitHub Pages

It is **not** a full port of the current Godot UI, campaign, editor, invasion flow, audio, persistence, or mobile acceptance surface.

## Local build

```bash
dotnet restore src/DungeonDefense.Web/DungeonDefense.Web.csproj
dotnet build src/DungeonDefense.Web/DungeonDefense.Web.csproj -c Release
dotnet run --project src/DungeonDefense.Web/DungeonDefense.Web.csproj -c Release
```

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
