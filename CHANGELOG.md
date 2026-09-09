
## 1.0.3 (2026-09-09)

- Added API Security scan: API flaws are listed under "API Security" in the Xygeni explorer, with endpoint, module, service, OWASP API Top 10 and CWE details (xygeni/xygeni-product-backlog#1691).
- Added AI Security scan: AI findings are listed under "AI Security", with the AI asset kind, the standards they map to (OWASP LLM / ASI Top 10) and their red-team vectors (xygeni/xygeni-product-backlog#1692).

## 1.0.2 (2026-08-12)

- Added Code Quality scan: quality findings are listed under "Code Quality" in the Xygeni explorer, with AI auto-fix support (xygeni/xygeni-product-backlog#56).

## 1.0.1 (2026-07-15)

- Fixed IDE license validation.
- Fixed "Please open a solution or project first" error when scanning with a solution open: the workspace root resolved before the solution finished loading was cached forever, and classic `.sln` opens never refreshed it (issues stored for the project were not reloaded either).

## 1.0.0 (2026-06-11)

- Added IDE license validation.
- Gated Auto Scan on Save behind a non-Free license; Free-tier users can still run manual scans.


## 0.3.0 (2026-05-15)

- Added configurable incremental scan: when enabled, saving a file triggers a debounced (1s) `xygeni scan --incremental` instead of a full scan. Disabled by default; toggle from the Xygeni Settings panel.
- Added SAST CODE FLOW tab in the issue details view: interactive source-to-sink D3 graph plus a textual path view. Clicking any node opens the corresponding file at the frame's line/column.

## 0.2.0 (2026-02-19)

- Added issue line decorators in the editor.
- Added scanner issues to Visual Studio Error List.
- Added proxy setting support in extension configuration.
- Improved issue details handling.

## 0.1.0 (2026-02-04)

- Initial beta release.
- Added remediation support.
