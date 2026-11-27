# CLAUDE.md

Guidance for Claude Code when working in this repository.

## What this is

One NuGet package, `src/Hlibz.McpDocs.AspNetCore`: a ReDoc-style documentation page for
MCP servers hosted in ASP.NET Core (`MapMcpDocs`), with an opt-in try-it console (`EnableTryIt`) that calls
the MCP endpoint from the browser in the stateless `2026-07-28` shape (one POST, SSE answer). `TryItTests`
posts that exact shape to a real endpoint, so an SDK change to the wire format fails there first. It builds `catalog.json` from what the MCP SDK
already registered (`McpServerOptions` collections), lists `[Authorize]`d primitives with their requirements
(`GatedPrimitives = Show`, the default) or per viewer (`Hide`), and serves the page, the catalog and
exports (`catalog.md`, `llms.txt`, `server.json`, a self-contained `docs.html`). The README's
Development section maps each source file to what it does; keep that map in step.

- The page is `Assets/` (`index.html`, `app.js`, `app.css`, `print.css`): vanilla JS, no
  dependencies, no inline script. Embedded as `McpDocs.<file>`; `.github/scripts/check-package.sh`
  fails the pack if one stops being embedded, for each target framework.
- `[Authorize]` filtering and endpoint discovery mirror ModelContextProtocol.AspNetCore internals,
  so the library depends on the range `[2.4.0,3.0.0)` (`VersionOverride` in its csproj). The tests
  and the sample run the central version in `Directory.Packages.props`; `sdk-latest.yml` runs the
  tests weekly against the newest stable 2.x. Lifting the 3.0 cap is a manual decision.
- A tool's errors are `_meta["hlibz/errors"]` (`McpDocsToolErrors`: `[{ code, description?, guidance? }]`, written
  by the server, malformed entries skipped): the catalog lists them as the tool entry's `errors` whatever
  `ShowMeta` says, and keeps them in `McpDocsSnapshot` (contract); the page shows an Errors block and leaves the
  key out of the raw `_meta` view; `catalog.md` has a table. `ToolErrorTests`, and a behaviour test for the page.
- `samples/Sample.McpServer`: tools, prompts, resources, a demo cookie sign-in and a demo bearer token
  (the token is the roles it grants), net10.0 only.

## Requires

The .NET SDK `global.json` pins (11.0.100, patches roll forward), plus the .NET 10 runtime to run
the net10.0 tests.

## Commands

```bash
dotnet build
dotnet test                                                   # both suites, both frameworks
dotnet test --project tests/Hlibz.McpDocs.AspNetCore.Tests
dotnet test --project tests/Hlibz.McpDocs.AspNetCore.VisualTests   # needs Chromium, see README
dotnet pack src/Hlibz.McpDocs.AspNetCore -c Release -o ./artifacts
bash .github/scripts/check-package.sh ./artifacts
dotnet run --project samples/Sample.McpServer
```

## Tests

xUnit v3 on Microsoft Testing Platform (`test.runner` in `global.json`), so `dotnet test` takes
`--project` and filters by trait with `--filter-trait "Category=Behaviour"`. Pass
`TestContext.Current.CancellationToken` to async calls (xUnit1051 is an error).

- `tests/Hlibz.McpDocs.AspNetCore.Tests`: a real server on `TestServer`. Contract snapshots in
  `Snapshots/` (`admin.json`, `anonymous.json`).
- `tests/Hlibz.McpDocs.AspNetCore.VisualTests`: Playwright. `Category=Behaviour` checks `app.js`
  through the DOM and runs in CI. `Category=Screenshot` compares against `Baselines/<os>/`; only
  macOS baselines are checked in, so CI skips them. `UPDATE_SNAPSHOTS=1` accepts changes; run that
  with `-f net10.0` so the two frameworks don't write the same files at once. A Playwright bump
  needs its Chromium installed again.

## Conventions

- Targets net10.0 and net11.0 (`src/Directory.Build.props`); the test projects run both.
- `Directory.Build.props`: nullable, implicit usings, latest C#, warnings as errors, `Version`.
  The release tag overrides `Version` with `-p:Version`.
- `Directory.Packages.props` manages versions centrally; csproj files reference packages without
  `Version=`. `Microsoft.AspNetCore.TestHost` has one version per target framework.
- File-scoped namespaces, explicit types (no `var`), braces always, 4-space indent, LF, UTF-8 —
  see `.editorconfig`. `dotnet format style` on a multi-targeted project can leave
  `<<<<<<< TODO: Unmerged change` markers: run it with one target framework.
- Every public member has XML docs (`GenerateDocumentationFile`).
- Actions are pinned by commit SHA; Dependabot keeps NuGet and actions current, weekly, grouped.

## Releasing

Set `Version` in `Directory.Build.props`, commit, then push the tag `vX.Y.Z`. `release.yml` tests,
packs with the tag's version, runs `check-package.sh` and publishes with NuGet trusted publishing
(the `release` environment).
