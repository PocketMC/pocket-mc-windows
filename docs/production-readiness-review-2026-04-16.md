# PocketMC Desktop Production Readiness Review (2026-04-16)

## Scope

This review is based on repository inspection of runtime composition, exception handling, diagnostics, CI/CD workflow configuration, and test/project setup.

## Executive Verdict

**Status: Conditionally ready (not fully production-ready yet).**

The app has strong foundations (global crash handling, dependency-injected architecture, path-safety tests, updater integration), but there are release-readiness risks that should be closed before broad production rollout.

---

## Strengths

1. **Global unhandled exception capture and crash report persistence exists.**
   - App wires UI thread, AppDomain, and unobserved task exception handlers and writes crash logs to `%LocalAppData%/PocketMC/logs`.
2. **Security hardening appears intentional in file handling and backups.**
   - Tests cover path traversal and ZIP extraction safety.
   - Diagnostics redact `rcon.password` in exported server properties.
3. **Auto-update flow has lifecycle controls.**
   - Update checks are semaphore-guarded to avoid concurrent update cycles.

---

## Production Blockers / High-Priority Risks

### 1) CI workflow scope should be explicitly tied to your canonical release branch

- Workflow `production-build.yml` currently triggers on `master` and `New-Ci-Pipeline`.
- This is correct **if `master` is your canonical release branch**. If your team releases from `main` or tags, the trigger should be updated accordingly.

**Risk:** If repository release practices drift from these configured branch triggers, production packaging may silently stop running for expected release pushes.

**Recommendation:**
- Keep `master` trigger if that is the intended release branch.
- If releases are cut from `main` or tags, add those triggers explicitly (for example semver tags like `v*`).
- Split CI (build/test) from release (pack/publish) if needed.

### 2) Local observability gap for production support

- App logging is configured with `AddDebug()` only.

**Risk:** Debug provider is often insufficient for post-mortem production support outside dev tooling; field diagnostics depend mostly on crash files and ad hoc bundle generation.

**Recommendation:**
- Add structured file logging (rolling files under `%LocalAppData%/PocketMC/logs`) and include correlation IDs per server instance/session.

### 3) Unimplemented converter reverse path can crash if binding mode changes

- `MinecraftMotdConverter.ConvertBack` throws `NotImplementedException`.

**Risk:** If a two-way binding or refactor accidentally exercises `ConvertBack`, the settings UX can fail at runtime.

**Recommendation:**
- Return `Binding.DoNothing` for unsupported convert-back scenarios or explicitly enforce one-way binding in XAML where used.

---

## Medium-Priority Improvements

1. **Dependency/version maintenance policy not enforced in CI**
   - No automated vulnerability scan / package audit gate in workflow.
2. **Coverage signal is weak in pipeline**
   - Tests run, but there is no minimum coverage threshold or report publishing gate.
3. **Release process coupling**
   - Build, test, versioning, packing, and artifact upload all happen in one workflow/job; failures can reduce diagnosability.

---

## Suggested Go-Live Checklist

Before broad production rollout, complete:

- [ ] Confirm workflow triggers match your real release source (`master` vs `main` and/or release tags).
- [ ] Add persistent file logging for release builds.
- [ ] Remove runtime `NotImplementedException` from value converters in UI paths.
- [ ] Add dependency vulnerability audit in CI.
- [ ] Publish test + coverage artifacts and define a minimum quality gate.
- [ ] Perform one clean-room install/update rollback validation on a fresh Windows 10 and Windows 11 machine.

---

## Overall Assessment

PocketMC is **close** to production-grade for controlled rollout, especially for technically experienced users. For a broad public release, the CI trigger alignment and production observability items should be addressed first to reduce operational risk.
