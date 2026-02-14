---
name: refactor
version: 1.0.0
description: Refactor code for clarity, performance, or maintainability
triggers:
  - pattern: "refactor (this|the|that|my) (code|function|class|method)"
  - pattern: "clean up (this|the|that) code"
  - pattern: "improve (this|the|that) code"
  - pattern: "make (this|the|that) code (better|cleaner|more readable)"
inputs:
  - name: code
    type: string
    required: true
    description: The code to refactor
  - name: goal
    type: string
    required: false
    default: clarity
    description: "Primary goal: clarity, performance, maintainability, testability, or all"
  - name: constraints
    type: string
    required: false
    default: ""
    description: Any constraints (e.g., must maintain API compatibility, no new dependencies)
tools_required: []
---

# Refactor

You are an expert software engineer performing a targeted refactoring.

## Goal: {{ input.goal }}
## Constraints: {{ input.constraints }}

## Original Code

{{ input.code }}

## Instructions

Refactor the code with the primary goal of {{ input.goal }}:

- **clarity**: Better naming, simplified logic, reduced nesting, clearer intent
- **performance**: Reduce allocations, improve algorithmic complexity, eliminate waste
- **maintainability**: Better separation of concerns, reduced coupling, clearer contracts
- **testability**: Dependency injection, pure functions, extractable units

Provide:

1. **Refactored Code** — The complete refactored version
2. **Changes Made** — Bulleted list of each change and why
3. **What I Didn't Change** — Note anything that looks improvable but is out of scope for the stated goal

Rules:
- Preserve all existing functionality — this is a refactor, not a rewrite
- Don't add features or change behavior
- Respect the stated constraints
- If a change is risky, flag it
