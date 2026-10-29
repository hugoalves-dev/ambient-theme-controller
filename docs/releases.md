# PoC builds and releases

The release workflow builds the current PoCs, not the finished Android/Windows applications. Each pull request from a branch in this repository publishes a GitHub prerelease tagged `v0.1.<run number>-alpha.pr<PR number>.r<attempt>`. A push to `main` publishes a stable GitHub release tagged `v0.1.<run number>`. The run number is specific to this workflow and provides a new patch version for each run; an existing stable tag is never moved or overwritten. Each release targets the actual source commit, and includes SHA-256 checksums.

Assets are `ambient-sensor-poc-android-arm64.apk` and `ambient-theme-poc-windows-x64.exe`. Windows is self-contained and single-file for x64. The Android release-mode APK currently uses the generated debug signing key; it is experimental and does not establish a production signing identity or support guaranteed upgrades across builds. Proper release signing remains a separate prerequisite for distribution.

GitHub normally restricts the token for pull requests from forks. Those PRs run build jobs but do not publish releases; publishing untrusted fork code with a write token is intentionally avoided. Builds from the main repository's PR branches and from `main` create actual GitHub Releases after both builds pass.
