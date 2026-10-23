# WinGet distribution

Package identifier: `behnamapps.RtlTerminal`.

Publication requires an accepted pull request in Microsoft's
[winget-pkgs repository](https://github.com/microsoft/winget-pkgs).
A successful submission does not mean the package is immediately searchable.
After acceptance, install with:

```powershell
winget install --id behnamapps.RtlTerminal --exact
```

The release workflow downloads the published installer, checks its SHA-256
against the GitHub release asset digest, generates manifests, validates them
with WinGet, and submits a pull request with Microsoft's WingetCreate tool.
Only stable releases are submitted. Existing versions and open submissions are
skipped, so rerunning a workflow does not create duplicate requests.
Published installers are never overwritten by later pushes with the same
application version: changing an installer would invalidate WinGet's checksum.
Changes to a released application require a new version.

Repository Actions secret `WINGET_TOKEN` must contain a GitHub classic personal
access token with `public_repo` permission. The token owner's account must be
able to fork public repositories. GitHub's automatic `GITHUB_TOKEN` cannot
submit a pull request to the external WinGet repository. Tokens are passed
through `WINGET_CREATE_GITHUB_TOKEN`, never command-line arguments.

See Microsoft's [token instructions](https://github.com/microsoft/winget-create/blob/main/doc/token.md).
If the secret expires or is absent, the WinGet job fails with setup guidance;
the separately completed GitHub release remains published.

To retry an existing release, manually run the **Publish to WinGet** workflow
with its version (for example, `1.1.4`).

Local manifest generation:

```powershell
./scripts/new-winget-manifest.ps1 -Version 1.1.4 -InstallerPath ./RtlTerminal-Setup-1.1.4-x64.exe
winget validate ./artifacts/winget/manifests/b/behnamapps/RtlTerminal/1.1.4
```
