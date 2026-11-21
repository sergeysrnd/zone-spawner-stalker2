# Code signing policy

## Current status

The public 0.4.3-preview contains **unsigned binaries**. Windows 11 Smart App Control can block `ZoneSpawnerLauncher.dll`, preventing the menu from starting with the game. See the [Windows troubleshooting guide](TROUBLESHOOTING.md#windows-blocks-launcher).

SignPath Foundation declined the application submitted on **15 November 2026**. There is currently no signing provider configured for these binaries.

## Responsibility

- Author and reviewer: repository maintainer [@sergeysrnd](https://github.com/sergeysrnd).
- Proposed release signing approver: @sergeysrnd.
- The maintainer has confirmed GitHub multi-factor authentication is enabled.
- External contributions and changes to build or signing configuration require maintainer review. Each production signing request must receive manual approval.

## Signing scope and build origin

Only our own ZoneSpawner.exe, ZoneSpawner.Setup.exe and ZoneSpawnerLauncher.dll built from this repository are candidates for signing. Upstream UE4SS, its compatibility fix, game executables and other projects' binaries are outside our signing scope.

The [Windows build workflow](../.github/workflows/windows-build.yml) builds C# code and the x64 native launcher on a GitHub-hosted Windows runner. It runs the existing menu, installer and Lua checks before creating the signing input. Actions are pinned to commits, the workflow token is read-only, and checkout credentials are not persisted.

The workflow verifies the product name, common version, x64 PE format and unsigned status of each input. Only these three binaries, their SHA-256 values and build metadata are uploaded. Test executables, personal settings, game files and downloaded loader archives are excluded from the artifact.

The workflow produces an **unsigned build artifact** for signing preparation. A future production signing step would require a provider with rules that verify the provenance and metadata of each file it signs.

The first complete [successful build and existing checks](https://github.com/sergeysrnd/zone-spawner-stalker2/actions/runs/43735958957) ran on 15 November 2026. This verifies build/test readiness, not approval by the signing provider.

Before distributing signed files, we must verify Authenticode signatures, the expected trusted publisher and timestamps, generate release hashes after signing, and test the installed game/mod combination with Windows protection enabled. Signing our files does not establish trust in third-party loader files, and new files may still receive SmartScreen reputation warnings.

## Privacy

See the [privacy notice](PRIVACY.md). The application has no telemetry or analytics implementation. Game commands and responses use local bridge files. Network actions implemented by the setup wizard are initiated by the person installing the mod.

Signing-service account details and credentials must not be committed to this public repository.
