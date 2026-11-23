# Builds and releases

The workflow `.github/workflows/release.yml` builds and tests the Android app and the Windows solution, then
publishes a GitHub Release automatically:

| Event | What happens |
|---|---|
| Pull request into `dev` or `main` | Build and test only. Outputs are available as workflow artifacts. |
| Push to `dev` (a commit or a merged pull request) | Pre-release tagged `v0.1.<run number>-dev`. |
| Push to `main` (a commit or a merged pull request) | Release tagged `v0.1.<run number>`, marked as **latest**. |
| Manual run (Actions → *Build, test and release* → *Run workflow*) on `dev` or `main` | Same as a push to that branch. |

Changes that touch only documentation (`docs/**`, any `*.md`, `LICENSE*`, `NOTICE`) do not start the workflow.
A push that mixes documentation with code or workflow files does.

Every release targets the commit it was built from, includes SHA-256 checksums and generated release notes. The
run number is shared by both branches, so `dev` and `main` tags never collide. An existing tag is never moved or
overwritten: re-running a workflow whose release already exists publishes nothing.

Assets are experimental, not the finished applications. The Windows executable is self-contained single-file
x64. The Android release-mode APK currently uses the generated debug signing key; it does not establish a
production signing identity or support guaranteed upgrades across builds. Proper release signing remains a
separate prerequisite for distribution (spec §23).
