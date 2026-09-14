# Agent workflow guide

This guide defines how agents analyze, plan, implement, and verify work on
Confera. [AGENTS.md](../AGENTS.md) requires reading it before changes.
[CONTRIBUTING.md](../CONTRIBUTING.md) remains the reference for repository
conventions; this guide does not duplicate its branch or commit rules.

## Analyze before proposing changes

Read the required sources listed in AGENTS.md and any documentation relevant to
the task. Inspect the current branch, working tree, affected code, callers, and
tests. Preserve existing user changes.

Establish the requested outcome, current behavior, documented target behavior,
and the gap between them. Identify affected invariants, ownership and dependency
boundaries, error behavior, compatibility, and relevant edge cases. Distinguish
facts supported by code or documentation from assumptions and proposals.

The analysis must cover the decisions needed for the task. Its written length
should reflect the complexity: a small refactor may need a few precise sentences,
while a new booking or persistence policy needs a detailed explanation.

Do not invent missing business requirements, defaults, architecture decisions,
or scope. If sources disagree, identify the conflict and ask for clarification.
Explicit user instructions take precedence; previously agreed decisions remain
valid until changed by the user.

## Plan, clarify, and obtain approval

Before changing repository files or creating a branch:

1. Present a concrete plan: intended result, affected files or components,
   ordered steps, documentation updates, and verification.
2. Highlight every unresolved decision that could change behavior, architecture,
   public contracts, dependencies, compatibility, or scope. Explain the options,
   trade-offs, and recommendation; a recommendation is not a decision.
3. Resolve these ambiguities with the user and revise the plan accordingly.
4. Obtain explicit approval of the resulting plan before executing it.

Do not treat silence, elapsed time, or a suggested default as approval. While
approval or clarification is pending, continue only independent inspection and
analysis; do not implement work that depends on an unanswered decision.

Approval authorizes all steps and routine implementation details within that
plan. Do not repeatedly request the same approval. Naming a private helper or
formatting an expression within an approved refactor does not require a separate
decision, provided it preserves the agreed behavior and scope.

A concise plan can use this structure:

- **Analysis:** current behavior, relevant sources, and the concrete problem.
- **Plan:** intended result, components, steps, and documentation changes.
- **Open decisions:** unresolved alternatives and their practical consequences.
- **Verification:** relevant tests, build, and review checks.

## Execute within the approved scope

Follow CONTRIBUTING.md for branch creation, repository workflow, configuration,
dependency boundaries, and validation. Continue work on its existing task branch
unless the approved plan calls for a new branch.

Keep the change focused. Do not add endpoints, packages, infrastructure,
abstractions, or unrelated cleanup outside the approved scope. Do not turn a
documented deferred feature into an implementation requirement.

If new evidence introduces a material decision or requires changing the plan,
pause the affected work, explain the finding, clarify the options, and obtain
approval for the revised plan. Report actual tool or environment failures instead
of silently weakening the intended checks.

Preserve user changes. Keep generated output, credentials, and machine-specific
configuration out of source changes. A plan for local work does not authorize
publication, merging, or sending messages to others.

## Prefer readable code

Readability comes before brevity, novelty, or mechanical use of a pattern.
Apply Clean Code, DRY, KISS, and YAGNI to make the current implementation easier
to understand and maintain, rather than adding speculative structure.

- Name methods and variables by domain intent. Make units explicit for UTC
  instants, daily hours, ticks, durations, rates, multipliers, and money.
- Use named `Require...` or `Ensure...` helpers for meaningful invariants. Keep
  aggregate-specific checks with their aggregate; share validation when it
  expresses the same rule in multiple places.
- Keep each method focused and at a consistent level of abstraction. Extract a
  helper when its name explains a coherent operation; do not split every line
  into a method or obscure the flow through excessive indirection.
- Follow .editorconfig. Separate guards, validation, state assignment, and
  calculation with blank lines. Wrap long signatures, calls, and conditions
  consistently without unrelated formatting churn.
- Use recent C# and .NET features when they clarify intent. Do not replace clear
  existing syntax simply to use a newer feature, or ban concise syntax that is
  already easy to read. Use `var` when the type is evident from context.
- Use LINQ for straightforward selection and aggregation. Prefer an explicit
  loop or named intermediate values when a pipeline hides branching, state,
  time arithmetic, or several distinct operations.
- Explain non-obvious reasoning and edge cases with a short comment or example.
  Do not use comments as a substitute for clearer names and structure.
- Apply DRY to duplicated knowledge and business rules. Similar-looking code
  with different responsibilities is not automatically a reason for a shared
  abstraction. Keep ownership and dependency boundaries clear.
- Prefer pure calculations and explicit inputs. Avoid hidden state, surprising
  mutation, and premature generalization. Optimize after an identified need;
  preserve understood behavior during a readability refactor.

## Verify and keep documentation accurate

Use the validation commands in CONTRIBUTING.md. Review all affected source and
configuration files, including new untracked files, and verify that the diff
matches the approved plan.

Run meaningful tests for changed behavior. For a refactor, use existing
behavioral tests; add coverage only for an identified gap or risk. Do not add
placeholder tests or tests that merely repeat private implementation details.
Report empty test projects and unavailable checks honestly.

Update README and affected decision documents when their statements become
inaccurate. Distinguish implemented behavior from planned work. Preserve the
original technical assignment rather than rewriting it as implementation notes.

Read [the implementation roadmap](implementation-roadmap.md) during task
analysis. Include the relevant roadmap update in the plan and implementation
changes, recording actual results, validation, and remaining work. Follow
CONTRIBUTING.md's roadmap-maintenance process and the roadmap's status
definitions. Mark a task Done only after merge and successful relevant checks;
confirm the actual merge and validation evidence before applying CONTRIBUTING.md's
exception for a final status update directly on `main`. Do not assume this update
will happen automatically. Include any pending update after merge in the handoff
to the user or the assigned agent.

Register every new documentation file in Confera.slnx using its actual relative
path under the corresponding solution folder, such as `/docs/`. Register root
agent instructions under `/Solution Items/`.

Finish with the result, the reason for material changes, actual validation, and
remaining limitations relevant to the task. Do not claim unimplemented or
untested behavior as complete.
