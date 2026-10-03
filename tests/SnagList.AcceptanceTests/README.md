# Acceptance tests (Gherkin, runner-agnostic)

Executable acceptance criteria for the staff-reporter appeal design
(`docs/superpowers/specs/2026-10-03-staff-reporter-appeal-design.md`),
one feature file per slice:

- `single-pass-report.feature` — one-pass `Snag` reporting with `SnagPhoto`
  attachments, last-used `Location` default, partial-failure handling.
- `my-reports-filter.feature` — personal filter over the `Snag` list,
  resolved from the caller's own identity.
- `status-change-mail.feature` — enriched mail on `SnagStatusChanged`.
- `status-explainer.feature` — lifecycle strip on the `Snag` detail page.

The `.feature` files are plain Gherkin with no runner wired yet: no
Reqnroll/SpecFlow packages are referenced anywhere in the repo, and none
are added here. To execute them, add a runner and bind the steps (the
Background and step phrasing is kept identical across files on purpose).
Until then these files serve as reviewable acceptance criteria, not as
a passing suite.
