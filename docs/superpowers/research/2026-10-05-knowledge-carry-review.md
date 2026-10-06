# Carrying knowledge across a boundary: design review

A second pair of eyes on a proposed feature: send a user's Claude session memories to an agent (or a workflow of agents) that distills and sanitizes them, so the person can carry their domain knowledge from one context to another, most often from work to home. Companion to `2026-10-05-claude-config-history-gaps.md`. Written to be general: one account or several, one machine or many.

## The verdict

The feature is worth building, but the shape in the one-line pitch has the risk in the wrong place. "Send memories to an agent that sanitizes them" implies raw work material travels to wherever the agent runs, and is cleaned afterwards. Flip it: **raw material never leaves the context it came from. Distillation runs there, under that context's rules and that context's model access. Only reviewed, abstracted output crosses.** Everything below follows from that one inversion.

## Who this is for, generally

The two-account, work-and-home setup is one case of a common shape. A person accumulates know-how inside a boundary and wants to keep the part that is theirs:

| Situation | Source | Destination | Risk level |
|---|---|---|---|
| Work to personal, same person, ongoing | Work seat or work account | Personal seat | High: employer data and IP |
| Leaving a job | Work seat | Personal | Highest: this is exactly what exit agreements police |
| Personal to team | Personal seat | Shared team memory or repo | Medium: leaks personal context the other way |
| Project to project | One repo's memory | Another repo | Low |
| Machine to machine, same context | Seat on PC A | Seat on PC B | Low (sync, not distillation) |

A repeatable design names the **source**, the **destination** and a **policy** for that crossing, and treats every one of these as the same mechanism with different policy. Your setup becomes one configured boundary, not a special case.

## What should cross, and what never should

The useful distinction is between knowledge that is *about the craft* and knowledge that is *about the place*.

- **Carries well (craft):** how a tool behaves, gotchas in a language or framework, debugging approaches, working preferences ("tests before refactors", "write files before long output"), review checklists, prompts that worked, patterns stated without the system they came from.
- **Never crosses (place):** names of systems, hosts, machines, repos, customers, colleagues; schemas, internal APIs, business rules, numbers, roadmaps; anything copied from proprietary code; credentials of any kind; anything a reasonable employer would call confidential.
- **The gray zone:** a gotcha discovered in a vendor product the employer licenses, a pattern that only makes sense next to an internal system, a decision rationale that exposes strategy. Default to holding these back, and make the person decide one at a time.

Abstraction is what makes something carryable. "The order sync job double-posts when the queue redelivers" is place. "Make consumers idempotent; redelivery is normal, not exceptional" is craft. The distiller's main job is that rewrite, not redaction.

## Where it runs (the part to get right first)

1. **Distill on the source side, with the source's sanctioned model access.** For a work boundary, that means the work account or work seat, on a machine the employer allows, under whatever AI policy the employer has. A personal account, a personal API key or a home model cluster reading raw work material is the data leaving the employer's sanctioned processor, before any sanitizing happens. If the source side can't run the distiller under its own rules, the feature doesn't run for that boundary.
2. **Only the candidate output moves**, as plain text the person can read in full.
3. **The destination never pulls.** The person pushes approved items across. No agent on the home side reaches into the work side, ever.

This matters doubly for anything that routes model requests across machines: a local cluster that sends a request to whichever node has the model can carry work text onto personal hardware, or personal text onto a work machine. Pin distillation to one node inside the source boundary, or don't use the cluster for it.

## Pipeline

Layered, because no single sanitizer is trustworthy on its own:

1. **Input selection.** Start with **memory files**, not transcripts. Memory files are already distilled by the agent that wrote them, small, and mostly craft. Transcripts are huge, full of tool output, and the likeliest place for secrets and copied code. Make transcripts a later, opt-in source.
2. **Deterministic scrub, first.** A secret scanner (gitleaks-style rules plus entropy), plus a **boundary denylist** the person (or an admin) maintains: org names, product names, hostnames, domains, repo names, people, customer names. Anything matching is removed or the item is dropped. Cheap, predictable, auditable.
3. **Model abstraction.** The distiller rewrites each surviving item into the craft form, classifies it (gotcha, pattern, how-to, preference, prompt), and must say in one line *why it generalizes*. Items it can't generalize without the place are dropped, not paraphrased.
4. **Deterministic re-check.** Run step 2 again on the model's output. Models re-introduce names they were told to remove, and paraphrase secrets into near-secrets.
5. **Human review, item by item.** Approve, edit or reject each one, with the source text shown next to the rewrite. Nothing crosses on a bulk "approve all" for a high-risk boundary. This is the step that makes the person, not the tool, responsible for what leaves.
6. **Deliver** approved items into the destination as normal memory, a CLAUDE.md section or a skill, each stamped with its provenance (boundary name, date, item hash; never a pointer back into the source).

## Making it repeatable and safe to run again

- **A policy file per boundary:** source, destination, allowed categories, denylist, whether transcripts are allowed, whether review is mandatory, the model access allowed for distillation. Versioned and readable.
- **A crossing log** on the destination side: what crossed, when, under which policy version, as hashes and short titles. It makes "what did I bring home?" answerable later, and lets someone remove a batch cleanly.
- **Idempotent runs:** remember what was already proposed and decided, so a second run only offers new material.
- **Reversible delivery:** every delivered item can be found and deleted from the destination by its crossing id.
- **A dry-run mode** that shows candidates and drops without delivering anything.

## Risks to name in the spec

- **It can look like exfiltration, because mechanically it is.** Sanitizing doesn't change what the action is. The defense is the policy, the source-side execution, the human review and the log, plus the person checking their employer's rules before configuring a work boundary. The product should say so plainly in the setup flow, not bury it.
- **False confidence in sanitization.** Denylists only catch what's on them; models miss things. Present the scrub as a filter that reduces review work, never as a guarantee.
- **Leaving a job is the highest-stakes use and the most tempting one.** Consider not supporting a bulk "export everything" mode at all.
- **Memory quality.** Distilling noisy or wrong memories spreads mistakes into a new context. Keep a "verified" flag and let the destination's agents treat carried items as advice, not fact.
- **Scope for Sanduhr.** Sanduhr is a usage monitor that is growing into a Claude estate manager. A distillation pipeline is a third product. Sanduhr is the right place to *see* boundaries, sources and the crossing log. The distiller itself may belong in a separate tool or a Claude Code plugin that runs inside the source seat, which is also what source-side execution requires.

## Suggested first slice

1. Fix G1 (discover every config home). Nothing above works if Sanduhr can't see a seat.
2. Read-only "knowledge inventory" per home: count and list memory files, categories, size. No model, no crossing.
3. A one-boundary dry run: memory files only, deterministic scrub plus model abstraction, running in the source seat, producing a review file. Nothing is delivered.
4. Review UI and delivery into the destination's memory with the crossing log.
5. Only then: transcripts as an opt-in source, more boundary types, team destinations.

## Open questions

- Which memory types exist across real users' setups (user, feedback, project, reference)? Feedback and user memories are mostly craft and are the obvious first category; project memories are mostly place.
- Should the denylist be learnable (offer terms the scrub saw often) or only hand-maintained?
- How should carried items age? A destination might want them to expire or be re-confirmed.
- What does a team destination need that a personal one doesn't (shared review, attribution)?
