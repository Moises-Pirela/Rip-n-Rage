# CI and prepared environment

The GitHub Actions workflow at `.github/workflows/ci.yml` builds the full standalone solution with .NET 10 on Ubuntu 24.04, Windows Server 2022 and macOS 15. The build explicitly enables built-in .NET analyzers (recommended rules; existing diagnostic warnings remain nonfatal). A framework-free assertion executable exercises bounded `Mathf` behavior on each host.

## SDK and build

- **SDK:** .NET 10 (`net10.0`) with `actions/setup-dotnet@v4`, `10.0.x`; no third-party NuGet packages are required for the normal solution build.
- **Manual:** from `Standalone/`, run `dotnet restore RipNRage.sln`, `dotnet build RipNRage.sln -c Release`, then `dotnet run --project tests/RipNRage.EngineChecks/RipNRage.EngineChecks.csproj -c Release`.
- **Native library environment:** bundled per-RID GLFW and OpenAL Soft are copied by `Directory.Build.targets`. Do **not** delete the per-platform native libraries or notices. See `native/NOTICE.md`, `native/licenses/GLFW-LICENSE.md` and `native/licenses/OpenAL-Soft-COPYING.txt` for provenance, redistribution obligations and exact bundled versions. macOS OpenAL uses Homebrew `openal-soft` or the platform framework; sound is optional. Native binaries are *not* rebuilt or downloaded in CI.
- **Linux graphics smoke test:** Ubuntu 24.04, Xvfb, Mesa software OpenGL, X11 libraries, and `LIBGL_ALWAYS_SOFTWARE=1`. This tests gameplay without physical GPU hardware; `--gpu software` deliberately permits llvmpipe. It exercises 620 frames at fixed dt, so the scripted invariant at frame 600 is reached.

## Strafe regression

From `Standalone/` on a machine with OpenGL and the appropriate native dependencies:

```sh
dotnet run -c Release --project src/RipNRage.Game -- --script strafe --frames 620 --fixed-dt 0.016667 --mute --gpu software --no-vsync --width 640 --height 360 --no-postfx
```

The 600-frame run must complete, report no fall, no stuck rewind and no player death. A failed invariant, or early termination before evaluation, now returns exit code 1. An exception still returns the existing nonzero fatal exit code. In Linux CI use `xvfb-run -a` before `dotnet run`.

## Limitations

CI's software-rasterized test does not validate GPU driver performance, actual audio devices, or self-contained publish for every target runtime. Bundled Linux OpenAL Soft requires glibc 2.38 or newer (Ubuntu 24.04 works); older systems may fall back to installed libraries or silence. Native notices are verified to exist but this is not a substitute for a licensing audit.
