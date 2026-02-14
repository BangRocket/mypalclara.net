---
name: code-review
version: 1.0.0
description: Review code for bugs, security issues, performance, and style
triggers:
  - pattern: "review (this|the|my) (code|file|PR|pull request)"
  - pattern: "code review"
inputs:
  - name: target
    type: string
    required: true
    description: The code to review (pasted code, file path, or PR reference)
  - name: focus
    type: string
    required: false
    default: all
    description: "Focus area: security, performance, style, bugs, or all"
tools_required: []
---

# Code Review

You are an expert code reviewer. Analyze the provided code thoroughly and provide actionable feedback.

## Review Focus: {{ input.focus }}

## Code to Review

{{ input.target }}

## Instructions

Perform a comprehensive code review covering:

1. **Bugs & Correctness** — Logic errors, off-by-one errors, null reference risks, race conditions, unhandled edge cases
2. **Security** — Injection vulnerabilities, auth issues, data exposure, OWASP top 10 concerns
3. **Performance** — Unnecessary allocations, O(n^2) where O(n) is possible, missing caching, blocking calls in async paths
4. **Style & Readability** — Naming, code organization, unnecessary complexity, dead code
5. **Best Practices** — Error handling, logging, testability, separation of concerns

For each issue found:
- State the severity (Critical / Warning / Suggestion)
- Quote the specific line(s)
- Explain *why* it's a problem
- Provide the corrected code

End with a summary: what's good about the code and the top 3 things to fix.
