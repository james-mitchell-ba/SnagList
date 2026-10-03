Feature: Enriched status-change mail for reporters
  As a staff reporter, I want status-change mail that names my report
  and links back to it, so that I know it is being handled.

  Background:
    Given a Location named "Head office" exists
    And Staff user "jane.smith" reported a Snag at "Head office" with sub-location "Kitchen ceiling"

  Scenario: Acknowledge triggers enriched mail
    Given I am the reporter "jane.smith"
    When Maintenance acknowledges my Snag
    Then an email is sent to "jane.smith"
    And the subject names the new status "Acknowledged"
    And the body names "Head office" and "Kitchen ceiling"
    And the body names the move from "Reported" to "Acknowledged"
    And the body contains a link to my Snag detail page

  Scenario: Each transition notifies the reporter
    Given I am the reporter "jane.smith"
    When Maintenance moves my Snag from "Acknowledged" to "InProgress"
    Then an email is sent to "jane.smith" naming the move from "Acknowledged" to "InProgress"

  Scenario: No mail when the reporter identity is unknown
    Given the reporter's staff identity is not mirrored
    When Maintenance acknowledges the Snag
    Then no email is sent
    And no error is raised

  Scenario: Missing web-app address fails fast
    Given the web-app base address is not configured
    When the application starts
    Then startup fails with a configuration error
    And no link-less mail can be sent
