---
date: 2026-09-27
slug: feat-deploy-use-minio-for-s3-compatible-storage-in-the-home-lab-stack
title: "feat(deploy): use MinIO for S3-compatible storage in the home-lab stack"
summary: "Kept MinIO as the target (per spec) but build it and `mc` from source in-repo (`deploy/home-lab/minio/Dockerfile`, `mc.Dockerfile`) rather than pulling pre-built images, verified by actually building and running both here."
kind: product
status: accepted
sequence: 2026-09-27T14:19:48.000Z
evidence: "https://github.com/james-mitchell-ba/SnagList/pull/3; merge commit b84407cd2f140d94d0ee9035820e3b024f17156c"
---

## Context

The spec (Task 5) calls for MinIO via pre-built `minio/minio:latest` / `minio/mc:latest` images.
An earlier environment couldn't pull those, so `deploy/home-lab/docker-compose.yml` was left
running LocalStack as a stand-in — a deviation from the spec. This environment confirmed the same
pull is denied everywhere now (Docker Hub/quay.io no longer serve pre-built MinIO community-edition
images to anonymous pulls at all), so the spec's own approach was no longer directly achievable.

## Decision

Kept MinIO as the target (per spec) but build it and `mc` from source in-repo
(`deploy/home-lab/minio/Dockerfile`, `mc.Dockerfile`) rather than pulling pre-built images, verified
by actually building and running both here. Rejected alternative: keep LocalStack permanently —
rejected because it diverges from the spec's intended storage backend and its own image availability
isn't guaranteed to be more stable long-term.

## Consequences

`docker compose up` now builds MinIO/`mc` from source on first run (compiles in ~1-2 minutes,
network access to the Go module proxy required), instead of pulling a small pre-built image. The
home-lab stack's storage backend now matches the spec. Left open: if Docker Hub/quay.io resume
serving pre-built MinIO images, the Dockerfiles could be dropped in favor of `image:` again — not
done here since verifying that isn't within this change's scope. Separately, the infrastructure DI
registration now depends on `AWS_REGION` being set (or defaults to `us-east-1`) for the real-AWS
storage/email providers — unrelated to this PR's MinIO change, but landed here since it's what
surfaced the gap.

---

AI-Fingerprint: sha256:7cab8a9ec364
