#!/usr/bin/env bash
set -euo pipefail

# Synchronize the production C# gameplay projects and browser-visible content from
# the sibling 2dTD repository. Reproducibility is determined from the paths actually
# copied into Web; unrelated Godot/docs work may remain dirty during parallel work.

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
WEB_ROOT="$(cd "$SCRIPT_DIR/.." && pwd)"
SOURCE_ROOT="$(cd "$WEB_ROOT/../2dTD" 2>/dev/null && pwd || true)"
ALLOW_DIRTY=0
DRY_RUN=0
SKIP_BUILD=0
VERIFY_ONLY=0
DOTNET="${DOTNET:-$(command -v dotnet 2>/dev/null || true)}"
if [[ -z "$DOTNET" && -x /usr/local/share/dotnet/dotnet ]]; then
  DOTNET=/usr/local/share/dotnet/dotnet
fi
if [[ -z "$DOTNET" ]]; then
  echo "error: dotnet not found" >&2
  exit 127
fi

usage() {
  cat <<'EOF'
Usage: scripts/sync-from-2dtd.sh [options]

Options:
  --source <path>   Source 2dTD repository. Default: ../2dTD
  --allow-dirty     Allow a dirty source tree for local preview snapshots.
                    The provenance file will mark the snapshot non-reproducible.
  --dry-run         Print the files that would be synchronized without changing them.
  --no-build        Skip the Release build after synchronization.
  --verify-only     Compare the current snapshot with the source without modifying it.
  -h, --help        Show this help.
EOF
}

while [[ $# -gt 0 ]]; do
  case "$1" in
    --source)
      [[ $# -ge 2 ]] || { echo "error: --source requires a path" >&2; exit 2; }
      SOURCE_ROOT="$(cd "$2" 2>/dev/null && pwd || true)"
      shift 2
      ;;
    --allow-dirty)
      ALLOW_DIRTY=1
      shift
      ;;
    --dry-run)
      DRY_RUN=1
      shift
      ;;
    --no-build)
      SKIP_BUILD=1
      shift
      ;;
    --verify-only)
      VERIFY_ONLY=1
      shift
      ;;
    -h|--help)
      usage
      exit 0
      ;;
    *)
      echo "error: unknown option: $1" >&2
      usage >&2
      exit 2
      ;;
  esac
done

if [[ -z "$SOURCE_ROOT" ]] || ! SOURCE_GIT_ROOT="$(git -C "$SOURCE_ROOT" rev-parse --show-toplevel 2>/dev/null)"; then
  echo "error: source repository not found. Pass --source <2dTD path>." >&2
  exit 2
fi

require_path() {
  local path="$1"
  [[ -e "$path" ]] || { echo "error: required source path is missing: $path" >&2; exit 2; }
}

require_path "$SOURCE_ROOT/src/DungeonDefense.Core"
require_path "$SOURCE_ROOT/src/DungeonDefense.Contracts"
require_path "$SOURCE_ROOT/src/DungeonDefense.Application"
require_path "$SOURCE_ROOT/src/DungeonDefense.Infrastructure"
require_path "$SOURCE_ROOT/src/DungeonDefense.Presentation"
require_path "$SOURCE_ROOT/content/monster-roster.json"
require_path "$SOURCE_ROOT/content/vertical-slice.json"
require_path "$SOURCE_ROOT/content/invasion-vertical-slice.json"
require_path "$SOURCE_ROOT/content/invasion-maps.json"
require_path "$SOURCE_ROOT/content/cosmetics.json"
require_path "$SOURCE_ROOT/godot/assets/production"

SNAPSHOT_PATHS=(
  src/DungeonDefense.Core
  src/DungeonDefense.Contracts
  src/DungeonDefense.Application
  src/DungeonDefense.Infrastructure
  src/DungeonDefense.Presentation
  content/monster-roster.json
  content/vertical-slice.json
  content/invasion-vertical-slice.json
  content/invasion-maps.json
  content/narrative-campaign.json
  content/cosmetics.json
  godot/assets/production
)
# Record the newest commit that actually changed the synchronized snapshot paths.
# Repository HEAD may advance because of docs or other parallel work that Web never copies.
SOURCE_REVISION="$(git -C "$SOURCE_ROOT" log -1 --format=%H -- "${SNAPSHOT_PATHS[@]}")"
SOURCE_STATUS="$(git -C "$SOURCE_ROOT" status --porcelain --untracked-files=normal -- "${SNAPSHOT_PATHS[@]}")"
SOURCE_DIRTY=0
if [[ -n "$SOURCE_STATUS" ]]; then
  SOURCE_DIRTY=1
fi
REPOSITORY_STATUS="$(git -C "$SOURCE_ROOT" status --porcelain --untracked-files=normal -- .)"
REPOSITORY_DIRTY=0
if [[ -n "$REPOSITORY_STATUS" ]]; then
  REPOSITORY_DIRTY=1
fi

if [[ "$SOURCE_DIRTY" -eq 1 && "$ALLOW_DIRTY" -ne 1 ]]; then
  echo "error: Web snapshot source paths are dirty; refusing to record a misleading revision snapshot." >&2
  echo "       Commit/stash changes under shared src/content/production-assets, or use --allow-dirty for a local preview." >&2
  exit 3
fi

RSYNC_COMMON=(-a --omit-dir-times --delete --exclude bin --exclude obj --exclude .DS_Store --exclude '*.import')
if [[ "$DRY_RUN" -eq 1 || "$VERIFY_ONLY" -eq 1 ]]; then
  RSYNC_COMMON+=(--dry-run --itemize-changes)
fi

sync_tree() {
  local source="$1"
  local target="$2"

  mkdir -p "$target"
  rsync "${RSYNC_COMMON[@]}" "$source/" "$target/"
}

sync_file() {
  local source="$1"
  local target="$2"

  mkdir -p "$(dirname "$target")"
  if [[ "$DRY_RUN" -eq 1 || "$VERIFY_ONLY" -eq 1 ]]; then
    if ! cmp -s "$source" "$target" 2>/dev/null; then
      echo "would update: ${target#$WEB_ROOT/}"
    fi
    return
  fi
  cp "$source" "$target"
}

echo "Source:   $SOURCE_ROOT"
echo "Revision: $SOURCE_REVISION"
if [[ "$SOURCE_DIRTY" -eq 1 ]]; then
  echo "State:    snapshot paths dirty (local preview; not reproducible from revision alone)"
elif [[ "$REPOSITORY_DIRTY" -eq 1 ]]; then
  echo "State:    snapshot paths clean (unrelated source-repository changes ignored)"
else
  echo "State:    clean"
fi

PROJECTS=(
  DungeonDefense.Core
  DungeonDefense.Contracts
  DungeonDefense.Application
  DungeonDefense.Infrastructure
  DungeonDefense.Presentation
)

for project in "${PROJECTS[@]}"; do
  sync_tree "$SOURCE_ROOT/src/$project" "$WEB_ROOT/src/$project"
done

sync_file \
  "$SOURCE_ROOT/content/monster-roster.json" \
  "$WEB_ROOT/src/DungeonDefense.Web/wwwroot/content/monster-roster.json"
sync_file \
  "$SOURCE_ROOT/content/vertical-slice.json" \
  "$WEB_ROOT/src/DungeonDefense.Web/wwwroot/content/vertical-slice.json"
sync_file \
  "$SOURCE_ROOT/content/invasion-vertical-slice.json" \
  "$WEB_ROOT/src/DungeonDefense.Web/wwwroot/content/invasion-vertical-slice.json"
sync_file \
  "$SOURCE_ROOT/content/invasion-maps.json" \
  "$WEB_ROOT/src/DungeonDefense.Web/wwwroot/content/invasion-maps.json"
sync_file \
  "$SOURCE_ROOT/content/narrative-campaign.json" \
  "$WEB_ROOT/src/DungeonDefense.Web/wwwroot/content/narrative-campaign.json"
sync_file \
  "$SOURCE_ROOT/content/cosmetics.json" \
  "$WEB_ROOT/src/DungeonDefense.Web/wwwroot/content/cosmetics.json"

sync_tree \
  "$SOURCE_ROOT/godot/assets/production" \
  "$WEB_ROOT/src/DungeonDefense.Web/wwwroot/assets/production"

if [[ "$VERIFY_ONLY" -eq 1 ]]; then
  # A second rsync pass without modifying output lets us fail when any delta exists.
  VERIFY_OUTPUT="$({
    for project in "${PROJECTS[@]}"; do
      rsync -a --omit-dir-times --delete --exclude bin --exclude obj --exclude .DS_Store --exclude '*.import' --dry-run --itemize-changes \
        "$SOURCE_ROOT/src/$project/" "$WEB_ROOT/src/$project/"
    done
    if ! cmp -s "$SOURCE_ROOT/content/monster-roster.json" "$WEB_ROOT/src/DungeonDefense.Web/wwwroot/content/monster-roster.json"; then
      echo "content/monster-roster.json differs"
    fi
    if ! cmp -s "$SOURCE_ROOT/content/vertical-slice.json" "$WEB_ROOT/src/DungeonDefense.Web/wwwroot/content/vertical-slice.json"; then
      echo "content/vertical-slice.json differs"
    fi
    if ! cmp -s "$SOURCE_ROOT/content/invasion-vertical-slice.json" "$WEB_ROOT/src/DungeonDefense.Web/wwwroot/content/invasion-vertical-slice.json"; then
      echo "content/invasion-vertical-slice.json differs"
    fi
    if ! cmp -s "$SOURCE_ROOT/content/invasion-maps.json" "$WEB_ROOT/src/DungeonDefense.Web/wwwroot/content/invasion-maps.json"; then
      echo "content/invasion-maps.json differs"
    fi
    if ! cmp -s "$SOURCE_ROOT/content/narrative-campaign.json" "$WEB_ROOT/src/DungeonDefense.Web/wwwroot/content/narrative-campaign.json"; then
      echo "content/narrative-campaign.json differs"
    fi
    if ! cmp -s "$SOURCE_ROOT/content/cosmetics.json" "$WEB_ROOT/src/DungeonDefense.Web/wwwroot/content/cosmetics.json"; then
      echo "content/cosmetics.json differs"
    fi
    rsync -a --omit-dir-times --delete --exclude .DS_Store --exclude '*.import' --dry-run --itemize-changes \
      "$SOURCE_ROOT/godot/assets/production/" "$WEB_ROOT/src/DungeonDefense.Web/wwwroot/assets/production/"
  } | sed '/^[[:space:]]*$/d')"

  if [[ -n "$VERIFY_OUTPUT" ]]; then
    echo
    echo "snapshot differs from source:" >&2
    echo "$VERIFY_OUTPUT" >&2
    exit 4
  fi

  RECORDED_REVISION="$(sed -n 's/^Source game revision: //p' "$WEB_ROOT/SOURCE_REVISION.txt" | head -n 1)"
  if [[ "$SOURCE_DIRTY" -eq 0 && "$RECORDED_REVISION" != "$SOURCE_REVISION" ]]; then
    echo "error: snapshot content matches source but SOURCE_REVISION.txt records $RECORDED_REVISION" >&2
    exit 5
  fi

  echo "Snapshot verification passed."
  exit 0
fi

if [[ "$DRY_RUN" -eq 1 ]]; then
  echo "Dry run complete; no files were changed."
  exit 0
fi

DIRTY_LABEL="no"
REPRODUCIBLE_LABEL="yes"
if [[ "$SOURCE_DIRTY" -eq 1 ]]; then
  DIRTY_LABEL="yes"
  REPRODUCIBLE_LABEL="no"
fi

cat > "$WEB_ROOT/SOURCE_REVISION.txt" <<EOF
Source project: 2dTD
Source game revision: $SOURCE_REVISION
Source snapshot paths dirty: $DIRTY_LABEL
Source repository has unrelated changes: $( [[ "$REPOSITORY_DIRTY" -eq 1 ]] && echo yes || echo no )
Reproducible from revision alone: $REPRODUCIBLE_LABEL
Snapshot date: $(date +%Y-%m-%d)
Shared projects: DungeonDefense.Core, DungeonDefense.Contracts, DungeonDefense.Application, DungeonDefense.Infrastructure, DungeonDefense.Presentation
Content: content/monster-roster.json, content/vertical-slice.json, content/invasion-vertical-slice.json, content/invasion-maps.json, content/narrative-campaign.json, content/cosmetics.json
Art: godot/assets/production
EOF

if [[ "$SKIP_BUILD" -ne 1 ]]; then
  "$DOTNET" build "$WEB_ROOT/src/DungeonDefense.Web/DungeonDefense.Web.csproj" -c Release
fi

echo "Snapshot synchronization complete."
