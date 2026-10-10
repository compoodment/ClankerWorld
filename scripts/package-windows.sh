#!/usr/bin/env bash
set -euo pipefail

# Builds the portable Windows 11 x64 package (#468): the Godot release export
# from scripts/verify-godot-windows-export.sh plus a self-contained win-x64 host
# from the same commit, in one folder named for the version, zipped.
#
#   bash scripts/verify-godot-windows-export.sh
#   bash scripts/package-windows.sh [export-dir] [output-dir]
#
# The game starts the host from its host/ folder and keeps the player's saves,
# keys and logs in %LOCALAPPDATA%\ClankerWorld, never in this folder.

readonly HOST_EXE="ClankerWorld.Viewer.exe"
readonly HOST_ASSEMBLY="ClankerWorld.Viewer.dll"

repo_root="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
export_dir="${1:-${repo_root}/export/windows-x64}"
output_dir="${2:-${repo_root}/export/package}"
for path_var in export_dir output_dir; do
    if [[ "${!path_var}" != /* ]]; then printf -v "${path_var}" '%s/%s' "${repo_root}" "${!path_var}"; fi
done

revision="$(git -C "${repo_root}" rev-parse HEAD)"
if [[ ! -f "${export_dir}/ClankerWorld.exe" || ! -f "${export_dir}/manifest.sha256" ]]; then
    printf 'No verified Godot export in %s. Run scripts/verify-godot-windows-export.sh first.\n' "${export_dir}" >&2
    exit 1
fi
# The game and host must come from the same verified source revision.
if ! grep -q -x -F "# Source commit: ${revision}" "${export_dir}/manifest.sha256"; then
    printf 'The Godot export in %s was not built from %s.\n' "${export_dir}" "${revision}" >&2
    exit 1
fi
(cd "${export_dir}" && sha256sum --check --quiet --strict manifest.sha256)

version="$(dotnet msbuild "${repo_root}/src/ClankerWorld.Viewer/ClankerWorld.Viewer.csproj" -nologo -getProperty:Version)"
if [[ ! "${version}" =~ ^[0-9]+\.[0-9]+\.[0-9]+(-[0-9A-Za-z.-]+)?$ ]]; then
    printf 'Cannot determine the game version: %s\n' "${version}" >&2
    exit 1
fi
package_name="ClankerWorld-${version}-windows-x64"
scratch_root="$(mktemp -d "${RUNNER_TEMP:-${TMPDIR:-/tmp}}/clankerworld-package.XXXXXX")"
trap 'rm -rf -- "${scratch_root}"' EXIT
stage="${scratch_root}/${package_name}"
mkdir -p "${stage}"

printf 'Copying the Godot export\n'
find "${export_dir}" -mindepth 1 -maxdepth 1 ! -name manifest.sha256 -exec cp -a {} "${stage}/" \;

printf 'Publishing the self-contained win-x64 host\n'
SourceRevisionId="${revision}" dotnet publish "${repo_root}/src/ClankerWorld.Viewer/ClankerWorld.Viewer.csproj" \
    --configuration Release --runtime win-x64 --self-contained -p:RestoreLockedMode=true \
    --output "${stage}/host" --nologo
if [[ ! -f "${stage}/host/${HOST_EXE}" ]]; then
    printf 'The host executable was not published.\n' >&2
    exit 1
fi
pe_description="$(file -b "${stage}/host/${HOST_EXE}")"
if [[ "${pe_description}" != *"PE32+"* || "${pe_description}" != *"x86-64"* ]]; then
    printf 'The host is not a Windows x64 PE file: %s\n' "${pe_description}" >&2
    exit 1
fi
if ! grep -a -F -q -- "${version}+${revision}" "${stage}/host/${HOST_ASSEMBLY}"; then
    printf 'The host assembly does not carry %s+%s.\n' "${version}" "${revision}" >&2
    exit 1
fi

cat > "${stage}/README.txt" <<EOF
ClankerWorld ${version} for Windows 11 x64
Source commit: ${revision}

Start ClankerWorld.exe. It starts your world server in the background and
stops it when you quit.

Your worlds, keys and logs are kept in %LOCALAPPDATA%\\ClankerWorld, not in this
folder, so you can replace this folder with a newer version without losing
them. Windows may warn that the publisher is unknown: this alpha is not signed.
EOF
sed -i 's/$/\r/' "${stage}/README.txt"

printf 'Writing the package manifest\n'
(
    cd "${stage}"
    {
        printf '# %s\n' "${package_name}"
        printf '# Source commit: %s\n' "${revision}"
        while IFS= read -r -d '' artifact; do
            sha256sum --binary "${artifact}"
        done < <(find . -type f ! -name manifest.sha256 -print0 | sort -z)
    } > manifest.sha256
    sha256sum --check --quiet --strict manifest.sha256
)

printf 'Zipping %s\n' "${package_name}"
mkdir -p "${output_dir}"
zip_path="${output_dir}/${package_name}.zip"
rm -f -- "${zip_path}" "${zip_path}.sha256"
python3 - "${scratch_root}" "${package_name}" "${zip_path}" <<'PY'
import os, sys, zipfile
root, name, destination = sys.argv[1:]
with zipfile.ZipFile(destination, "w", zipfile.ZIP_DEFLATED, compresslevel=9) as archive:
    for folder, directories, files in os.walk(os.path.join(root, name)):
        directories.sort()
        for file in sorted(files):
            path = os.path.join(folder, file)
            archive.write(path, os.path.relpath(path, root))
PY
(cd "${output_dir}" && sha256sum --binary "${package_name}.zip" > "${package_name}.zip.sha256")

printf 'Windows package ready: %s (%s)\n' "${zip_path}" "$(du -h "${zip_path}" | cut -f1)"
