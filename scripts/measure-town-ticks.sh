#!/usr/bin/env bash
# Run the portable native Town probe against this or a historical checkout.
set -euo pipefail
script_root=$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)
output_dir=${1:-"${TMPDIR:-/tmp}/clankerworld-town-timings"}
variant=${2:-current}
target_root=${3:-"${script_root}/.."}
cases=${4:-4:96,4:192,4:384,8:96,8:192,8:384,16:96,16:192,16:384}
start_tick=${5:-256}
samples=${6:-64}
profile_gate=${7:-}
if [[ -n "${profile_gate}" && ( -e "${profile_gate}.ready" || -e "${profile_gate}.go" ) ]]; then
    printf 'Use a fresh profile gate prefix; existing markers would skip the sampling pause.\n' >&2
    exit 2
fi
[[ "${variant}" =~ ^[a-zA-Z0-9_-]+$ ]] || { printf 'Use a simple variant name.\n' >&2; exit 2; }
target_root=$(cd "${target_root}" && pwd)
mkdir -p "${output_dir}"
output_dir=$(cd "${output_dir}" && pwd)
probe_dir=$(mktemp -d)
trap 'rm -rf "${probe_dir}"' EXIT
dotnet new console --name TownTickMeasurement --output "${probe_dir}" --framework net10.0 --no-restore >/dev/null
dotnet add "${probe_dir}/TownTickMeasurement.csproj" reference "${target_root}/src/ClankerWorld.Simulation/ClankerWorld.Simulation.csproj" >/dev/null
cp "${script_root}/measure-town-ticks.cs" "${probe_dir}/Program.cs"
cp "${probe_dir}/Program.cs" "${output_dir}/${variant}.probe.cs"
commit=$(git -C "${target_root}" rev-parse HEAD)
dotnet run --project "${probe_dir}/TownTickMeasurement.csproj" --configuration Release -- \
    "${output_dir}/${variant}.json" "${commit}" "${cases}" "${start_tick}" "${samples}" \
    "${output_dir}/${variant}.probe.cs" "${profile_gate}"
