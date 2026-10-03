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
    mkdir -p "${tool_cache_root}/locks"
    if command -v flock >/dev/null 2>&1; then
        (
            flock 9
            "$@"
        ) 9>"${lock}.lock"
        return
    fi
    # Without flock, mkdir is the atomic step; a lock whose process has ended is taken over.
    local waited=0 status=0
    until mkdir "${lock}.d" 2>/dev/null; do
        if [[ -f "${lock}.d/pid" ]] && ! kill -0 "$(cat "${lock}.d/pid" 2>/dev/null)" 2>/dev/null; then
            rm -rf -- "${lock}.d"
            continue
        fi
        if (( waited >= 3600 )); then
            printf 'Timed out waiting for the tool cache lock %s\n' "${lock}.d" >&2
            return 1
        fi
        sleep 1
        waited=$((waited + 1))
    done
    printf '%s\n' "$$" > "${lock}.d/pid"
    "$@" || status=$?
    rm -rf -- "${lock}.d"
    return "${status}"
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
    mkdir -p "${path%/*}"
    partial="$(mktemp "${path}.partial.XXXXXX")"
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
    mv -- "${partial}" "${path}"
}

tool_cache_download() {
    local url="$1" sha256="$2" name="$3"
    local folder="${tool_cache_root}/downloads/${sha256}"
    tool_cache_locked "${sha256}" tool_cache_fetch "${url}" "${sha256}" "${folder}/${name}" >&2 || return 1
    # The folder's time records the last use, which pruning reads.
    touch "${folder}"
    printf '%s\n' "${folder}/${name}"
}

tool_cache_unpack() {
    local archive="$1" folder="$2" partial
    if [[ -f "${folder}/.complete" ]]; then
        return 0
    fi
    if [[ -e "${folder}" ]]; then
        chmod -R u+w -- "${folder}"
        rm -rf -- "${folder}"
    fi
    mkdir -p "${folder%/*}"
    partial="$(mktemp -d "${folder}.partial.XXXXXX")"
    printf 'Unpacking %s\n' "${archive##*/}"
    if ! unzip -q "${archive}" -d "${partial}"; then
        rm -rf -- "${partial}"
        return 1
    fi
    : > "${partial}/.complete"
    mv -- "${partial}" "${folder}"
    # Read-only, so no run can change what other runs share.
    chmod -R a-w -- "${folder}"
}

# The archive must already be verified against the SHA-256 given, as tool_cache_download does.
tool_cache_unzip() {
    local archive="$1" sha256="$2"
    local folder="${tool_cache_root}/unpacked/${sha256}"
    tool_cache_locked "${sha256}" tool_cache_unpack "${archive}" "${folder}" >&2 || return 1
    touch "${folder}"
    printf '%s\n' "${folder}"
}
