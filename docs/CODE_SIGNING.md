# Code signing policy

## Current status

The public 0.4.3-preview ZIP is **unsigned**. Our application to the SignPath Foundation free open-source program was submitted on **15 November 2026** and is awaiting review; the project has not been approved and no SignPath-signed release is available yet. Windows Smart App Control can block the current unsigned executables or launcher DLL.

We plan to use free code signing provided by [SignPath.io](https://signpath.io), with a certificate held by [SignPath Foundation](https://signpath.org), subject to acceptance of our application and technical setup. This statement describes the proposed provider, not an existing signing relationship.

## Responsibility

- Author and reviewer: repository maintainer [@sergeysrnd](https://github.com/sergeysrnd).
- Proposed release signing approver: @sergeysrnd.
- The maintainer has confirmed GitHub multi-factor authentication is enabled. SignPath account MFA must also be configured if the application is accepted.
- External contributions and changes to build or signing configuration require maintainer review. Each production signing request must receive manual approval.

## Signing scope and build origin

Only our own ZoneSpawner.exe, ZoneSpawner.Setup.exe and ZoneSpawnerLauncher.dll built from this repository are candidates for signing. Upstream UE4SS, its compatibility fix, game executables and other projects' binaries are outside our signing scope.

The [Windows build workflow](../.github/workflows/windows-build.yml) builds C# code and the x64 native launcher on a GitHub-hosted Windows runner. It runs the existing menu, installer and Lua checks before creating the signing input. Actions are pinned to commits, the workflow token is read-only, and checkout credentials are not persisted.

The workflow verifies the product name, common version, x64 PE format and unsigned status of each input. Only these three binaries, their SHA-256 values and build metadata are uploaded. Test executables, personal settings, game files and downloaded loader archives are excluded from the artifact.

The current workflow produces an **unsigned signing input**, not a player release. It has no signing credential, does not submit to SignPath and does not publish a release. Once approved, service-side origin restrictions and explicit file/metadata rules must be configured before adding a production signing step.

The first complete [successful build and existing checks](https://github.com/sergeysrnd/zone-spawner-stalker2/actions/runs/43735958957) ran on 15 November 2026. This verifies build/test readiness, not approval by the signing provider.

Before distributing signed files, we must verify Authenticode signatures, the expected trusted publisher and timestamps, generate release hashes after signing, and test the installed game/mod combination with Windows protection enabled. Signing our files does not establish trust in third-party loader files, and new files may still receive SmartScreen reputation warnings.

## Privacy

See the [privacy notice](PRIVACY.md). The application has no telemetry or analytics implementation. Game commands and responses use local bridge files. Network actions implemented by the setup wizard are initiated by the person installing the mod.

Signing-service account details and credentials must not be committed to this public repository.
