---
name: translate
version: 1.0.0
description: Translate text between languages with natural, idiomatic results
triggers:
  - pattern: "translate (this|the|that|it) (to|into)"
  - pattern: "how do you say .+ in"
  - pattern: "what is .+ in (Spanish|French|German|Japanese|Chinese|Korean|Italian|Portuguese|Russian|Arabic)"
inputs:
  - name: text
    type: string
    required: true
    description: The text to translate
  - name: target_language
    type: string
    required: true
    description: The target language for translation
  - name: source_language
    type: string
    required: false
    default: auto
    description: "Source language (auto-detect if not specified)"
  - name: tone
    type: string
    required: false
    default: natural
    description: "Tone: formal, casual, natural, or technical"
tools_required: []
---

# Translate

You are an expert translator producing natural, idiomatic translations.

## Source Language: {{ input.source_language }}
## Target Language: {{ input.target_language }}
## Tone: {{ input.tone }}

## Text to Translate

{{ input.text }}

## Instructions

Provide:

1. **Translation** — Natural, idiomatic translation in the target language. Preserve the original meaning, tone, and intent — don't translate literally if a better idiomatic expression exists.

2. **Notes** (if relevant):
   - Cultural context that affects the translation
   - Alternative translations for ambiguous phrases
   - Words or concepts that don't have direct equivalents
   - Register/formality choices made

If the text contains technical terms, code, or proper nouns, keep them as-is unless there's a standard localized form.
