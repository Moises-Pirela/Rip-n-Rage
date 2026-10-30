# source this file: exports the private .NET toolchain and sane defaults for the sandbox VM
_here="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
if [ -x "$_here/.dotnet/dotnet" ]; then
  export DOTNET_ROOT="$_here/.dotnet"
  export PATH="$DOTNET_ROOT:$PATH"
fi
export DOTNET_CLI_TELEMETRY_OPTOUT=1 DOTNET_NOLOGO=1 DOTNET_SKIP_FIRST_TIME_EXPERIENCE=1 DOTNET_CLI_HOME="${DOTNET_CLI_HOME:-$_here/.dotnet/home}"
export NUGET_PACKAGES="$_here/.dotnet/nuget"
# Headless VMs: default to the Xvfb display :1 only when that server actually exists. Never force it on a
# desktop — a wrong DISPLAY lands you on a virtual X server with the llvmpipe *software* renderer.
if [ -z "${DISPLAY:-}" ] && [ -z "${WAYLAND_DISPLAY:-}" ] && [ -S /tmp/.X11-unix/X1 ]; then export DISPLAY=:1; fi
