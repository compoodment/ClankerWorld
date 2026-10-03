#!/usr/bin/env bash
set -euo pipefail

# Export the first distributable client target using only the exact Godot .NET
# editor and export templates published for this project. The generated bundle
# is suitable for upload as a CI artifact, not for release distribution: it is
# deliberately unsigned.
readonly GODOT_VERSION="4.7.2"
readonly GODOT_RELEASE="${GODOT_VERSION}-stable"
readonly GODOT_TEMPLATE_VERSION="${GODOT_VERSION}.stable.mono"
readonly GODOT_ARCHIVE="Godot_v${GODOT_VERSION}-stable_mono_linux_x86_64.zip"
readonly GODOT_ARCHIVE_SHA256="129f82db7bafd54ae14bb5bb284041c73860e8c7a009a3a026ca5e946cbff247"
readonly GODOT_TEMPLATES_ARCHIVE="Godot_v${GODOT_VERSION}-stable_mono_export_templates.tpz"
readonly GODOT_TEMPLATES_SHA256="92f8681e349ef1f90891b792da95e3b2b0bd1ed610b78018c58feb2d87e15a9d"
readonly GODOT_RELEASE_URL="https://github.com/godotengine/godot/releases/download/${GODOT_RELEASE}"
readonly EXPORT_PRESET="Windows 11 x64"
readonly EXPORT_EXE="ClankerWorld.exe"
readonly CLIENT_ASSEMBLY="ClankerWorld.GodotClient.dll"

repo_root="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
project_dir="${repo_root}/src/ClankerWorld.GodotClient"
project_file="${project_dir}/ClankerWorld.GodotClient.csproj"
default_output_dir="${repo_root}/export/windows-x64"

usage() {
    printf 'Usage: %s [output-directory]\n' "${0##*/}" >&2
}

if (( $# > 1 )); then
    usage
    exit 2
fi

output_dir="${1:-${default_output_dir}}"
if [[ "${output_dir}" != /* ]]; then
    output_dir="${repo_root}/${output_dir}"
fi

require_command() {
    if ! command -v "$1" >/dev/null 2>&1; then
        printf 'Required command not found: %s\n' "$1" >&2
        exit 1
    fi
}

verify_sha256() {
    local expected="$1"
    local path="$2"
    local label="$3"
    local actual

    actual="$(sha256sum "${path}" | awk '{ print $1 }')"
    if [[ "${actual}" != "${expected}" ]]; then
        printf '%s SHA-256 mismatch\nexpected: %s\nactual:   %s\n' \
            "${label}" "${expected}" "${actual}" >&2
        return 1
    fi
}

download_verified() {
    local url="$1"
    local path="$2"
    local expected_sha256="$3"
    local label="$4"
    local temporary_path

    if [[ -f "${path}" ]]; then
        printf 'Checking cached %s SHA-256\n' "${label}"
        verify_sha256 "${expected_sha256}" "${path}" "${label}"
        return
    fi

    mkdir -p "$(dirname "${path}")"
    temporary_path="$(mktemp "${path}.partial.XXXXXX")"

    printf 'Fetching %s\n' "${label}"
    if ! curl --fail --location --retry 3 --retry-all-errors --silent --show-error \
        --output "${temporary_path}" "${url}"; then
        rm -f -- "${temporary_path}"
        return 1
    fi
    printf 'Checking downloaded %s SHA-256\n' "${label}"
    if ! verify_sha256 "${expected_sha256}" "${temporary_path}" "${label}"; then
        rm -f -- "${temporary_path}"
        return 1
    fi
    mv "${temporary_path}" "${path}"
}

for command in awk cp curl dotnet file find git grep sed sha256sum sort tar tr unzip; do
    require_command "${command}"
done

if [[ ! -f "${project_file}" ]]; then
    printf 'Godot client project file not found: %s\n' "${project_file}" >&2
    exit 1
fi

if [[ -e "${output_dir}" && ! -d "${output_dir}" ]]; then
    printf 'Export output path is not a directory: %s\n' "${output_dir}" >&2
    exit 1
fi

if [[ -d "${output_dir}" ]] && [[ -n "$(find "${output_dir}" -mindepth 1 -maxdepth 1 -print -quit)" ]]; then
    printf 'Refusing to overwrite non-empty export output directory: %s\n' "${output_dir}" >&2
    exit 1
fi
mkdir -p "${output_dir}"

cache_dir="${GODOT_DOWNLOAD_CACHE:-${XDG_CACHE_HOME:-${HOME}/.cache}/clankerworld-godot}"
editor_archive_path="${cache_dir}/${GODOT_ARCHIVE}"
templates_archive_path="${cache_dir}/${GODOT_TEMPLATES_ARCHIVE}"
scratch_parent="${RUNNER_TEMP:-/tmp}"
scratch_root="$(mktemp -d "${scratch_parent%/}/clankerworld-godot-windows-export.XXXXXX")"

cleanup() {
    rm -rf -- "${scratch_root}"
}
trap cleanup EXIT

download_verified "${GODOT_RELEASE_URL}/${GODOT_ARCHIVE}" "${editor_archive_path}" \
    "${GODOT_ARCHIVE_SHA256}" "Godot .NET editor archive"
download_verified "${GODOT_RELEASE_URL}/${GODOT_TEMPLATES_ARCHIVE}" "${templates_archive_path}" \
    "${GODOT_TEMPLATES_SHA256}" "Godot .NET export templates archive"

tool_root="${scratch_root}/tool"
export XDG_DATA_HOME="${scratch_root}/data"
template_unpack_root="${scratch_root}/templates"
staged_repo_root="${scratch_root}/repository"
staged_project_dir="${staged_repo_root}/src/ClankerWorld.GodotClient"
staged_project_file="${staged_project_dir}/ClankerWorld.GodotClient.csproj"
staged_solution_path="${staged_project_dir}/ClankerWorld.GodotClient.sln"
shared_placement_rules="${repo_root}/src/ClankerWorld.Shared/AgentPlacementRules.cs"
staged_shared_dir="${staged_repo_root}/src/ClankerWorld.Shared"

printf 'Extracting Godot .NET editor\n'
unzip -q "${editor_archive_path}" -d "${tool_root}"
godot_bin="$(find "${tool_root}" -type f -name "Godot_v${GODOT_VERSION}-stable_mono_linux.x86_64" -print -quit)"
if [[ -z "${godot_bin}" || ! -x "${godot_bin}" ]]; then
    printf 'Godot .NET editor executable was not found after extraction.\n' >&2
    exit 1
fi

printf 'Installing Godot .NET export templates\n'
unzip -q "${templates_archive_path}" -d "${template_unpack_root}"
template_version_file="$(find "${template_unpack_root}" -type f -name version.txt -print -quit)"
if [[ -z "${template_version_file}" ]]; then
    printf 'Godot export templates archive did not contain version.txt.\n' >&2
    exit 1
fi
template_version="$(tr -d '\r\n' < "${template_version_file}")"
if [[ "${template_version}" != "${GODOT_TEMPLATE_VERSION}" ]]; then
    printf 'Unexpected Godot .NET export template version: %s\n' "${template_version}" >&2
    exit 1
fi
template_root="${XDG_DATA_HOME}/godot/export_templates/${template_version}"
mkdir -p "$(dirname "${template_root}")"
mv "$(dirname "${template_version_file}")" "${template_root}"
if [[ ! -f "${template_root}/windows_release_x86_64.exe" ]]; then
    printf 'Windows x64 release template was not found after extraction.\n' >&2
    exit 1
fi

# The isolated stage has no .git directory. Keep its assembly identity tied to
# the source checkout, including when CI checks out a pull-request merge commit.
build_revision="$(git -C "${repo_root}" rev-parse HEAD)"
if [[ ! "${build_revision}" =~ ^[0-9a-f]{40}$ ]]; then
    printf 'Cannot determine the source commit for this export.\n' >&2
    exit 1
fi
build_version="$(dotnet msbuild "${project_file}" -nologo -getProperty:Version)"
file_version="$(dotnet msbuild "${project_file}" -nologo -target:GetAssemblyVersion -getProperty:FileVersion)"
if [[ ! "${build_version}" =~ ^[0-9A-Za-z.+-]+$ || ! "${file_version}" =~ ^[0-9]+\.[0-9]+\.[0-9]+\.[0-9]+$ ]]; then
    printf 'Cannot determine valid assembly version metadata for this export.\n' >&2
    exit 1
fi
build_line="Build ${build_version}+${build_revision:0:7}"
export SourceRevisionId="${build_revision}"

printf 'Staging Godot client source for an isolated export\n'
# Godot's self-contained win-x64 publish can otherwise update the source
# lockfile with a runtime-specific target graph.
mkdir -p "${staged_project_dir}"
for build_file in global.json Directory.Build.props; do
    if [[ ! -f "${repo_root}/${build_file}" ]]; then
        printf 'Required build configuration file not found: %s\n' "${repo_root}/${build_file}" >&2
        exit 1
    fi
    cp "${repo_root}/${build_file}" "${staged_repo_root}/${build_file}"
done
tar --exclude='./.godot' --exclude='./.godot/*' -C "${project_dir}" -cf - . | tar -C "${staged_project_dir}" -xf -
if [[ ! -f "${staged_project_file}" ]]; then
    printf 'Staged Godot client project file not found: %s\n' "${staged_project_file}" >&2
    exit 1
fi
if [[ ! -f "${shared_placement_rules}" ]]; then
    printf 'Shared agent placement rules not found: %s\n' "${shared_placement_rules}" >&2
    exit 1
fi
mkdir -p "${staged_shared_dir}"
cp "${shared_placement_rules}" "${staged_shared_dir}/AgentPlacementRules.cs"
cp "${repo_root}/src/ClankerWorld.Shared/BuildInformation.cs" "${staged_shared_dir}/BuildInformation.cs"
# Godot requires four numeric parts in the PE version fields. Its product name
# and our manifest carry the readable semantic version and source revision.
sed -i \
    -e "s/^application\/file_version=.*/application\/file_version=\"${file_version}\"/" \
    -e "s/^application\/product_version=.*/application\/product_version=\"${file_version}\"/" \
    -e "s/^application\/product_name=.*/application\/product_name=\"ClankerWorld ${build_line}\"/" \
    "${staged_project_dir}/export_presets.cfg"

printf 'Creating temporary Godot C# solution for export\n'
dotnet new sln --name "ClankerWorld.GodotClient" --output "${staged_project_dir}" --format sln
dotnet sln "${staged_solution_path}" add "${staged_project_file}"

export_exe_path="${output_dir}/${EXPORT_EXE}"
printf 'Exporting %s release bundle and publishing Godot C# scripts\n' "${EXPORT_PRESET}"
"${godot_bin}" --headless --path "${staged_project_dir}" --export-release "${EXPORT_PRESET}" "${export_exe_path}"

if [[ ! -f "${export_exe_path}" ]]; then
    printf 'Expected Windows executable was not exported: %s\n' "${export_exe_path}" >&2
    exit 1
fi
if [[ ! -f "${output_dir}/${EXPORT_EXE%.exe}.pck" ]]; then
    printf 'Expected Godot resource pack was not exported.\n' >&2
    exit 1
fi
client_assembly_path="$(find "${output_dir}" -type f -name "${CLIENT_ASSEMBLY}" -print -quit)"
if [[ -z "${client_assembly_path}" ]]; then
    printf 'Expected published Godot client assembly was not exported.\n' >&2
    exit 1
fi

if ! grep -a -F -q -- "${build_version}+${build_revision}" "${client_assembly_path}"; then
    printf 'The exported client assembly does not contain the expected version and source commit.\n' >&2
    exit 1
fi

pe_description="$(file -b "${export_exe_path}")"
if [[ "${pe_description}" != *"PE32+"* || "${pe_description}" != *"x86-64"* ]]; then
    printf 'Exported executable is not a Windows x64 PE file: %s\n' "${pe_description}" >&2
    exit 1
fi

manifest_path="${output_dir}/manifest.sha256"
printf 'Writing export manifest\n'
{
    printf '# ClankerWorld Windows 11 x64 release export\n'
    printf '# %s\n' "${build_line}"
    printf '# Source commit: %s\n' "${build_revision}"
    printf '# Windows file/product version: %s\n' "${file_version}"
    printf '# Godot editor: %s (%s)\n' "${GODOT_ARCHIVE}" "${GODOT_ARCHIVE_SHA256}"
    printf '# Godot .NET templates: %s (%s)\n' "${GODOT_TEMPLATES_ARCHIVE}" "${GODOT_TEMPLATES_SHA256}"
    printf '# Godot template version: %s\n' "${GODOT_TEMPLATE_VERSION}"
    printf '# Export preset: %s\n' "${EXPORT_PRESET}"
    (
        cd "${output_dir}"
        while IFS= read -r -d '' artifact; do
            sha256sum --binary "${artifact}"
        done < <(find . -type f ! -name "${manifest_path##*/}" -print0 | sort -z)
    )
} > "${manifest_path}"

(
    cd "${output_dir}"
    sha256sum --check --quiet --strict "${manifest_path##*/}"
)

printf 'Windows 11 x64 release export verified: %s\n' "${output_dir}"
