# Validation

Checked on 2026-10-22 on Windows with .NET Framework 4.8 or newer installed (registry release 533509). The source builds with the Windows .NET Framework C# compiler in C# 5 mode, with warnings treated as errors.

## Automated results

- `tests/Run-Tests.ps1`: **13 passed, 0 failed**. Known SHA-256 vectors for empty input and `abc`; a randomized file spanning multiple buffers checked against an independent direct .NET calculation; Unicode filenames; unchanged source bytes and timestamps; final/bounded progress; locked and missing files; cancellation before and during reading; strict expected-hash validation; comparison; checksum-manifest formatting.
- `tests/Run-UiTests.ps1`: **11 passed, 0 failed**. Runs the actual Windows Forms event loop offscreen. Covers async hashing, match/mismatch/invalid/empty comparison values, English/Thai switching, error recovery, cancellation and recovery, default/minimum layout bounds, UTF-8 export without BOM, replacing a chosen unrelated output, and source protection for both the same path and an NTFS hard-link alias.
- `Build.ps1`: release executable built successfully. No NuGet or third-party runtime libraries are used.

The English and Thai images in `docs/images/` are programmatic `DrawToBitmap` renders of the actual form using an `abc` sample. The preview path is shown as `example.txt` to avoid publishing a local machine path. They are not desktop screenshots.

## Reproduce

```powershell
.\Build.ps1
.\tests\Run-Tests.ps1
.\tests\Run-UiTests.ps1
.\Package.ps1
```

The tests create temporary fixtures only under `tests/.tmp`, then clean up their own unique run directory. Generated test executables are under `tests/.artifacts`; both locations are ignored by Git.

## Limits

The native Browse/Save dialogs, physical drag-and-drop, system clipboard actions, real multi-monitor DPI changes, and every Windows 10/11 configuration have not been interactively tested. Offscreen layout checks do not establish those behaviors. Hashing is cancellable between file reads; slow devices or network paths may delay a pending read.

The exported checksum records the file at the time hashing completed. If the file changes later, calculate its checksum again before relying on or sharing the old result. Matching checksums establish equality against the supplied expected value; they do not establish publisher identity or software safety.
