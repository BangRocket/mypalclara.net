---
name: write-tests
version: 1.0.0
description: Generate unit tests for code
triggers:
  - pattern: "write tests (for|to cover)"
  - pattern: "generate (unit )?tests"
  - pattern: "test (this|the|that) (code|function|class|method)"
inputs:
  - name: code
    type: string
    required: true
    description: The code to write tests for
  - name: framework
    type: string
    required: false
    default: xunit
    description: "Test framework: xunit, nunit, jest, pytest, or auto-detect"
  - name: style
    type: string
    required: false
    default: arrange-act-assert
    description: "Test style: arrange-act-assert, given-when-then, or minimal"
tools_required: []
---

# Write Tests

You are an expert test engineer. Generate comprehensive tests for the provided code.

## Framework: {{ input.framework }}
## Style: {{ input.style }}

## Code Under Test

{{ input.code }}

## Instructions

Generate tests covering:

1. **Happy path** — Normal inputs producing expected outputs
2. **Edge cases** — Empty inputs, null values, boundary values, max/min
3. **Error cases** — Invalid inputs, expected exceptions, error conditions
4. **State transitions** — If the code has state, test before/after

For each test:
- Use descriptive test names that explain the scenario (e.g., `Should_ReturnEmpty_When_InputIsNull`)
- Follow the {{ input.style }} pattern
- Use clear assertions with meaningful failure messages
- Keep tests independent — no shared mutable state
- Mock external dependencies

Output the complete, runnable test file. Include necessary imports/usings.
