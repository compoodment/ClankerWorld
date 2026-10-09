#!/usr/bin/env bash
# Compare native cached observations against this or a historical checkout.
set -euo pipefail
script_root=$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)
output_dir=${1:-"${TMPDIR:-/tmp}/clankerworld-cached-observations"}
variant=${2:-current}
target_root=${3:-"${script_root}/.."}
[[ "${variant}" =~ ^[a-zA-Z0-9_-]+$ ]] || { printf 'Use a simple variant name.\n' >&2; exit 2; }
target_root=$(cd "${target_root}" && pwd)
mkdir -p "${output_dir}"
output_dir=$(cd "${output_dir}" && pwd)
probe_dir=$(mktemp -d)
trap 'rm -rf "${probe_dir}"' EXIT
dotnet restore "${target_root}/src/ClankerWorld.Viewer/ClankerWorld.Viewer.csproj" --locked-mode >/dev/null
dotnet new console --name CachedObservationMeasurement --output "${probe_dir}" --framework net10.0 --no-restore >/dev/null
dotnet add "${probe_dir}/CachedObservationMeasurement.csproj" reference "${target_root}/src/ClankerWorld.Viewer/ClankerWorld.Viewer.csproj" >/dev/null
cp "${script_root}/measure-cached-observations.cs" "${probe_dir}/Program.cs"
cp "${probe_dir}/Program.cs" "${output_dir}/${variant}.probe.cs"
dotnet run --project "${probe_dir}/CachedObservationMeasurement.csproj" --configuration Release -- "${output_dir}" "${variant}"
