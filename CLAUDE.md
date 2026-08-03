# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

@AGENTS.md

## Line endings

Do not normalize or convert line endings. `core.autocrlf` is configured and handles this correctly in
almost every case — committed content is normalized to LF regardless of what the working copy holds, so
a mixed working tree is not a problem worth fixing.

- Never rewrite a file solely to change its line endings. That is churn, not a fix.
- Do not "fix" a file whose endings differ from its neighbours. Leave it alone.
- Ignore git's `LF will be replaced by CRLF` warning — it is informational, not an error.
- The bar for converting endings is an actual, demonstrated failure (a tool that chokes on CRLF, a test
  asserting on exact bytes). Say what broke before converting anything.

## graphify

This project has a knowledge graph at graphify-out/ with god nodes, community structure, and cross-file relationships.

Rules:
- For codebase questions, first run `graphify query "<question>"` when graphify-out/graph.json exists. Use `graphify path "<A>" "<B>"` for relationships and `graphify explain "<concept>"` for focused concepts. These return a scoped subgraph, usually much smaller than GRAPH_REPORT.md or raw grep output.
- If graphify-out/wiki/index.md exists, use it for broad navigation instead of raw source browsing.
- Read graphify-out/GRAPH_REPORT.md only for broad architecture review or when query/path/explain do not surface enough context.
- After modifying code, run `graphify update .` to keep the graph current (AST-only, no API cost).
