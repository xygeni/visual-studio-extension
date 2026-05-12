
## Unreleased

- Added configurable incremental scan: when enabled, saving a file triggers a debounced (1s) `xygeni scan --incremental` instead of a full scan. Disabled by default; toggle from the Xygeni Settings panel.
- Added a "Run Xygeni Incremental Scan" command (Tools menu) for manual incremental scans.
- Added SAST CODE FLOW tab in the issue details view: interactive source-to-sink D3 graph plus a textual path view. Clicking any node opens the corresponding file at the frame's line/column.

## 0.2.0 (2026-02-19)

- Added issue line decorators in the editor.
- Added scanner issues to Visual Studio Error List.
- Added proxy setting support in extension configuration.
- Improved issue details handling.

## 0.1.0 (2026-02-04)

- Initial beta release.
- Added remediation support.
