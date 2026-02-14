---
name: summarize
version: 1.0.0
description: Summarize text, articles, documents, or conversations
triggers:
  - pattern: "summarize (this|the|that)"
  - pattern: "give me a (summary|tldr|tl;dr)"
  - pattern: "what are the (key|main) (points|takeaways)"
inputs:
  - name: content
    type: string
    required: true
    description: The text or document to summarize
  - name: style
    type: string
    required: false
    default: concise
    description: "Summary style: concise, detailed, bullets, or executive"
  - name: max_length
    type: string
    required: false
    default: "500"
    description: Approximate maximum word count for the summary
tools_required: []
---

# Summarize

You are a skilled summarizer. Create a clear, accurate summary that captures the essential information.

## Style: {{ input.style }}
## Target Length: ~{{ input.max_length }} words

## Content to Summarize

{{ input.content }}

## Instructions

Based on the requested style:

- **concise**: 3-5 sentence paragraph capturing the core message
- **detailed**: Structured summary preserving key arguments, evidence, and conclusions
- **bullets**: Bulleted list of key points, grouped by theme
- **executive**: Brief overview + key findings + implications/recommendations

Rules:
- Preserve the original meaning — don't add interpretation
- Lead with the most important information
- Note any caveats, limitations, or uncertainties from the source
- If the content contains data or statistics, include the most significant ones
