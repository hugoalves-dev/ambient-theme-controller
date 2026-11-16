# Builds and releases

The workflow `.github/workflows/release.yml` builds and tests on every pull request and on every push to `main`. **It does not publish anything on its own.** Build outputs of those runs are available only as workflow artifacts.

A GitHub Release is published only when the workflow is started manually (Actions → *Build, test and release* → *Run workflow*). It is tagged `v0.1.<run number>`, marked as a pre-release, targets the commit it was built from and includes SHA-256 checksums. An existing tag is never moved or overwritten.

Assets are experimental, not the finished Android/Windows applications. Windows executables are self-contained single-file x64. The Android release-mode APK currently uses the generated debug signing key; it does not establish a production signing identity or support guaranteed upgrades across builds. Proper release signing remains a separate prerequisite for distribution (spec §23).
