# ado-repo-catalog

Laptop-first Azure DevOps git repository catalog. Isolated tooling only.

The console app walks git repositories through Azure DevOps REST (it never clones history), infers purpose/stack/services from a small set of key files, and writes:

1. One markdown wiki page per repository
2. `catalog.json` for agents

An agent can answer: repo name and URL, purpose, stack, services, functionality, and when to add code in that repo.

This repository must not contain a work PAT, a real Azure DevOps organization name, a company name, or company source. Tests use fictional Microsoft Learn-style names only (`fabrikam`, `contoso-demo`).

## Requirements

- .NET 10 SDK
- An Azure DevOps personal access token with:
  - **Code (Read)** — list repositories, commits, and file contents
  - **Project (Read)** — list projects

## Laptop run

Configuration is read in this order (later sources win): gitignored `appsettings.Local.json`, .NET user-secrets, then environment variables.

From the repository root:

```bash
cd src/AdoRepoCatalog
dotnet user-secrets set Organization "<your-ado-org>"
dotnet user-secrets set PersonalAccessToken "<your-pat>"
# optional: limit to one project; omit to scan every project
# dotnet user-secrets set Project "<project>"
dotnet run --project . -- --output ../../out
```

Equivalent environment variables (also accepted with an `ADO_` prefix, for example `ADO_ORGANIZATION`):

| Key | Meaning |
| --- | --- |
| `Organization` | Azure DevOps organization name (not a URL) |
| `Project` | Optional. Empty = scan all projects |
| `PersonalAccessToken` | PAT sent as HTTP Basic with an empty username |
| `AccessToken` | Optional Bearer token instead of a PAT |
| `OutputDirectory` | Default `./out` (gitignored). Prefer a directory outside the repo in pipelines |
| `StatePath` | Incremental index state. Default `./.ado-catalog/state.json` (gitignored) |

Do not commit `appsettings.Local.json`, user-secrets files, or PATs. If you use a local JSON file, keep the token out of git:

```json
{
  "Organization": "",
  "Project": "",
  "PersonalAccessToken": "",
  "OutputDirectory": "./out"
}
```

Wiki pages land in `{OutputDirectory}/wiki/`. `catalog.json` is written next to that folder (`{OutputDirectory}/catalog.json`). Incremental state is `{StatePath}`.

Later this same command can run on an Azure DevOps pipeline using pipeline secrets for the PAT. This tool does not open pull requests into product repositories.

## What gets fetched

Official `dev.azure.com` REST 7.1 endpoints only:

- `GET {org}/_apis/projects`
- `GET {org}/{project}/_apis/git/repositories`
- `GET {org}/{project}/_apis/git/repositories/{id}` (default branch)
- `GET .../commits?searchCriteria.$top=1&searchCriteria.itemVersion.version={branch}`
- `GET .../items?scopePath=/&recursionLevel=OneLevel&versionDescriptor.version={branch}`
- Item content for key files only

Key files when present: `README*`, `AGENTS.md`, `*.sln`, `*.csproj`, `*pipeline*` / `azure-pipelines*.yml`, `Dockerfile*`, `package.json`, `go.mod`, `pyproject.toml`, `appsettings*.json`, and top-level `Program.cs` / `*Controller.cs`. If the root listing shows `src/` (or a similar shallow folder), one extra one-level listing is fetched. The tree is never walked fully.

## Incremental index

State maps repository id → last indexed default-branch HEAD SHA. Unchanged SHAs skip a repo when its wiki page still exists. A SHA change or a missing page always refreshes.

## Branch resolution

1. Repository `defaultBranch` (with `refs/heads/` stripped)
2. Fallback order: `develop`, `dev`, `Develop`, `Dev`, `main`, `master`, `Main`, `Master`
3. First branch that has a commit or an items listing

The branch that was used is recorded on the wiki page and in `catalog.json`.

## Wiki pages and catalog.json

Each page uses a safe `{project}-{repo}` slug, YAML frontmatter (`name`, `project`, `remoteUrl`, `defaultBranch`, `headSha`, `languages`, `frameworks`, `services`, `confidence`, `lowConfidence`, `lastIndexed`, `whenToWriteHere`), and sections for purpose, stack, services, routing guidance, generated file list, and a human override block.

Pages are published immediately. Low confidence is a frontmatter flag plus a short note, not a hold.

On refresh, `<!-- OVERRIDE:START -->` … `<!-- OVERRIDE:END -->` (or a `## Human override` section) is preserved verbatim. The rest of the page is regenerated.

`catalog.json` is an array of the same frontmatter fields plus `wikiPath`.

v1 is wiki + JSON only. `ICatalogEmbedder` is a no-op stub for a later Qdrant / Azure AI Search embed of wiki pages (not source).

## Build and test

```bash
dotnet build
dotnet test
```
