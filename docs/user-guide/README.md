# SnagList User Guide

SnagList lets staff report building and maintenance issues — `Snag`s — at a
company `Location`, and lets the maintenance team triage and resolve them.
Screenshots below were captured against the local Docker deployment (see
[`05-deployment-local-docker.md`](../specs/2026-09-27-snaglist/05-deployment-local-docker.md)).

There are two roles:

- **Staff** — reports `Snag`s, comments on them, and withdraws or edits their
  own reports while still `Reported`.
- **Maintenance** — triages `Snag`s (acknowledge, start work, resolve, close,
  reject) and manages `Location`s.

A staff member can also hold the Maintenance role, as the seeded `bob.maintenance`
account below does.

## Signing in

SnagList delegates sign-in to Keycloak. From any page, following **Log in**
redirects to the identity provider's sign-in screen:

![Sign-in screen](images/01-sign-in.png)

The local Docker deployment seeds two accounts for demos and testing:

| Username | Password | Roles |
| --- | --- | --- |
| `jane.smith` | `password` | Staff |
| `bob.maintenance` | `password` | Staff, Maintenance |

## Viewing Snags

After signing in, the **Snags** page lists every reported issue, with a
dropdown to filter by status:

![Snags list, signed in as Staff](images/02-snags-list-staff.png)

## Reporting a Snag

Following **Report** opens the report form. Choose the `Location`, describe
where at that `Location` the issue is, and set its category and severity:

![Empty Report a Snag form](images/03-report-snag-form.png)

![Report a Snag form filled in](images/04-report-snag-filled.png)

Submitting takes you straight to the new `Snag`'s detail page, showing its
status and the actions available to you. As the reporter, you can **Edit** or
**Withdraw** it while it is still `Reported`:

![Snag detail page as Staff, showing Edit and Withdraw](images/05-snag-detail-staff.png)

## Adding a photo

Open a `Snag` and scroll to **Photos**. On a mobile device with a camera, choose
**Take a photo**, allow camera access if your browser asks, then take and confirm
the picture. The camera picker prefers the rear camera when one is available.
Once the upload finishes, the picture appears in the Photos list. To select an
existing picture or another file, use **Upload a photo** instead.

![Photos section in the mobile layout, showing Upload a photo and Take a photo](images/13-snag-camera-option.png)

## Adding a comment

Anyone — Staff or Maintenance — can add a follow-up comment to a `Snag`,
regardless of its status:

![Comment added to a Snag](images/06-snag-comment-added.png)

## Viewing Locations

The **Locations** page lists every site `Snag`s can be reported against:

![Locations page as Staff](images/07-locations-staff.png)

## Triaging a Snag (Maintenance)

Signed in as a Maintenance user, the **Snags** page is the same list, but
opening a `Reported` `Snag` now shows the triage actions instead:

![Snags list, signed in as Maintenance](images/08-snags-list-maintenance.png)

![Snag detail as Maintenance, showing Acknowledge and Reject](images/09-snag-detail-maintenance.png)

Following **Acknowledge** moves the `Snag` to `Acknowledged` and swaps in the
next actions (**Start work**, **Reject**):

![Snag after being acknowledged](images/10-snag-acknowledged.png)

A `Snag` otherwise moves one-directionally through `Reported` → `Acknowledged`
→ `InProgress` → `Resolved` → `Closed`, with `Rejected` reachable from
`Reported` or `Acknowledged`.

## Managing Locations (Maintenance)

Only Maintenance can add a new `Location`, from the same **Locations** page:

![New Location form filled in](images/11-locations-new-location.png)

Retiring a `Location` hides it from the picker on the Report a Snag form
without deleting its history — existing `Snag`s still show it:

![Locations page as Maintenance, with one retired Location](images/12-locations-maintenance.png)
