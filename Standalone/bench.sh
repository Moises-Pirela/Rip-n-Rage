#!/usr/bin/env bash
# Builds (if needed) and runs the physics / engine benchmark: ./bench.sh [--scenario crates|ragdolls|projectiles|ground|all] [--no-render] [--report bench.md] ...
set -e
cd "$(dirname "$0")"
source tools/env.sh
dotnet build -c Release --nologo -v q src/RipNRage.Benchmark/RipNRage.Benchmark.csproj
exec dotnet src/RipNRage.Benchmark/bin/Release/net10.0/RipNRageBenchmark.dll "$@"
