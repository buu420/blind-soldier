# X64 Bootstrap Module Readiness Design

## Problem

Blind Soldier 0.1.6 can terminate a newly launched Final Fantasy VII 2026 process with bootstrap exit code 17. The failure log ends with `Remote module not found: KERNEL32.DLL` immediately after the primary thread is resumed.

Local logs reproduce the same intermittent sequence: two launches failed at the module lookup, while later launches with the identical executable and package succeeded. Ghidra analysis of the shipped x64 bootstrap (SHA-256 `D03BB5AC596C7CD00EF7C97921E4814FA2CF18E67895C6F65334969EECCB9BC0`) confirms that it retries only when `CreateToolhelp32Snapshot` fails. If snapshot creation succeeds while the target loader table is still incomplete, it enumerates that single snapshot and fails immediately when the required module is absent.

Microsoft documents that module snapshots may fail or return incorrect information while a target process loader table is not initialized or its module list is changing. The target is already resumed as required, so the remaining defect is the one-shot enumeration.

## Selected Design

Replace the one-shot module lookup with a bounded readiness wait:

- Accept the target process handle as well as its process ID.
- Create and enumerate a fresh module snapshot on every attempt.
- Compare module names case-insensitively, preserving the existing behavior.
- Retry snapshot errors `ERROR_BAD_LENGTH` and `ERROR_PARTIAL_COPY`.
- Also retry when a valid snapshot does not yet contain the requested module.
- Sleep 10 milliseconds between attempts for no more than 5 seconds.
- Stop immediately if the target process exits.
- Close every snapshot handle before retrying or returning.
- Emit one final diagnostic on target exit, a non-retryable snapshot failure, or timeout.

`ResolveRemoteLoadLibraryW` will pass the existing process handle into this readiness wait. Injection, payload validation, runtime selection, and fail-closed behavior remain unchanged.

## Alternatives Considered

1. Add a fixed delay before injection. This is smaller but makes startup slower on every machine and remains vulnerable to slower systems.
2. Replace the injector with a lower-level loader or manual mapping. This is substantially riskier and unnecessary because the existing injection succeeds once the target module list is ready.

The bounded fresh-snapshot retry is the smallest change that directly addresses the documented failure mode.

## Test Design

Add a real native behavior test rather than a source-text assertion:

- Start a child copy of the bootstrap test executable.
- Keep a system DLL unloaded until the parent signals an event.
- Begin waiting for that DLL in the child process from the parent.
- Signal the child to load the DLL after the lookup has begun.
- Assert that the lookup survives the initially missing module and returns its remote base before the timeout.

The test must fail against the current one-shot implementation before production code changes. Both Win32 and x64 bootstrap behavior executables must then pass, followed by the native source contract, bootstrap builds, portable-package validation, and release checks.

## Release

Publish the corrected packages as Blind Soldier 0.1.7 on the beta channel for both the 2013 and 2026 mod-manager entries. Package URLs and hashes must correspond to newly built archives. The 2026 package must contain the corrected x64 bootstrap; the 2013 package must retain its architecture-correct x86 bootstrap and all existing self-contained runtime files.
