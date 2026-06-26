<!--
AI onboarding file.
Mode: bootstrap
Indexed commit: 972c10ec80f90a30a4fa80abfebc633af3365847
Last generated: 2026-06-26T11:30:00Z
Generator: generic high-end AI coding agent
Purpose: Help future AI sessions understand this repository quickly.
Audience: Any high-capability AI coding agent, regardless of vendor or model family.
Human edits are allowed. Future refreshes should preserve valid human edits.
-->

# Testing

## Test systems

| System | Purpose | Evidence |
|---|---|---|
| NUnit tests | C# unit tests in `OpenRA.Test`. | `OpenRA.Test/OpenRA.Test.csproj`, `Makefile` |
| MiniYAML checks | Validate official mod/content YAML. | `Makefile`, `make.ps1` |
| Lua syntax checks | Parse Lua scripts under mod map/script paths. | `Makefile`, `make.ps1`, CI |
| Code/style checks | Debug build with warnings as errors plus utility interface checks. | `Makefile`, `Directory.Build.props` |
| CI | Linux/Windows .NET 8 matrix. | `.github/workflows/ci.yml` |

## Run all relevant checks

Unix-like:

```sh
make check
make tests
make check-scripts
make TREAT_WARNINGS_AS_ERRORS=true test
```

Windows:

```powershell
./make.ps1 check
./make.ps1 tests
./make.ps1 check-scripts
$ENV:TREAT_WARNINGS_AS_ERRORS = "true"
./make.ps1 test
```

CI installs Lua 5.1 before script/mod checks.

## Unit tests

```sh
make tests
```

Equivalent commands from `Makefile`:

```sh
dotnet build OpenRA.Test/OpenRA.Test.csproj -c Debug --nologo -p:TargetPlatform=$(TARGETPLATFORM)
dotnet test bin/OpenRA.Test.dll --test-adapter-path:.
```

## Focused tests

Use focused NUnit filters only after inspecting actual test class/method names. A typical shape is:

```sh
dotnet test bin/OpenRA.Test.dll --test-adapter-path:. --filter "FullyQualifiedName~SomeTestName"
```

Verify filter syntax against the installed .NET/NUnit adapter if it fails.

## Mod YAML checks

```sh
make test
```

This builds first and then checks MiniYAML for `ts-content`, `ts`, `d2k-content`, `d2k`, `cnc-content`, `cnc`, `ra-content`, and `ra`.

## Lua checks

```sh
make check-scripts
```

This runs `luac -p` over Lua scripts in mod map and script paths. Use it when editing `mods/*/maps/**/*.lua` or `mods/*/scripts/*.lua`.

## Minimum validation by task

| Change type | Minimum expected validation |
|---|---|
| Documentation only | Link/JSON checks; no product tests required unless docs affect scripts. |
| C# engine/server/network | `make check` plus focused/unit tests where applicable. |
| Mod YAML/chrome/rules | `make test`; add `make check` if C# is touched. |
| Lua scripts | `make check-scripts`; add mod YAML checks if manifest/map YAML changed. |
| Packaging/install/workflows | Review affected scripts/workflows; run non-destructive local equivalents if available. |
| CI/build config | Run local equivalent of the changed CI path when feasible. |

## Flaky/slow tests

No specific flaky tests were identified in this bootstrap scan. Full mod validation can be slower because it builds and scans multiple official mod packages.

## Reporting

Always report exact commands run and results. If commands are skipped because the environment lacks .NET, Lua, native libraries, or build assets, say so explicitly.
