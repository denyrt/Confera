# Instructions for agents working on Confera

Before creating a branch or changing repository files, read and follow:

1. [CONTRIBUTING.md](CONTRIBUTING.md) in full. It is the reference for repository
   workflow, naming, configuration, dependency boundaries, and validation.
2. [Agent workflow guide](docs/agent-workflow.md) in full. Analysis, clarification
   of ambiguous decisions, and explicit approval of the plan precede execution.
3. [README.md](README.md) for actual implementation status and scope.
4. [Domain model](docs/domain-model.md) for ownership, API scope, initial data,
   reporting semantics, and deferred features.
5. [Booking and pricing decisions](docs/booking-and-pricing-rules.md) for the
   agreed time, tariff, money, snapshot, room-change, and deletion rules.
6. [Implementation roadmap](docs/implementation-roadmap.md) for task status,
   completion criteria, validation evidence, and outstanding decisions. Maintain
   it according to CONTRIBUTING.md as work advances and tasks finish.
7. [Technical assignment](docs/task-specification.md), or its Ukrainian
   counterpart, when interpreting assignment requirements.

Read additional documentation relevant to the task and re-read affected sources
when the user corrects a decision or the scope changes. Explicit user instructions
take precedence over repository guidance; surface unresolved conflicts before
implementation. Continue within an approved plan without repeatedly requesting
the same approval.

## Task completion

After completing a repository task, include recommended commit subject(s) and
a recommended pull request title in the final response. Label them separately,
use the English Conventional Commit format defined in CONTRIBUTING.md, and
describe the final scope of the changes. Provide these recommendations even
when the user will commit or open the pull request manually; the commit subject
and pull request title may be identical when appropriate.

Creating a pull request requires a separate explicit user request. Recommending
a title does not authorize creating the pull request.
