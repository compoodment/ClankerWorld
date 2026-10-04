# Shared cache for the pinned tools that ClankerWorld scripts download, such as Godot.
# Source this file, then:
#   path="$(tool_cache_download <url> <sha256> <file-name>)"   a verified download
#   folder="$(tool_cache_unzip <archive> <sha256>)"             that archive unpacked, once
# Each entry is keyed by its pinned SHA-256, so every version and platform has its own and a
# changed pin never reuses an old file. A download is checked when it arrives and again each
# time it is used. Work happens in a temporary path that is renamed into place under a lock, so
# several sessions can share the cache at once. Unpacked folders are made read-only.
# CLANKERWORLD_TOOL_CACHE moves the cache; scripts/clean-workspace.sh prunes what no script pins.

tool_cache_root="${CLANKERWORLD_TOOL_CACHE:-${XDG_CACHE_HOME:-${HOME}/.cache}/clankerworld/tools}"

tool_cache_sha256() {
    if command -v sha256sum >/dev/null 2>&1; then
        sha256sum "$1" | awk '{ print $1 }'
    else
        shasum -a 256 "$1" | awk '{ print $1 }'
    fi
}

# Runs the rest of the arguments as a command while holding the lock named by the first.
tool_cache_locked() {
    local name="$1"
    shift
    local lock="${tool_cache_root}/locks/${name}"
    mkdir -p "${tool_cache_root}/locks" || return 1
    if command -v flock >/dev/null 2>&1; then
        (
            flock 9 || exit 1
            "$@"
        ) 9>"${lock}.lock"
        return
    fi
    # Without flock, mkdir is atomic. Never delete another process's lock: two stale-lock
    # waiters could otherwise remove a new owner's directory. Fail closed on stale locks.
    local waited=0 owner
    until mkdir "${lock}.d" 2>/dev/null; do
        owner="$(cat "${lock}.d/pid" 2>/dev/null || true)"
        if [[ "${owner}" =~ ^[1-9][0-9]*$ ]] && ! kill -0 "${owner}" 2>/dev/null &&
            [[ "$(cat "${lock}.d/pid" 2>/dev/null || true)" == "${owner}" ]]; then
            printf 'Stale tool cache lock; inspect and remove %s before retrying\n' "${lock}.d" >&2
            return 1
        fi
        if (( waited >= 5 )) && [[ ! -f "${lock}.d/pid" ]]; then
            printf 'Uninitialised tool cache lock; inspect %s before retrying\n' "${lock}.d" >&2
            return 1
        fi
        if (( waited >= 3600 )); then
            printf 'Timed out waiting for the tool cache lock %s\n' "${lock}.d" >&2
            return 1
        fi
        sleep 1
        waited=$((waited + 1))
    done
    printf '%s\n' "${BASHPID:-$$}" > "${lock}.d/pid" || return 1
    (
        trap 'rm -rf -- "${lock}.d"' EXIT
        "$@"
    )
}

tool_cache_fetch() {
    local url="$1" sha256="$2" path="$3" partial actual
    if [[ -f "${path}" ]]; then
        printf 'Checking cached %s\n' "${path##*/}"
        if [[ "$(tool_cache_sha256 "${path}")" == "${sha256}" ]]; then
            return 0
        fi
        printf 'Cached %s failed its SHA-256 check; downloading it again\n' "${path##*/}"
        rm -f -- "${path}"
    fi
    mkdir -p "${path%/*}" || return 1
    partial="$(mktemp "${path}.partial.XXXXXX")" || return 1
    printf 'Downloading %s\n' "${path##*/}"
    if ! curl --fail --location --retry 3 --retry-all-errors --silent --show-error \
        --output "${partial}" "${url}"; then
        rm -f -- "${partial}"
        return 1
    fi
    actual="$(tool_cache_sha256 "${partial}")"
    if [[ "${actual}" != "${sha256}" ]]; then
        printf '%s SHA-256 mismatch\nexpected: %s\nactual:   %s\n' "${path##*/}" "${sha256}" "${actual}"
        rm -f -- "${partial}"
        return 1
    fi
    mv -- "${partial}" "${path}" || return 1
}

tool_cache_use_download() {
    tool_cache_fetch "$@" || return 1
    # Record use before releasing the same lock the cleanup holds. Never recreate a
    # removed directory as an empty file if a path unexpectedly disappears.
    touch -c "${3%/*}" || return 1
}

tool_cache_download() {
    local url="$1" sha256="$2" name="$3"
    local folder="${tool_cache_root}/downloads/${sha256}"
    tool_cache_locked "${sha256}" tool_cache_use_download "${url}" "${sha256}" "${folder}/${name}" >&2 || return 1
    [[ -f "${folder}/${name}" ]] || return 1
    printf '%s\n' "${folder}/${name}"
}

tool_cache_unpack() {
    local archive="$1" folder="$2" partial
    if [[ -f "${folder}/.complete" ]]; then
        touch -c "${folder}" || return 1
        return 0
    fi
    if [[ -e "${folder}" ]]; then
        chmod -R u+w -- "${folder}"
        rm -rf -- "${folder}"
    fi
    mkdir -p "${folder%/*}" || return 1
    partial="$(mktemp -d "${folder}.partial.XXXXXX")" || return 1
    printf 'Unpacking %s\n' "${archive##*/}"
    if ! unzip -q "${archive}" -d "${partial}"; then
        rm -rf -- "${partial}"
        return 1
    fi
    : > "${partial}/.complete" || return 1
    # This lock excludes other publishers; refuse an unexpected destination rather
    # than letting mv put the temporary folder inside it.
    [[ ! -e "${folder}" ]] || return 1
    mv -- "${partial}" "${folder}" || return 1
    # Read-only, so no run can change what other runs share.
    chmod -R a-w -- "${folder}" || return 1
    touch -c "${folder}" || return 1
}

# The archive must already be verified against the SHA-256 given, as tool_cache_download does.
tool_cache_unzip() {
    local archive="$1" sha256="$2"
    local folder="${tool_cache_root}/unpacked/${sha256}"
    tool_cache_locked "${sha256}" tool_cache_unpack "${archive}" "${folder}" >&2 || return 1
    [[ -d "${folder}" && -f "${folder}/.complete" ]] || return 1
    printf '%s\n' "${folder}"
}
