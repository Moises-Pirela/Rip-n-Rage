# source this file: picks a .NET 10+ SDK and exports sane defaults (Linux sandbox VM, macOS, any desktop)
_here="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"

# true when the given dotnet has an SDK of major version 10 or newer
_rnr_has_sdk() { [ -x "$1" ] && "$1" --list-sdks 2>/dev/null | awk -F. '$1+0 >= 10 { found=1 } END { exit !found }'; }

# Order: the private ./.dotnet, the dotnet on PATH, then the usual per-user / system install locations. A dotnet on
# PATH that only has older SDKs (e.g. a 6.0 in /usr/local/share/dotnet on macOS) is skipped instead of failing the build.
_rnr_dotnet=""
for _c in "$_here/.dotnet/dotnet" "$(command -v dotnet 2>/dev/null)" "$HOME/.dotnet/dotnet" \
          /usr/local/share/dotnet/dotnet /opt/homebrew/opt/dotnet/libexec/dotnet /usr/share/dotnet/dotnet /usr/lib/dotnet/dotnet; do
  [ -n "$_c" ] && _rnr_has_sdk "$_c" && { _rnr_dotnet="$_c"; break; }
done
if [ -z "$_rnr_dotnet" ]; then
  echo "No .NET 10 SDK found; installing a private one into $_here/.dotnet" >&2
  "$_here/tools/setup-dotnet.sh" >&2
  _rnr_dotnet="$_here/.dotnet/dotnet"
fi
# resolve symlinks (Homebrew / /usr/local/bin) so DOTNET_ROOT points at the real install
_rnr_real="$(cd "$(dirname "$_rnr_dotnet")" && pwd -P)/$(basename "$_rnr_dotnet")"
[ -L "$_rnr_dotnet" ] && _rnr_real="$(readlink -f "$_rnr_dotnet" 2>/dev/null || python3 -c 'import os,sys;print(os.path.realpath(sys.argv[1]))' "$_rnr_dotnet")"
export DOTNET_ROOT="$(dirname "$_rnr_real")"
export PATH="$DOTNET_ROOT:$PATH"
unset _c _rnr_dotnet _rnr_real

export DOTNET_CLI_TELEMETRY_OPTOUT=1 DOTNET_NOLOGO=1 DOTNET_SKIP_FIRST_TIME_EXPERIENCE=1
if [ -x "$_here/.dotnet/dotnet" ] && [ "$DOTNET_ROOT" = "$_here/.dotnet" ]; then
  export DOTNET_CLI_HOME="${DOTNET_CLI_HOME:-$_here/.dotnet/home}"
  export NUGET_PACKAGES="$_here/.dotnet/nuget"
fi
# Headless VMs: default to the Xvfb display :1 only when that server actually exists. Never force it on a
# desktop — a wrong DISPLAY lands you on a virtual X server with the llvmpipe *software* renderer.
if [ "$(uname -s)" = Linux ] && [ -z "${DISPLAY:-}" ] && [ -z "${WAYLAND_DISPLAY:-}" ] && [ -S /tmp/.X11-unix/X1 ]; then export DISPLAY=:1; fi
