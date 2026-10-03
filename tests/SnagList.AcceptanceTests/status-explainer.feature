Feature: Reporter-facing status explainer on the Snag detail page
  As a staff reporter, I want to see where my Snag stands in its lifecycle,
  so that I do not need to know the maintenance workflow.

  Background:
    Given a Location named "Head office" exists
    And Staff user "jane.smith" reported a Snag at "Head office"

  Scenario: Current status is highlighted in the lifecycle strip
    Given I am signed in as Staff user "jane.smith"
    When I open my Snag detail page
    Then I see the lifecycle strip Reported, Acknowledged, InProgress, Resolved, Closed
    And the current status of my Snag is highlighted

  Scenario: Strip follows the Snag as it moves
    Given my Snag is "InProgress"
    And I am signed in as Staff user "jane.smith"
    When I open my Snag detail page
    Then "InProgress" is highlighted in the lifecycle strip

  Scenario: Terminal outsiders are explained
    Given my Snag was rejected
    And I am signed in as Staff user "jane.smith"
    When I open my Snag detail page
    Then I see a note that rejected reports leave the flow
