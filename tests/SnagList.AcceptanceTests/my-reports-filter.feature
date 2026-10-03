Feature: My-reports filter on the Snag list
  As a staff reporter, I want to see only my own Snag reports,
  so that I can track what I filed without searching the whole list.

  Background:
    Given a Location named "Head office" exists
    And Staff user "jane.smith" reported a Snag at "Head office"
    And Staff user "sam.jones" reported a Snag at "Head office"

  Scenario: Toggle shows only my reports
    Given I am signed in as Staff user "jane.smith"
    And I am on the Snags page
    When I enable the "My reports" toggle
    Then I see only Snags reported by "jane.smith"
    And I do not see Snags reported by "sam.jones"

  Scenario: Toggle off restores the full list
    Given I am signed in as Staff user "jane.smith"
    And I am on the Snags page with "My reports" enabled
    When I disable the "My reports" toggle
    Then I see Snags from all reporters

  Scenario: Filter is resolved from my own identity
    Given I am signed in as Staff user "sam.jones"
    And I am on the Snags page
    When I enable the "My reports" toggle
    Then I see only Snags reported by "sam.jones"

  Scenario: Filter survives paging
    Given I am signed in as Staff user "jane.smith"
    And "jane.smith" has more reports than one page holds
    And I am on the Snags page with "My reports" enabled
    When I load more results
    Then every listed Snag is reported by "jane.smith"
    And no Snag appears twice
