---
name: Feature Request
about: Propose a new algorithm, feature, or architectural enhancement
title: "[FEAT] "
labels: ["enhancement"]
assignees: "ericksonlopezf"
---

## Problem Statement
<!-- A clear and concise description of the problem this feature solves. E.g. "I'm always frustrated when..." -->

## Proposed Solution
<!-- A clear and concise description of what you want to happen. -->

## Invariant Assessment
`EricksonLopez.RateLimiting` enforces non-negotiable architectural invariants. How does your proposal affect:
- **Allocation Profile:** Does it preserve the 0 B heap allocation guarantee on hot paths?
- **Native AOT:** Does it avoid dynamic reflection, dynamic IL generation, and trim warnings?
- **Thread-Safety & Concurrency:** Does it support lock-free or bounded thread-safe execution?
- **Resilience:** Does it support deterministic Fail-Open / Fail-Closed fallback via `Result<T>`?

## Alternatives Considered
<!-- Any alternative solutions or workarounds you have considered. -->

## Additional Context
<!-- Add any other context, diagrams, or references here. -->
