#!/bin/bash
set -euo pipefail
umask 077

# Finder starts .command files outside the checkout; always build from this file's folder.
project_root="$(cd "$(dirname "$0")" && pwd)"
cd "$project_root"

mode="${1:-run}"
case "$mode" in
    run|--check|--headless|--install) ;;
    *) printf 'Usage: %s [--check | --headless | --install]\n' "$0"; exit 2 ;;
esac
if [[ $# -gt 1 ]]; then
    printf 'Use only one option.\n'
    exit 2
fi

unset VISOLOOM_REFERENCE_TOKEN
unset VISOLOOM_REFERENCE_AUTOCONNECT
server_pid=""
pending_update=""
cleanup() {
    local result=$?
    trap - EXIT
    if [[ -n "$server_pid" ]]; then
        kill "$server_pid" 2>/dev/null || true
        wait "$server_pid" 2>/dev/null || true
    fi
    if [[ -n "$pending_update" && -f "$pending_update" ]]; then
        unlink "$pending_update" || true
    fi
    unset VISOLOOM_REFERENCE_TOKEN
    unset VISOLOOM_REFERENCE_AUTOCONNECT
    if [[ $result -ne 0 && -t 0 ]]; then
        printf '\nSetup stopped. Read the message above, then press Return to close.\n'
        read -r || true
    fi
    exit "$result"
}
trap cleanup EXIT
trap 'printf "\nStopping the local test service…\n"; exit 0' INT TERM HUP

if [[ -n "${VISOLOOM_DOTNET:-}" ]]; then
    dotnet_path="$VISOLOOM_DOTNET"
elif [[ -x "$HOME/.local/share/visoloom-dev/dotnet/dotnet" ]]; then
    dotnet_path="$HOME/.local/share/visoloom-dev/dotnet/dotnet"
elif command -v dotnet >/dev/null 2>&1; then
    dotnet_path="$(command -v dotnet)"
else
    printf 'Install the .NET SDK specified in global.json. See DEVELOPMENT.md.\n'
    exit 1
fi

if [[ "$mode" != --check ]]; then
    if [[ "$(uname -s)" != Darwin || ! -d '/Applications/Rhino 8.app' ]]; then
        printf 'This launcher needs Rhino 8 in Applications on macOS. See DEVELOPMENT.md for Windows.\n'
        exit 1
    fi
    if [[ "$mode" != --install ]] && /usr/sbin/lsof -nP -iTCP:5179 -sTCP:LISTEN >/dev/null 2>&1; then
        printf 'The local test port is already in use. Stop the previous test service, then try again.\n'
        exit 1
    fi
fi

printf '\nBuilding VisoLoom from the current code…\n'
"$dotnet_path" restore VisoLoom.sln --locked-mode
"$dotnet_path" build VisoLoom.sln --configuration Release --no-restore

if [[ "$mode" == --check ]]; then
    "$dotnet_path" test VisoLoom.sln --configuration Release --no-build
    printf '\nBuild and automated checks passed.\n'
    exit 0
fi

build_folder="$project_root/src/VisoLoom.Plugin/bin/Release/net7.0"
for file in "$build_folder"/RhinoCommon.* "$build_folder"/Rhino.UI.* "$build_folder"/Eto.* \
    "$build_folder"/Ed.Eto.* "$build_folder"/System.Drawing.Common.* "$build_folder"/Microsoft.Win32.SystemEvents.*; do
    if [[ -e "$file" ]]; then
        printf 'A Rhino runtime assembly was found in the build output. Stop and inspect the build.\n'
        exit 1
    fi
done

# Mac RhinoCommon plug-ins live inside a same-named .rhp folder, with dependencies beside the assembly.
plugin_folder="$HOME/Library/Application Support/McNeel/Rhinoceros/8.0/MacPlugIns/VisoLoom-dev/VisoLoom.rhp"
if [[ -e "$plugin_folder" && ! -f "$plugin_folder/.visoloom-development" ]]; then
    printf 'The development plug-in folder already contains another installation. See DEVELOPMENT.md.\n'
    exit 1
fi
mkdir -p "$plugin_folder"
chmod 700 "$plugin_folder"
touch "$plugin_folder/.visoloom-development"
for name in VisoLoom.rhp VisoLoom.deps.json VisoLoom.runtimeconfig.json \
    VisoLoom.Core.dll VisoLoom.Protocol.dll VisoLoom.Providers.dll; do
    # Replace the file rather than modifying an assembly a running Rhino may have mapped.
    pending_update="$(mktemp "$plugin_folder/.update.XXXXXX")"
    cp "$build_folder/$name" "$pending_update"
    mv "$pending_update" "$plugin_folder/$name"
    pending_update=""
done

if [[ "$mode" == --install ]]; then
    printf '\nLatest code installed for Rhino 8. Restart any running Rhino session to load it.\n'
    exit 0
fi

# The session token is never written into a file or passed as a command-line argument.
export VISOLOOM_REFERENCE_TOKEN="$(openssl rand -hex 32)"
export VISOLOOM_REFERENCE_AUTOCONNECT=1
export VISOLOOM_REFERENCE_STORAGE="$HOME/Library/Application Support/VisoLoom/development-server"
if [[ -f "$HOME/.local/share/VisoLoom/engine-prototype/connection.json" ]]; then
    export VISOLOOM_ENGINE_CONFIG="$HOME/.local/share/VisoLoom/engine-prototype/connection.json"
fi
"$dotnet_path" "$project_root/server/VisoLoom.ReferenceServer/bin/Release/net10.0/VisoLoom.ReferenceServer.dll" &
server_pid=$!

ready=false
for ((attempt = 0; attempt < 50; attempt++)); do
    if ! kill -0 "$server_pid" 2>/dev/null; then
        printf 'The local service stopped during startup. Check its message above.\n'
        exit 1
    fi
    if printf 'header = "Authorization: Bearer %s"\n' "$VISOLOOM_REFERENCE_TOKEN" | \
        curl --config - --noproxy '*' --fail --silent --output /dev/null --max-time 1 \
            http://127.0.0.1:5179/api/v1/capabilities; then
        ready=true
        break
    fi
    sleep 0.2
done
if [[ "$ready" != true ]]; then
    printf 'The local service did not become ready. Stop other test services and try again.\n'
    exit 1
fi

# The service retains its own copy; apps opened afterward do not need the token.
unset VISOLOOM_REFERENCE_TOKEN VISOLOOM_REFERENCE_AUTOCONNECT

printf '\nVisoLoom is ready for local testing.\n'
printf 'Latest code installed for Rhino 8. Restart Rhino to load a changed build.\n'
printf 'Capture testing returns your image unchanged. Prepared local models are checked separately at startup.\n'
printf 'For local FLUX: open VisoLoom Settings, check the local model, then choose it in the Model menu.\n'
if [[ "$mode" == run ]]; then
    printf 'In Rhino: run VisoLoom. The local test service connects automatically.\n'
    printf 'Use Test capture, compare both images, then try Save PNG….\n'
    open -a '/Applications/Rhino 8.app'
fi
printf '\nKeep this window open while testing. Press Control-C to stop the service.\n'
wait "$server_pid"
