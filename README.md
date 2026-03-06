# de4dot-cex-babel

Fork of de4dot CEx with native Babel Obfuscator integration (single-step workflow).

## What was added

- Native Babel detection integrated in de4dot pipeline
- Babel version reporting in detection/verbose logs
- Legacy compatibility cleanup pass for Babel-protected assemblies
- Delegate wrapper cleanup ported from legacy Babel tooling patterns
- Babel VM/delegate/runtime cleanup orchestration inside de4dot module flow
- Better runtime dependency hints when cleanup is partial
- Regression helper scripts for Babel samples

## Current Babel workflow

Before:
1. `Babel-DeobfuscatorNET4`
2. `de4dot CEx`

Now:
1. `de4dot.exe test_babel.dll`

## Build

Recommended full build:

```powershell
C:\Windows\Microsoft.NET\Framework\v4.0.30319\MSBuild.exe de4dot.sln /t:Build /p:Configuration=Release /p:Platform="Mixed Platforms" /p:CscToolPath=C:\BuildTools\MSBuild\Current\Bin\Roslyn /p:CscToolExe=csc.exe
```

Scripted core build:

```powershell
powershell -ExecutionPolicy Bypass -File .\scripts\build-release.ps1 -Clean
```

## Test / regression

Run deobfuscation:

```powershell
.\Release\de4dot.exe -f .\test_file_v2\test_babel_old_vers.dll -v
```

Run batch regression on `test_file*` directories:

```powershell
powershell -ExecutionPolicy Bypass -File .\scripts\babel-regression.ps1
```

Outputs include detection score, detected Babel version, cleanup exit code, unresolved delegates, missing dependencies, and output hash.

## Notes

- Some Babel samples require referenced runtime DLLs near target assembly for full delegate/VM cleanup.
- If verbose logs show unresolved delegate wrappers, add missing dependencies and rerun.
- This project may invoke methods during deobfuscation; use an isolated VM/sandbox for untrusted samples.

## Troubleshooting (Babel)

If you see logs like:

```text
[!] Babel runtime dependency hint: missing referenced assemblies may reduce delegate/VM cleanup
[!] Missing candidates: Newtonsoft.Json, WindowsBase
...
[!] Babel delegate cleanup incomplete: 1 unresolved wrappers remain
[!] Missing runtime dependencies observed: Newtonsoft.Json
```

it means de4dot cleaned most Babel protections, but at least one delegate wrapper could not be resolved because required runtime dependencies were missing.

Recommended fix:

1. Place missing DLLs (for example `Newtonsoft.Json.dll`) next to the target assembly.
2. Rerun de4dot with `-v`.
3. Confirm the final pass reports `candidates=0` and no `unresolved wrappers remain`.

Note:

- `WindowsBase` may appear as a candidate in some environments because of framework references.
- It is often informational only; the important line is `Missing runtime dependencies observed: ...`.

## Credits / Thanks

- Original de4dot by `0xd4d`
- de4dot CEx base work by `ViRb3`
- Babel reverse-engineering and legacy patterns inspired/adapted from `Babel-DeobfuscatorNET4`
- Special thanks to **CodeExplorer** for shared code and research direction used during Babel integration

## License

This project follows de4dot licensing (GPLv3). Keep original notices and attribution when redistributing.

## References

- Original de4dot README: [README-orig.md](README-orig.md)
