# ado-repo-catalog

A .NET 10 console tool that catalogs Azure DevOps git repositories for humans and agents. It talks to official REST APIs only: it never clones product repos, never sparse-checkouts, and never writes back to those remotes.

For each repository it writes:

1. One markdown wiki page (purpose, stack, services, when to add code here)
2. `catalog.json` next to the wiki folder

Tests use fictional Microsoft Learn names only (`fabrikam`, `contoso-demo`). This repository must not contain a work PAT, a real organization name, or company source.

**Layout:** `src/AdoRepoCatalog` (library), `src/AdoRepoCatalog.Cli` (host), `tests/AdoRepoCatalog.Tests`.

**License:** [MIT](LICENSE)

## Safety

- Read-only HTTP GET against Azure DevOps REST. No PRs, no `AGENTS.md` writes, no pushes into product repos.
- No `git clone` and no sparse checkout. One top-level listing, at most one extra shallow folder, then a handful of key files. Fetched bodies are discarded after inference.
- Wiki and state are markdown + JSON metadata. Source file bodies are not copied into the wiki.
- One configured credential. Do not add extra PATs to dodge rate limits.

## Requirements

- [.NET 10 SDK](https://dotnet.microsoft.com/download)
- An Azure DevOps personal access token with:
  - **Code (Read)** — repositories, commits, and file contents
  - **Project (Read)** — project list

## Run on a laptop

Configuration is loaded in this order (later sources win): gitignored `appsettings.Local.json`, .NET user-secrets, then environment variables.

```bash
cd src/AdoRepoCatalog.Cli
dotnet user-secrets set Organization "<your-ado-org>"
dotnet user-secrets set PersonalAccessToken "<your-pat>"
# optional; omit to scan every project
# dotnet user-secrets set Project "<project>"
dotnet run --project . -- --output ../../out
```

The same keys work as environment variables, including an `ADO_` prefix (`ADO_ORGANIZATION`, `ADO_PAT` / `ADO_PERSONALACCESSTOKEN`, `ADO_PROJECT`, `ADO_OUTPUTDIRECTORY`, `ADO_STATEPATH`, `ADO_MAXCONCURRENCY`).

| Key | Meaning |
| --- | --- |
| `Organization` | Azure DevOps organization name (not a URL) |
| `Project` | Optional. Empty = scan all projects |
| `PersonalAccessToken` | PAT as HTTP Basic with an empty username |
| `AccessToken` | Optional Bearer token instead of a PAT |
| `OutputDirectory` | Default `./out` (gitignored). Prefer a directory outside the repo in a pipeline |
| `StatePath` | Incremental index. Default `./.ado-catalog/state.json` (gitignored) |
| `MaxConcurrency` | Repos in flight at once. Default 3, clamped to 2–4 |

`appsettings.Local.json` is gitignored. Never commit a PAT or a real organization name. A local file looks like:

```json
{
  "Organization": "",
  "Project": "",
  "PersonalAccessToken": "",
  "OutputDirectory": "./out"
}
```

**Output**

- Wiki pages: `{OutputDirectory}/wiki/{project}-{repo}.md`
- Agent catalog: `{OutputDirectory}/catalog.json`
- Incremental state: `{StatePath}`

The same command can run in an Azure DevOps pipeline with the PAT stored as a secret. It does not open pull requests into product repositories.

## Rate limits

Azure DevOps TSTU budget is about 200 per user per 5 minutes. The first full crawl is the expensive pass. Later runs skip a repo when HEAD SHA is unchanged and a wiki page already exists: the list payload plus one commit GET is enough. Skipped repos do not fetch items, trees, or file contents.

- 2–4 repositories at a time (default 3)
- Honor `Retry-After`, `X-RateLimit-Delay`, and `X-RateLimit-Remaining`
- Delay headers on HTTP 200 still wait before the next call
- HTTP 429: use `Retry-After` when present, otherwise exponential backoff with jitter

## What it fetches

Official `dev.azure.com` REST 7.1 only:

- `GET {org}/_apis/projects`
- `GET {org}/{project}/_apis/git/repositories`
- `GET {org}/{project}/_apis/git/repositories/{id}` (only when the list payload has no usable default branch / HEAD)
- `GET .../commits?searchCriteria.$top=1&searchCriteria.itemVersion.version={branch}`
- `GET .../items?scopePath=/&recursionLevel=OneLevel&versionDescriptor.version={branch}` (index path only)
- Item content for key files only (index path only)

Key files when present: `README*`, `AGENTS.md`, `*.sln`, `*.csproj`, `*pipeline*` / `azure-pipelines*.yml`, `Dockerfile*`, `package.json`, `go.mod`, `pyproject.toml`, `appsettings*.json`, and top-level `Program.cs` / `*Controller.cs`. One extra one-level listing of `src/` (or similar) if the root listing shows it.

Branch used, in order: repository `defaultBranch` (with `refs/heads/` stripped), then `develop`, `dev`, `Develop`, `Dev`, `main`, `master`, `Main`, `Master`.

## Wiki pages and catalog.json

Each page has YAML frontmatter (`name`, `project`, `remoteUrl`, `defaultBranch`, `headSha`, `languages`, `frameworks`, `services`, `confidence`, `lowConfidence`, `lastIndexed`, `whenToWriteHere`) plus sections for purpose, stack, services, routing guidance, generated file list, and a human override block.

Pages publish immediately. Low confidence is a flag and a short note, not a hold. On refresh, `<!-- OVERRIDE:START -->` … `<!-- OVERRIDE:END -->` (or `## Human override`) is kept verbatim.

`catalog.json` is an array of the same frontmatter fields plus `wikiPath`.

`ICatalogEmbedder` is a no-op reserved for embedding wiki pages later (never source).

## Build and test

```bash
dotnet build
dotnet test
dotnet format --verify-no-changes
```

`dotnet test` prints coverlet coverage for the `AdoRepoCatalog` library. GitHub Actions runs restore, `dotnet format --verify-no-changes`, and `dotnet test` on pull requests and on pushes to `main`. The workflow has no secrets, organization names, or PATs.
