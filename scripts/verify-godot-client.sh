#!/usr/bin/env bash
set -euo pipefail

# Keep the Godot editor out of the repository and verify the exact archived
# .NET-capable engine in CI. The project itself remains a normal C# project,
# while this step proves Godot can import its scene and build the attached code.
# The editor comes from the shared tool cache (scripts/tool-cache.sh).
readonly GODOT_VERSION="4.7.2"
readonly GODOT_RELEASE="4.7.2-stable"
readonly GODOT_ARCHIVE="Godot_v${GODOT_VERSION}-stable_mono_linux_x86_64.zip"
readonly GODOT_SHA256="129f82db7bafd54ae14bb5bb284041c73860e8c7a009a3a026ca5e946cbff247"
readonly GODOT_URL="https://github.com/godotengine/godot/releases/download/${GODOT_RELEASE}/${GODOT_ARCHIVE}"

repo_root="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
# shellcheck source=tool-cache.sh
source "${repo_root}/scripts/tool-cache.sh"

archive_path="$(tool_cache_download "${GODOT_URL}" "${GODOT_SHA256}" "${GODOT_ARCHIVE}")"
tool_root="$(tool_cache_unzip "${archive_path}" "${GODOT_SHA256}")"
godot_bin="$(find "${tool_root}" -type f -name "Godot_v${GODOT_VERSION}-stable_mono_linux.x86_64" -print -quit)"
test -n "${godot_bin}"

# Godot keeps its settings, caches and the game's user:// files under these folders. Give each
# run its own, so runs from different worktrees or sessions share no state, and remove it after.
scratch_root="$(mktemp -d "${RUNNER_TEMP:-${TMPDIR:-/tmp}}/clankerworld-godot-client.XXXXXX")"
trap 'rm -rf -- "${scratch_root}"' EXIT
printf '%s\n' "$$" > "${scratch_root}/.clankerworld-run.pid"
export XDG_CONFIG_HOME="${scratch_root}/config"
export XDG_DATA_HOME="${scratch_root}/data"
export XDG_CACHE_HOME="${scratch_root}/cache"

printf 'Building Godot C# scripts and starting the scene headlessly\n'
"${godot_bin}" --headless --path "${repo_root}/src/ClankerWorld.GodotClient" --build-solutions --quit
"${godot_bin}" --headless --path "${repo_root}/src/ClankerWorld.GodotClient" --quit-after 120
"${godot_bin}" --headless --path "${repo_root}/src/ClankerWorld.GodotClient" -- --ui-smoke-test
