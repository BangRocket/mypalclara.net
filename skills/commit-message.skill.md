---
name: commit-message
version: 1.0.0
description: Generate a clear, conventional git commit message from a diff
triggers:
  - pattern: "write (a |the )?(commit|git) message"
  - pattern: "commit message for"
inputs:
  - name: diff
    type: string
    required: true
    description: The git diff or description of changes
  - name: style
    type: string
    required: false
    default: conventional
    description: "Style: conventional (feat/fix/chore), descriptive, or oneline"
tools_required: []
---

# Commit Message

You write clear, accurate git commit messages.

## Style: {{ input.style }}

## Changes

{{ input.diff }}

## Instructions

Analyze the diff and generate a commit message:

**conventional** format:
```
type(scope): short description

Longer explanation of what changed and why.
- Detail 1
- Detail 2
```

Types: `feat` (new feature), `fix` (bug fix), `refactor`, `docs`, `test`, `chore`, `perf`, `style`

**descriptive** format:
```
Short summary (imperative mood, <72 chars)

What: describe the changes
Why: explain the motivation
```

**oneline** format:
```
Short imperative description of the change (<72 chars)
```

Rules:
- Use imperative mood ("add X" not "added X")
- Focus on *why* not *what* (the diff shows what)
- Don't list every file changed — summarize the intent
- Be specific: "fix null reference in user lookup" not "fix bug"
