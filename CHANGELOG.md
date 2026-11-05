
## 1.0.4 (2026-11-05)

- Scanner options (xygeni/xygeni-product-backlog#415): a command-line-like field in the Scan configuration section of Xygeni Settings adds `xygeni scan` options with autocompletion and validation (a closed list: include patterns, detectors, mute, standard, AI review, license analysis, custom configuration); the options the extension sets itself (`--run`, `-o`, excludes…) are refused. Saved as `options` in `.xygeni\ide.json` and validated again when the file is read.
- Run one scan type alone: a "Run ... scan only" node under each category of the Xygeni Explorer runs just that scan and keeps the findings of the others.
- Security (xygeni/xygeni-product-backlog#1438): a server URL that uses `http://` to a host other than localhost is refused, with the reason in the connection status, because the API token would travel in clear on every request. `http://localhost` stays allowed. The new "Allow insecure HTTP" check in Xygeni Settings lets a server without TLS through on purpose; the status then keeps warning that the token travels in clear.
- MCP server updates (xygeni/xygeni-product-backlog#2382): after an update the info bar says what to do in plain words (restart your AI assistant so it uses the new version), with "Show me how" opening a per-assistant guide (Copilot, Claude Code, Cursor, Claude Desktop) in the MCP Server section of Xygeni Settings and a note there that stays until dismissed with OK; while the connection check runs the MCP server status says "Checking…" instead of "not installed".
- The scanner is installed in `%LOCALAPPDATA%\.xygenidata\scanner`, a folder that does not change with the extension version, so an extension update no longer downloads the ~250 MB scanner again; the copy inside the previous extension folder is moved there. A download or extraction that stops halfway is reported as a failed installation instead of "installed successfully".
- The startup connection check retries up to three times when the backend answers 5xx or refuses the connection, and Xygeni Settings shows "Connecting…" meanwhile. The Full scan / On save switches of Scan configuration are drawn as pill switches.
- A 503 from the backend's `/ping` no longer marks the URL as invalid: the endpoint answers 503 while the server's CPU is above its threshold, so the extension goes on with the token check instead of asking for a reconnect. When that token check itself gets a 5xx or no answer, the status says the server is unavailable and to retry, instead of "invalid token".
- Scan configuration per project (xygeni/xygeni-product-backlog#415): a new "Scan configuration" section in Xygeni Settings chooses which scan types the full scan runs (one checkbox per scanner; SCA brings suspect dependencies) and which of the file-scoped ones (Secrets, SAST, IaC, Malware, Code Quality) also run on save, and which folders or glob patterns are excluded from every scan (passed as `--exclude-add`, so the scanner's default excludes are kept; needs scanner 6.23 or later). Saved in `.xygeni\ide.json` at the root of the open solution or folder, shared with the other Xygeni IDE plugins and meant to be committed; the defaults (every scan type, Secrets / SAST / IaC / Malware on save, nothing excluded) write nothing. A full scan now shows only the findings of the scan types it ran.
- MCP server updates (xygeni/xygeni-product-backlog#2382): once a day, after the connection is validated, the extension reads the published MCP server version and, according to the new "MCP server updates" setting in the MCP Server section of Xygeni Settings, shows an info bar with Update / Ignore this version actions (default), updates in the background, or does nothing. The "Check for updates" button checks now. The check is shared with the other Xygeni IDE plugins through `%USERPROFILE%\.xygeni\mcp\last-check.txt`; when the published version cannot be read (no network, not published yet) nothing is shown, it is only logged.
- The installed MCP server version is written to `%USERPROFILE%\.xygeni\mcp\version.txt` and shown with the path and the update status in the MCP Server section of Xygeni Settings, which also copies the MCP client configuration (JSON) to the clipboard, as the other Xygeni IDE plugins generate it (xygeni/xygeni-product-backlog#2382).
- The MCP server download is verified against the published SHA-256 checksum; a mismatch keeps the JAR already installed. An update that cannot replace a JAR locked by a running MCP client is kept as `.pending` and applied the next time Visual Studio starts (xygeni/xygeni-product-backlog#2382).
- Added Malware: malware evidences are listed under "Malware" in the Xygeni explorer (kind, detector, severity, explanation, scan type, location, resolved code and tags, plus the CODE FLOW tab with graph, path and AI Explain for the evidences that carry code flows) and in the Error List. The injection point of each code-flow step is now read from the report field `injectionPoint`, so it shows in the flow details. The CODE FLOW tab no longer breaks when the scanned code of a step contains `</script>`: the flow data is written script-safe. Malware now runs in the full scan as well as in the incremental one; before, its findings were scanned but never shown (xygeni/xygeni-product-backlog#1473).
- The MCP server is now downloaded after the scanner is installed (and on startup when it is missing); before, it was never downloaded (xygeni/xygeni-product-backlog#2382).
- The MCP server is installed in `%USERPROFILE%\.xygeni\mcp\xygeni-mcp-server.jar`, shared with the other Xygeni IDE plugins, so its path does not change when the extension is updated. It is written to a temporary file and moved into place under a lock, so two IDEs installing at the same time never leave a half-written JAR (xygeni/xygeni-product-backlog#2382).

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
