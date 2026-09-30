#!/usr/bin/env bash
set -euo pipefail

# Move the entries waiting in changes/ into the Unreleased section of
# CHANGELOG.md, newest first, and delete their files. Each pull request adds its
# own file instead of editing CHANGELOG.md, so parallel pull requests do not
# conflict. Run this when preparing a release, or whenever the changelog should
# catch up, and commit the result.

repo_root="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
cd "${repo_root}"

changelog="CHANGELOG.md"
heading="## Unreleased"

if [[ "$(grep -c "^${heading}\$" "${changelog}")" != "1" ]]; then
    printf '%s must contain exactly one "%s" heading.\n' "${changelog}" "${heading}" >&2
    exit 1
fi

# Order files by when they reached the repository, newest first. A file that is
# not committed yet counts as newest.
entries=()
while IFS= read -r path; do
    [[ "$(basename "${path}")" == "README.md" ]] && continue
    added="$(git log --diff-filter=A --format=%ct -1 -- "${path}" 2>/dev/null || true)"
    entries+=("${added:-9999999999} ${path}")
done < <(find changes -maxdepth 1 -type f -name '*.md' 2>/dev/null | LC_ALL=C sort)

if [[ "${#entries[@]}" -eq 0 ]]; then
    echo "No change entries to collect."
    exit 0
fi

mapfile -t files < <(printf '%s\n' "${entries[@]}" | LC_ALL=C sort -k1,1nr -k2 | cut -d' ' -f2-)

# git rm refuses changed tracked files. Check every entry before writing the
# changelog so a refusal cannot leave copied entries waiting to be copied again.
for path in "${files[@]}"; do
    if git ls-files --error-unmatch "${path}" >/dev/null 2>&1 &&
        { ! git diff --quiet -- "${path}" || ! git diff --cached --quiet -- "${path}"; }; then
        printf 'Commit changes to %s before collecting entries.\n' "${path}" >&2
        exit 1
    fi
done

block="$(mktemp)"
trap 'rm -f "${block}"' EXIT
for path in "${files[@]}"; do
    # Drop Windows line endings and blank lines at the start and end; keep each
    # entry's own wording.
    text="$(tr -d '\r' <"${path}" | sed -e '/./,$!d')"
    if [[ "${text}" != "- "* ]]; then
        printf '%s must start with a "- " bullet.\n' "${path}" >&2
        exit 1
    fi
    printf '%s\n' "${text}" >>"${block}"
done

# Insert the block, set off by blank lines, straight after the heading.
updated="$(mktemp)"
awk -v heading="${heading}" -v block="${block}" '
    { print }
    $0 == heading {
        print ""
        while ((getline line < block) > 0) print line
        print ""
        if ((getline next_line) > 0 && next_line != "") print next_line
    }
' "${changelog}" >"${updated}"
mv "${updated}" "${changelog}"

for path in "${files[@]}"; do
    if git ls-files --error-unmatch "${path}" >/dev/null 2>&1; then
        git rm -q "${path}"
    else
        rm "${path}"
    fi
done

printf 'Moved %d change entries into %s.\n' "${#files[@]}" "${changelog}"
