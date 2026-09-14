# Contributing to Confera

## Workflow and branches

Use GitHub Flow: create a short-lived branch from an up-to-date `main`, make a
focused change, open a pull request into `main`, review and validate it, then
squash merge and delete the branch. Keep `main` buildable.

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

Install a .NET SDK accepted by `global.json`. From the repository root:

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

Domain behavioral tests are implemented. To run the relevant suite:

```powershell
dotnet test --project tests/Confera.Domain.Tests --configuration Release --no-build
```

Application and Integration test projects still contain no tests. The runner
reports `Zero tests ran` with exit code 8 for each empty project, so the solution
test command currently fails. Do not suppress this result or add placeholder
tests solely to make it green. A successful build or zero discovered tests is
not evidence of tested application behavior. Add meaningful tests with behavior
changes. Docker will be needed when container-backed tests are introduced;
PostgreSQL and Testcontainers are not configured yet.

Before opening a PR, review `git diff` and `git status`, run `git diff --check`,
build the solution, and run relevant tests. Include new source and configuration
files in the commit, but exclude generated outputs and credentials.

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
| AppHost | Api |
| Domain.Tests | Domain |
| Application.Tests | Application, Domain |
| Integration.Tests | Api, Infrastructure |

Application owns use cases and the persistence contracts they need.
Infrastructure implements these contracts and will own the EF Core context,
database mappings, and migrations. API references Infrastructure to compose DI;
controllers should call Application use cases rather than access the context.
AppHost orchestrates executable services and external resources.

Project references make types available; they do not register services in DI.
They also do not enforce every architectural boundary: SDK-style projects expose
transitive references. Review actual type usage as well as `.csproj` changes.
Add direct references when a project starts directly using another project's types.

## References

- [GitHub Flow](https://docs.github.com/en/get-started/using-github/github-flow)
- [Conventional Commits](https://www.conventionalcommits.org/en/v1.0.0/)
- [Clean Architecture and dependency direction](https://learn.microsoft.com/en-us/dotnet/architecture/modern-web-apps-azure/common-web-application-architectures)
