# Deployment: AWS

Terraform-managed, mirroring JointBooking's module boundaries.

## Compute: Lambda, container-image packaged

`SnagList.Api` and `SnagList.Mcp` run as Lambda functions behind API Gateway (two routes — REST and
MCP), packaged from the *same* container image used in the other two deployment targets via the
Lambda container-image runtime (`Amazon.Lambda.AspNetCoreServer.Hosting`). No separate zip/native
build artifact to keep in sync — one Dockerfile per service, everywhere.

## Identity: Entra ID, validated directly

Entra ID integration is bearer-JWT validation against Entra's JWKS endpoint directly from the
application — not OIDC federation into AWS IAM. The only AWS-side consequence is network egress: a
NAT gateway is required, since Entra's OIDC endpoint has no VPC interface endpoint. (Per
JointBooking's own cost findings, VPC interface endpoints dominate the cost of a deployment like
this, not request volume — worth remembering when reviewing the Terraform plan, not a reason to
skip the NAT gateway.)

## Object storage: S3

The single `IBlobStorage` implementation from
[02-solution-architecture.md](02-solution-architecture.md) points at a real S3 bucket here instead
of MinIO — same code, different endpoint and credentials, resolved through configuration.

## Persistence: RDS PostgreSQL

Encrypted at rest, deletion protection enabled, a least-privilege application database role
distinct from the migration-runner's own (more privileged) role.

## Email: SES

The `IEmailSender` implementation sends through SES in this environment (Mailpit is a
local/home-lab-only concern).

## Secrets and config

Non-secret configuration in SSM Parameter Store; the token-signing key and database credentials in
Secrets Manager. The application's fail-fast startup check (see
[04-security-and-authentication.md](04-security-and-authentication.md)) refuses to boot on a
missing or placeholder value here just as it does everywhere else.

## Static hosting

The Blazor WASM bundle is served from S3 + CloudFront with Origin Access Control — no server-side
component needed for the web app itself; only the API and MCP surfaces run as Lambda functions.

## Terraform modules

`networking` (including the NAT gateway above), `database`, `compute`, `config` (SSM/Secrets
Manager wiring), `api-gateway` (REST + MCP routes), `static-site` (S3 + CloudFront + OAC), `email`
(SES), `state-backend`.
