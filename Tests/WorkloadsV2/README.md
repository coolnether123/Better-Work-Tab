# Workloads V2 deterministic tests

This is a standalone, non-shipping .NET Framework test executable. It links
the pure Workloads V2 model, typed state/diff/session/persistence/converter
sources, projection contracts/providers, and the production MP transaction
protocol directly. The Scribe and Multiplayer API surfaces are minimal
test-only stubs; no RimWorld assembly is referenced.

Build and run from this directory with:

```powershell
powershell.exe -NoLogo -NoProfile -ExecutionPolicy Bypass -Command "& msbuild .\WorkloadsV2.Deterministic.csproj /t:Rebuild /p:Configuration=Release /v:minimal"
.\bin\Release\BetterWorkTab.WorkloadsV2.Deterministic.exe
```

The project is outside `Source/`, has no project reference from
`Source/Better Work Tab.csproj`, and has no package/output copy target. Its
`bin/` and `obj/` directories are ignored locally by the project-level test
runner convention and must not be copied into a mod package.
