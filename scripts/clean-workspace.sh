#!/usr/bin/env bash
set -euo pipefail

# Lists, and with --apply removes, ClankerWorld files on this machine that can be rebuilt or are
# no longer used. It never touches tracked files, uncommitted or unpushed work, or anything a
# session may still be using. See "Disk space" in docs/development/build-and-test.md.

usage() {
    cat <<'EOF'
Usage: scripts/clean-workspace.sh [--apply] [--offline] [--idle-hours N] [--keep-days N]

Without --apply, only lists what it would remove and why. Run it from any checkout or worktree.

  1. Worktrees of this repository whose work is finished: their branch's copy on origin is
     deleted (GitHub deletes it after a merge) or their commit is already on origin/main, with
     no uncommitted, untracked or local files, not locked, not the one you run this from and
     untouched for --idle-hours. The branch is kept, so `git worktree add <path> <branch>`
     brings the files back. Files in .evidence/keep/ move to the kept-evidence folder first.
  2. Build and test output (bin/, obj/, TestResults/, .godot/, export/, build/) in other
     worktrees untouched for --idle-hours, unless locked.
  3. Tool cache entries that no script here pins and that were unused for --keep-days.
  4. Temporary clankerworld-* folders left by runs and tests, untouched for --idle-hours.
  5. Kept evidence older than --keep-days.

  --apply         remove what is listed
  --offline       don't fetch; only worktrees whose commit is on origin/main count as finished
  --idle-hours N  how long a folder must go unchanged to count as idle (default 12)
  --keep-days N   how long to keep unused tool versions and kept evidence (default 30)
EOF
}

apply=false
offline=false
idle_hours=12
keep_days=30
while (( $# > 0 )); do
    case "$1" in
        --apply) apply=true ;;
        --offline) offline=true ;;
        --idle-hours) idle_hours="${2:?--idle-hours needs a number}"; shift ;;
        --keep-days) keep_days="${2:?--keep-days needs a number}"; shift ;;
        -h|--help) usage; exit 0 ;;
        *) usage >&2; exit 2 ;;
    esac
    shift
done
if [[ ! "${idle_hours}" =~ ^[0-9]+$ || ! "${keep_days}" =~ ^[0-9]+$ ]]; then
    printf -- '--idle-hours and --keep-days take whole numbers.\n' >&2
    exit 2
fi

here="$(git rev-parse --show-toplevel)"
script_dir="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
# shellcheck source=tool-cache.sh
source "${script_dir}/tool-cache.sh"
kept_root="${CLANKERWORLD_KEPT_EVIDENCE:-${XDG_STATE_HOME:-${HOME}/.local/state}/clankerworld/kept-evidence}"
temp_root="${TMPDIR:-/tmp}"
temp_root="${temp_root%/}"
idle_minutes=$((idle_hours * 60))
keep_minutes=$((keep_days * 24 * 60))

# Ignored paths that a build or test run recreates. Any other ignored file, such as .env, saves/,
# logs/, keys or editor settings, is local and keeps its worktree.
reproducible_pattern='(^|/)(bin|obj|TestResults|build|export|\.godot|[^/]*\.godot|__pycache__|\.pytest_cache)(/|$)|\.py[cod]$|(^|/)\.DS_Store$'
build_output_pattern='(^|/)(bin|obj|TestResults|build|export|\.godot|[^/]*\.godot)/$'

listed_kib=0
listed=0
note() { printf '  %s\n' "$*"; }

size_kib() {
    du -sk -- "$1" 2>/dev/null | awk '{ print $1 }' || printf '0\n'
}

# Lists one thing to remove, and removes it with --apply.
remove() {
    local reason="$1" path="$2" kib
    kib="$(size_kib "${path}")"
    listed_kib=$((listed_kib + ${kib:-0}))
    listed=$((listed + 1))
    printf '%s  %s\n    %s\n' "$(human "${kib:-0}")" "${path}" "${reason}"
    if [[ "${apply}" == true ]]; then
        chmod -R u+w -- "${path}" 2>/dev/null || true
        rm -rf -- "${path}"
    fi
}

human() {
    awk -v kib="$1" 'BEGIN { if (kib >= 1048576) printf "%6.1f GiB", kib / 1048576; else if (kib >= 1024) printf "%6.1f MiB", kib / 1024; else printf "%6d KiB", kib }'
}

# True when anything under the path changed in the last idle period.
recently_changed() {
    [[ -n "$(find "$1" -mmin "-${idle_minutes}" -print -quit 2>/dev/null)" ]]
}

common_dir="$(cd "$(git rev-parse --git-common-dir)" && pwd)"
remote_known=false
if [[ "${offline}" == false ]]; then
    if git -C "${here}" fetch --prune --quiet origin; then
        remote_known=true
    else
        printf 'Could not fetch origin; treating deleted branches as unknown.\n' >&2
    fi
fi
main_ref=""
if git -C "${here}" rev-parse -q --verify refs/remotes/origin/main >/dev/null; then
    main_ref=refs/remotes/origin/main
fi

finished_reason() {
    local path="$1" branch="$2" head="$3" remote merge
    if [[ -n "${main_ref}" ]] && git -C "${path}" merge-base --is-ancestor "${head}" "${main_ref}" 2>/dev/null; then
        printf 'its commit is on origin/main'
        return 0
    fi
    [[ -n "${branch}" && "${remote_known}" == true ]] || return 1
    remote="$(git -C "${path}" config "branch.${branch}.remote" || true)"
    merge="$(git -C "${path}" config "branch.${branch}.merge" || true)"
    [[ -n "${remote}" && -n "${merge}" ]] || return 1
    if git -C "${path}" rev-parse -q --verify "refs/remotes/${remote}/${merge#refs/heads/}" >/dev/null; then
        return 1
    fi
    printf 'its branch on %s was deleted' "${remote}"
}

archive_kept_evidence() {
    local path="$1" name="$2" destination
    [[ -d "${path}/.evidence/keep" ]] || return 0
    destination="${kept_root}/${name//\//__}-$(date +%Y%m%d%H%M%S)"
    note "keeps ${path}/.evidence/keep in ${destination}"
    if [[ "${apply}" == true ]]; then
        mkdir -p "${kept_root}"
        mv -- "${path}/.evidence/keep" "${destination}"
        # Its age counts from now, not from when the files were written.
        touch "${destination}"
    fi
}

printf '%s what can be cleaned for %s\n\n' "$([[ "${apply}" == true ]] && echo Removing || echo Listing)" "${common_dir%/.git}"
printf 'Worktrees:\n'
prunable=false
first=true
path="" head="" branch="" locked=false
handle_worktree() {
    local is_main="$1" reason gitdir status ignored local_files
    [[ -n "${path}" ]] || return 0
    if [[ ! -d "${path}" ]]; then
        note "${path}: folder is gone; git's record of it will be pruned"
        prunable=true
        return 0
    fi
    if [[ "${path}" == "${here}" ]]; then
        note "${path}: kept, you are running from it"
        return 0
    fi
    if [[ "${locked}" == true ]]; then
        note "${path}: kept, locked"
        return 0
    fi
    gitdir="$(git -C "${path}" rev-parse --absolute-git-dir)"
    # The worktree's own index and HEAD change with every commit, checkout or staging.
    if recently_changed "${path}" ||
        [[ -n "$(find "${gitdir}" -maxdepth 1 \( -name index -o -name HEAD \) -mmin "-${idle_minutes}" -print -quit)" ]]; then
        note "${path}: kept, changed in the last ${idle_hours} hours"
        return 0
    fi
    # --no-optional-locks keeps git status from rewriting the index, which would look like activity.
    status="$(git --no-optional-locks -C "${path}" status --porcelain --untracked-files=all)"
    ignored="$(git --no-optional-locks -C "${path}" status --porcelain --ignored=traditional --untracked-files=normal |
        sed -n 's/^!! //p')"
    local_files="$(printf '%s\n' "${ignored}" | grep -Ev "${reproducible_pattern}|^\.evidence/" | grep -v '^$' | head -3 || true)"
    if [[ "${is_main}" == false && -z "${status}" && -z "${local_files}" ]] && reason="$(finished_reason "${path}" "${branch}" "${head}")"; then
        archive_kept_evidence "${path}" "${branch:-${path##*/}}"
        remove "finished worktree: ${reason}; branch ${branch:-(detached)} is kept" "${path}"
        prunable=true
        return 0
    fi
    if [[ -n "${status}" ]]; then
        note "${path}: kept, has uncommitted or untracked files"
    elif [[ -n "${local_files}" ]]; then
        note "${path}: kept, has local files ($(printf '%s' "${local_files}" | tr '\n' ' '))"
    elif [[ "${is_main}" == true ]]; then
        note "${path}: kept, the main checkout"
    else
        note "${path}: kept, work not finished"
    fi
    while IFS= read -r output; do
        [[ -n "${output}" ]] && remove "build output in an idle worktree" "${path}/${output%/}"
    done < <(printf '%s\n' "${ignored}" | grep -E "${build_output_pattern}" || true)
}

while IFS= read -r line || [[ -n "${line}" ]]; do
    case "${line}" in
        "worktree "*) path="${line#worktree }" ;;
        "HEAD "*) head="${line#HEAD }" ;;
        "branch "*) branch="${line#branch refs/heads/}" ;;
        locked*) locked=true ;;
        "")
            handle_worktree "${first}"
            first=false
            path="" head="" branch="" locked=false
            ;;
    esac
done < <(git -C "${here}" worktree list --porcelain; printf '\n')
if [[ "${prunable}" == true && "${apply}" == true ]]; then
    git -C "${here}" worktree prune
fi

printf '\nTool cache (%s):\n' "${tool_cache_root}"
pinned="$(cat "${script_dir}"/*.sh | grep -oE '\b[0-9a-f]{64}\b' | sort -u || true)"
legacy_cache="${XDG_CACHE_HOME:-${HOME}/.cache}/clankerworld-godot"
if [[ -d "${legacy_cache}" ]]; then
    # Move verified archives from the old export cache instead of downloading them again.
    for file in "${legacy_cache}"/*; do
        [[ -f "${file}" ]] || continue
        sha256="$(tool_cache_sha256 "${file}")"
        if grep -qx "${sha256}" <<<"${pinned}" && [[ ! -e "${tool_cache_root}/downloads/${sha256}/${file##*/}" ]]; then
            note "moves ${file##*/} from the old cache into the shared one"
            if [[ "${apply}" == true ]]; then
                mkdir -p "${tool_cache_root}/downloads/${sha256}"
                mv -- "${file}" "${tool_cache_root}/downloads/${sha256}/"
            fi
        fi
    done
    remove "old Godot download cache, replaced by the shared tool cache" "${legacy_cache}"
fi
for kind in downloads unpacked; do
    [[ -d "${tool_cache_root}/${kind}" ]] || continue
    for entry in "${tool_cache_root}/${kind}"/*; do
        [[ -e "${entry}" ]] || continue
        name="${entry##*/}"
        if [[ "${name}" == *.partial.* ]]; then
            [[ -n "$(find "${entry}" -maxdepth 0 -mmin -1440 -print)" ]] || remove "unfinished download or unpacking" "${entry}"
        elif ! grep -qx "${name}" <<<"${pinned}" && [[ -z "$(find "${entry}" -maxdepth 0 -mmin "-${keep_minutes}" -print)" ]]; then
            remove "tool version no script here pins, unused for ${keep_days} days" "${entry}"
        fi
    done
done

printf '\nTemporary folders (%s):\n' "${temp_root}"
for entry in "${temp_root}"/clankerworld-* "${temp_root}"/tmp.*/clankerworld-godot-*; do
    [[ -e "${entry}" ]] || continue
    recently_changed "${entry}" && continue
    if [[ "${entry}" == "${temp_root}"/tmp.*/* ]]; then
        # Left by an older Godot client check, inside a folder from mktemp -d.
        entry="${entry%/*}"
        [[ -z "$(find "${entry}" -mindepth 1 -maxdepth 1 ! -name 'clankerworld-godot-*' -print -quit)" ]] || continue
    fi
    remove "left by a run or test" "${entry}"
done

printf '\nKept evidence (%s):\n' "${kept_root}"
if [[ -d "${kept_root}" ]]; then
    for entry in "${kept_root}"/*; do
        [[ -e "${entry}" ]] || continue
        [[ -n "$(find "${entry}" -maxdepth 0 -mmin "-${keep_minutes}" -print)" ]] || remove "kept for more than ${keep_days} days" "${entry}"
    done
fi

printf '\n%s %d item(s), %s.\n' "$([[ "${apply}" == true ]] && echo Removed || echo Would remove)" "${listed}" "$(human "${listed_kib}" | sed 's/^ *//')"
if [[ "${apply}" == false && "${listed}" -gt 0 ]]; then
    printf 'Run again with --apply to remove them.\n'
fi
