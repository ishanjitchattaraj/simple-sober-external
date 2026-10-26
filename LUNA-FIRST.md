# LUNA-FIRST.md — SoberExternal

> Historically this playbook was written around a single named worker agent ("Luna").
> It now applies to **any lower-cost model tier** — pick the cheapest model that can
> finish the task, and reserve the expensive tier for orchestration and escalation.

## Core Rule
**Lower-cost models do the work. The higher-cost tier orchestrates.**

Maximize verified progress per unit of model usage.

## Tier roles

### Orchestrator (higher-cost tier)
- Delegate implementation, debugging, and validation to the lower-cost tier.
- Batch related tasks into a single handoff instead of many small ones.
- Set the appropriate model and reasoning level automatically on each delegation.
- Make only critical architectural decisions.
- Do not duplicate the worker's exploration, coding, or testing.
- Accept verified results without needless review churn.
- Delegate immediately; avoid slow planning loops.

### Worker (lower-cost tier)
- Own the task end-to-end: inspect → implement → debug → validate.
- Make local decisions within the existing architecture.
- Reuse code, context, and prior findings whenever possible.
- Read only the files needed for the current task.
- Run targeted validation; reserve full-suite checks for milestones.
- Resolve routine failures without handoff friction.
- Return concise, evidence-based results.

## Model selection

- Default every delegated task to the lowest-cost model likely to succeed.
- Escalate the model only when the task or the failure history justifies it.
- Drop back to the cheaper model as soon as the hard part is resolved.
- Use the strong tier for architecture, cross-cutting refactors, and irreducible debugging.
- Use the cheap tier for exploration, mechanical edits, targeted tests, and log triage.

## Adaptive Reasoning
| Effort | Use for | Typical model |
|---|---|---|
| LOW | Exploration, implementation, simple fixes, targeted tests | cheapest tier |
| MEDIUM | Multi-file changes, API work, moderate debugging | mid tier |
| HIGH | VM internals, crashes, concurrency, closure-heavy systems | strong tier |

- Start at the lowest sufficient effort.
- Escalate LOW → MEDIUM → HIGH → strong tier when justified.
- Skip levels when the task is obviously difficult.
- Reduce effort after the issue is resolved.
- Split high-complexity work into smaller LOW/MEDIUM tasks when possible.
- Preserve findings during escalation; do not restart blindly.

## Agent Efficiency
- Default to 1 worker; use 2 only for genuinely independent tasks.
- If a task looks unusually difficult or time-consuming, ask the user whether to use 3 worker subagents before starting that parallel work.
- Use 3 only with the user's approval, and only when the work can be split into independent, non-overlapping tasks.
- Prefer complete related batches over fragmented work.
- One agent owns each subsystem; avoid overlapping edits.
- When multiple workers contribute to the same section, coordinate directly using available agent-to-agent messaging; share relevant findings and resolve handoffs without routing routine updates through the orchestrator.
- Have the orchestrator handle only blockers, conflicts, and final consolidated results; if direct messaging is unavailable, state that limitation and keep coordination concise.
- Keep useful agent context alive when it reduces rework.
- Give each agent clear success criteria and relevant entry points.
- Keep full logs; surface only relevant failures.
- Avoid redundant builds, reviews, and repeated searches.
- After 2 failed attempts, reassess or escalate.
- Stop as soon as the acceptance criteria are met.
- Preserve important discoveries in concise project notes.

## Task Budget
- Prefer one investigation, one implementation, and focused validation.
- Expand scope only when evidence requires it.
- Do not chase unrelated failures.
- The orchestrator intervenes only for real blockers or high-risk changes.
- Never trade correctness or required validation for speed.

## Reporting
Changed: ...
Tests: ...
Blockers: ...

## Final Rule
**The orchestrator delegates work in batches. Lower-cost models build, fix, and validate.**

The orchestrator intervenes only when necessary.

Use the actual model and effort settings when supported.
