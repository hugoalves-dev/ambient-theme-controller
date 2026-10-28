# Windows theme experiment

This is an experimental registry probe, not the application or a complete theme adapter. It does not detect external changes, broadcast a settings notification, or establish that Windows and third-party apps visually refresh.

Run from the repository root on Windows 11, without administrator privileges:

```powershell
dotnet test poc/windows-theme/tests/ThemeProbe.Tests.csproj
dotnet run --project poc/windows-theme/ThemeProbe.csproj -- read
```

The default action is read-only. With the user's consent, note the original values and run `dotnet run --project poc/windows-theme/ThemeProbe.csproj -- light` or `-- dark`. This writes both HKCU Personalize values and verifies their readback; a failure can leave a partial change. Observe Windows UI and applications separately, including high contrast. Restore the original setting through Windows Settings, or rerun the appropriate command when both original values matched. Record Windows build, values, visual behavior, errors and any external edits. Do not infer visual success from registry readback.

Unit tests use an in-memory store; no real Windows theme is modified by the tests. External-change correlation and an automated real-device experiment remain open.
