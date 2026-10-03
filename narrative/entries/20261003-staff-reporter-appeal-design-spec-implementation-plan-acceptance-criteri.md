---
date: 2026-10-03
slug: staff-reporter-appeal-design-spec-implementation-plan-acceptance-criteri
title: "Staff reporter appeal: design spec, implementation plan, acceptance criteria"
summary: "Option B was chosen: client-orchestrated single-pass uploads over the unchanged create/photo endpoints (no contract or MCP parity change), a server-resolved reporter filter on the existing cursor-paged list, context-enriched dispatcher…"
kind: product
status: accepted
sequence: 2026-10-03T18:21:40.000Z
evidence: "https://github.com/james-mitchell-ba/SnagList/pull/13; merge commit 6337fe288da7dc12c6876e00262db144d60ea9af"
---

## Context

Staff reporters were the priority audience, with low-friction filing first and status-change visibility second. Evidence from the repo shaped the design: reporting is currently a two-step flow (photo-less form, photos added afterwards on the detail page), the list has only a status filter with no reporter view, and status mail already exists but carries a bare identifier body with no site context or link back.

## Decision

Option B was chosen: client-orchestrated single-pass uploads over the unchanged create/photo endpoints (no contract or MCP parity change), a server-resolved reporter filter on the existing cursor-paged list, context-enriched dispatcher mail with a deep link, and a static lifecycle strip on the detail page. Rejected: a multipart create-with-photos endpoint, client-side filtering by reporter name, enlarged domain-event payloads, and a full history endpoint (explicit follow-up).

## Consequences

This PR changes docs and acceptance criteria only; the 5-task implementation plan is ready to execute and the feature files are runner-agnostic Gherkin until a runner is adopted. No domain concepts, contracts, or lifecycle rules change.

---
AI-Fingerprint: sha256:176aeae78338
