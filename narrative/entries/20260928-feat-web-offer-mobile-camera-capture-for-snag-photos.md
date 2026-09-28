---
date: 2026-09-28
slug: feat-web-offer-mobile-camera-capture-for-snag-photos
title: "feat(web): offer mobile camera capture for snag photos"
summary: "Offer separate upload and camera actions, each with text that names the action."
kind: product
status: accepted
sequence: 2026-09-28T04:29:34.000Z
evidence: "https://github.com/james-mitchell-ba/SnagList/pull/11; merge commit 1e98751be01cfeb9e60d1bc79f2faa15188a784a"
---

## Context

Staff may report a Snag while standing at the affected Location. The detail page offered only a file picker for adding photos, making a fresh mobile photo less direct.

## Decision

Offer separate upload and camera actions, each with text that names the action. Show the camera action on devices with a coarse touch pointer, keep the file picker as an upload option, and reuse the existing photo upload handler and endpoint. Use native browser capture with a rear camera preference.

## Consequences

Mobile users can take and upload a photo in the same flow as a selected file. Camera availability and the capture experience remain browser controlled; the upload option remains available as a fallback. No API or domain model changes are needed.

---

AI-Fingerprint: sha256:889048e3bbcd
