# Contributing to Confera

## Workflow and branches

Use GitHub Flow: create a short-lived branch from an up-to-date `main`, make a
focused change, open a pull request into `main`, review and validate it, then
squash merge and delete the branch. Keep `main` buildable.

The only exception is the final roadmap status update described under Roadmap
maintenance below.

There is no permanent `develop` branch or separate release branch workflow.
Branch prefixes are a repository naming convention, not Gitflow.

Use `<type>/<short-kebab-case-description>` with lowercase English names:

| Prefix | Purpose | Example |
| --- | --- | --- |
| `feature/` | New functionality | `feature/postgres-persistence` |
| `fix/` | Bug fix | `fix/duplicate-registration` |
| `chore/` | Repository setup and maintenance | `chore/repo-setup` |
| `docs/` | Documentation only | `docs/local-development` |
| `refactor/` | Restructuring without changing behavior | `refactor/persistence-registration` |
| `test/` | Tests and test infrastructure | `test/postgres-integration` |
| `build/` | Build tooling or dependency changes | `build/update-packages` |
| `ci/` | CI configuration | `ci/validate-pull-requests` |
| `perf/` | Performance improvements | `perf/registration-query` |
| `style/` | Formatting without behavior changes | `style/format-source` |

### Create and publish a task branch

Check `git status --short --branch` and preserve any existing user changes before
switching branches. Fetch `origin` and verify that `origin/main` contains every
required prerequisite, including an approved specification. Do not assume that
a documentation branch or PR has merged. Continue an existing task on its task
branch; the following example is for a new branch:

```powershell
git fetch origin
git log -5 --oneline origin/main
git switch --no-track -c feature/example-change origin/main
git branch -vv
```

Run commands sequentially and stop on failure. `--no-track` is required when
branching from `origin/main`: Git can otherwise configure the new task branch
to track `origin/main`. The starting commit and the upstream are different
choices. A new unpublished task branch must have no upstream; a published one
must track its own matching remote branch, never `origin/main`.

When publication is explicitly authorized, verify the current branch and use
an explicit destination for its first push:

```powershell
git branch --show-current
git push --set-upstream origin HEAD:refs/heads/feature/example-change
git branch -vv
```

Replace `feature/example-change` in both examples with the actual task branch.
After pushing, verify that its upstream is `origin/feature/example-change`
before using a bare push or an IDE Publish/Sync action. If an existing task
branch tracks `origin/main`, remove that incorrect upstream with
`git branch --unset-upstream <task-branch>` before publishing it to its own
remote branch. Do not use force push or rewrite `main` to repair tracking.

A push is not a PR or a merge. Open the PR with the task branch as head and
`main` as base only when explicitly requested; local-work authorization does
not authorize publication or merging.

## Commits and pull requests

Use Conventional Commits for commit subjects and PR titles:

```text
<type>(<optional-scope>): <short imperative description>
```

Write subjects in English without a trailing period. Use `feat` for feature
commits, even though the corresponding branch prefix is `feature/`.
Other commit types match the prefixes above. Choose the type by the purpose
of the change, not merely by the files touched.

Examples:

```text
chore(repo): establish project structure and repository conventions
feat(persistence): add PostgreSQL persistence with EF Core
test(integration): verify persistence against PostgreSQL
docs: explain local development setup
```

For a breaking change, use `!` after the type/scope and describe the migration
in a `BREAKING CHANGE:` footer. Scope is optional; prefer meaningful areas such
as `repo`, `api`, `persistence`, or `integration`.

A PR should describe the problem, resulting behavior, and actual validation
performed. Mention material limitations, such as an empty test suite or an
unavailable Docker runtime. Keep unrelated changes in separate PRs. Use a
Conventional Commit PR title so the squash commit can use the same subject;
check the generated squash message before merging.

These conventions are documented expectations. No CI enforcement or GitHub
branch protection is configured by this document.

## Local setup and validation

Install a .NET SDK accepted by `global.json` and run Docker with Linux containers.
See [local development](docs/local-development.md) for connection settings,
worker options, migration commands, isolation, and the scoped database reset.
From the repository root:

```powershell
dotnet tool restore
dotnet restore Confera.slnx
dotnet build Confera.slnx --configuration Release --no-restore
dotnet run --project orchestration/Confera.AppHost
```

Run the relevant test projects. The repository selects
Microsoft.Testing.Platform in `global.json`; use its CLI syntax:

```powershell
dotnet test --solution Confera.slnx --configuration Release --no-build
```

Run every implemented suite explicitly, as CI does:

```powershell
dotnet test --project tests/Confera.Domain.Tests --configuration Release --no-build
dotnet test --project tests/Confera.Integration.Tests --configuration Release --no-build
dotnet test --project tests/Confera.AppHost.Tests --configuration Release --no-build
```

Application.Tests still contains no tests. The runner reports `Zero tests ran`
with exit code 8 for that project, so the solution test command currently fails.
Do not suppress this result or add placeholder tests solely to make it green.
A successful build or zero discovered tests is not evidence of tested application
behavior. Add meaningful tests with behavior changes. Integration and AppHost
suites require Docker and use PostgreSQL 18.6 with random ports and disposable
resources. Normal integration tests migrate a unique empty database per test;
seed scenarios opt in. Resource Reaper remains enabled.

Add `--report-trx --results-directory artifacts/tests/<suite>` for TRX reports.
The [P1 validation record](docs/p1-validation.md) contains actual commands and
results. The GitHub Actions workflow restores/builds Release and invokes each
implemented suite; failures remain failures. Test output and safe resource-state
diagnostics are ignored locally and archived in CI.

Before opening a PR, review `git diff` and `git status`, run `git diff --check`,
build the solution, and run relevant tests. Include new source and configuration
files in the commit, but exclude generated outputs and credentials.

## Roadmap maintenance

[docs/implementation-roadmap.md](docs/implementation-roadmap.md) records task
status, completion criteria, results, and validation evidence. Maintaining it is
part of completing a task, whether the author is a person or an agent.

For every feature or task that advances or changes the roadmap:

1. Update the affected entry in the implementation PR with its actual progress,
   delivered behavior, remaining work, and validation results. Use the roadmap's
   status definitions; an unmerged implementation cannot be Done.
2. If the work is not represented, add a task with the agreed scope and completion
   criteria. Record approved scope changes and unresolved decisions instead of
   silently changing the meaning of an existing task.
3. After the implementation merges and its completion criteria and relevant
   checks are satisfied, update the entry to Done and record its merged PR or
   commit. The author or maintainer can perform this update or explicitly
   delegate it to an agent.

### Final status update after merge

After confirming that the implementation is merged into an up-to-date `main`,
the task's completion criteria are met, and relevant checks passed, the author,
maintainer, or assigned agent may make one documentation commit directly on
`main` without a separate branch or PR.

This exception permits changes only to `docs/implementation-roadmap.md`, limited
to marking the existing task Done and recording its merged PR or commit and
validation evidence. Review the diff and run `git diff --check` before committing.
For example:

```text
docs(roadmap): mark P1 as done
```

Changes to code, configuration, other documentation, task scope, completion
criteria, or new roadmap tasks still follow GitHub Flow through a branch and PR.

For a partial delivery, record the completed portion and leave the remaining
task open. Keep results concise and link to detailed requirements rather than
duplicating them. This is a shared maintenance rule, not an automated CI check.

## Shared configuration

- `Directory.Build.props`: common framework, nullable, implicit usings, and analysis level.
- `Directory.Packages.props`: central versions for NuGet package references.
  Keep versions out of individual `PackageReference` entries.
- `.editorconfig`: formatting and style. Suggestions currently do not fail builds.
- `.config/dotnet-tools.json`: versioned local CLI tools, restored with `dotnet tool restore`.
- `global.json`: SDK selection and test runner. This is separate from the target framework.

Keep project-specific properties in the corresponding `.csproj`. The Aspire
AppHost SDK version remains in its `Sdk` attribute, and local tool versions
remain in the tool manifest. Do not commit secrets or machine-specific paths.

## Project dependencies

The table lists direct `ProjectReference` dependencies. Production dependencies
point inward; Domain stays independent of EF Core, ASP.NET Core, and hosting.

| Project | Direct dependencies |
| --- | --- |
| Domain | None |
| Application | Domain |
| Infrastructure | Application, Domain |
| Api | Application, Infrastructure, ServiceDefaults |
| ServiceDefaults | None within this solution |
| MigrationWorker | Infrastructure, ServiceDefaults |
| AppHost | Api, MigrationWorker |
| Domain.Tests | Domain |
| Application.Tests | Application, Domain |
| Integration.Tests | Api, Infrastructure, Domain |
| AppHost.Tests | AppHost, Infrastructure, MigrationWorker, Domain |

Application owns use cases and the persistence contracts they need.
Infrastructure owns the EF Core context, explicit mappings, versioned migrations,
provider configuration and one-time initializer. It implements future Application
persistence contracts as use cases require them. API references Infrastructure to compose DI;
controllers should call Application use cases rather than access the context.
AppHost orchestrates executable services and external resources.

Shared container/database test fixtures are linked source under `tests/Shared`;
they add no production dependency. Do not add EF annotations or hosting/provider
references to Domain, mutable collection access for EF, or generic repositories
before a use case needs an explicit contract.

Project references make types available; they do not register services in DI.
They also do not enforce every architectural boundary: SDK-style projects expose
transitive references. Review actual type usage as well as `.csproj` changes.
Add direct references when a project starts directly using another project's types.

## References

- [GitHub Flow](https://docs.github.com/en/get-started/using-github/github-flow)
- [Conventional Commits](https://www.conventionalcommits.org/en/v1.0.0/)
- [Clean Architecture and dependency direction](https://learn.microsoft.com/en-us/dotnet/architecture/modern-web-apps-azure/common-web-application-architectures)
