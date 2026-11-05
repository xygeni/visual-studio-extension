
## 1.0.3 (2026-09-09)

- Added API Security scan: API flaws are listed under "API Security" in the Xygeni explorer, with endpoint, module, service, OWASP API Top 10 and CWE details (xygeni/xygeni-product-backlog#1691).
- Added AI Security scan: AI findings are listed under "AI Security", with the AI asset kind, the standards they map to (OWASP LLM / ASI Top 10) and their red-team vectors (xygeni/xygeni-product-backlog#1692).
- AI Security findings can be fixed with the Xygeni Agent (FIX IT tab, scanner `util rectify --ai`) (xygeni/xygeni-product-backlog#1692).
- API Security findings are listed by flaw type, like the other categories; the full title is shown in the details (xygeni/xygeni-product-backlog#1691).
- Added scanner global options (xygeni/tech-support#378): a "Skip SSL verification" checkbox (`--skip-ssl-verify`, for corporate proxies that inspect TLS traffic) in the Xygeni configuration window, and a "Scanner Options (Advanced)" section with "Skip scanner update", "Verbose scanner output" and "Additional global options", placed before the scanner command. The section opens by itself while any of its options is on; a scanner call that fails on the SSL certificate offers to enable Skip SSL verification.
- Fixed API Security findings showing no location: the file and line now come from the API inventory of the report (the endpoint handler, the flaw's handler file or the module's OpenAPI spec), so they open in the editor from the explorer and the Error List (xygeni/visual-studio-extension#15).
- Fixed Error List rows of findings without a file location (API flaws scoped to a service or a module) doing nothing on double-click: they now open the issue details (xygeni/visual-studio-extension#15).
- Incremental scans (Auto Scan on Save) now refresh only the findings of the scan types they run; the other categories keep their last results instead of being re-read from the previous reports (xygeni/visual-studio-extension#15).
- Fixed the issue details view rendering scanner text as HTML: field values, tags, branch and explanations are now shown as plain text (xygeni/visual-studio-extension#15).
- Remediation advice of API Security and AI Security findings is rendered as markdown, the CODE SNIPPET tab is hidden for findings without an excerpt, and the empty Tags row is no longer shown (xygeni/visual-studio-extension#15).
- Fixed 'open file' and AI Explain from the issue details failing for every finding once an API or AI finding without an issue id was loaded (xygeni/visual-studio-extension#15).
- Fixed scans reported as failed when the scanner exits with 128 (issues found) or with 127 when only some scan types are unlicensed and the licensed ones wrote their reports; a scan with no licensed type is still reported as failed (xygeni/visual-studio-extension#15).

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
