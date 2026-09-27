# SnagList design spec

Staff at a corporate site report building-quality and maintenance issues (`Snag` records); the
maintenance team triages and resolves them. One cohesive service, three delivery surfaces (Blazor
WASM web app, REST API, MCP server for agents), three deployment targets (local docker, home-lab,
AWS).

Read in order:

1. [Executive summary](00-executive-summary.md)
2. [Domain model](01-domain-model.md)
3. [Solution architecture](02-solution-architecture.md)
4. [API design](03-api-design.md)
5. [Security & authentication](04-security-and-authentication.md)
6. [Deployment: local docker](05-deployment-local-docker.md)
7. [Deployment: home-lab](06-deployment-home-lab.md)
8. [Deployment: AWS](07-deployment-aws.md)
9. [Testing & non-functional requirements](08-testing-and-nonfunctional.md)

The implementation plan derived from this spec lives alongside it as `implementation-plan.md`.

This spec was produced with the superpowers brainstorming skill, working from the same
architectural patterns as the JointBooking repository (Clean Architecture layering, HATEOAS +
agent-friendly OpenAPI decoration with CI-enforced MCP parity, IdP-owned roles, and the
home-lab-vs-local docker-compose split), adapted to SnagList's own domain and deployment choices.
