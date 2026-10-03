Feature: Single-pass Snag reporting with photos
  As a staff reporter, I want to file a Snag with photos in one pass,
  so that reporting takes minimum effort on a phone.

  Background:
    Given a Location named "Head office" exists
    And I am signed in as Staff user "jane.smith"

  Scenario: File a Snag with two photos in one pass
    Given I am on the Report a Snag page
    When I choose location "Head office"
    And I enter sub-location "Kitchen ceiling"
    And I choose category "Plumbing"
    And I choose severity "Medium"
    And I attach photo files "leak1.jpg" and "leak2.jpg"
    And I submit the report
    Then a Snag is created at "Head office"
    And both photos are attached to the Snag
    And I am taken to the new Snag detail page

  Scenario: Last-used Location is preselected
    Given I previously reported a Snag at "Northern office"
    When I open the Report a Snag page
    Then location "Northern office" is preselected

  Scenario: Partial photo failure keeps me on the flow
    Given I am on the Report a Snag page with details filled in
    And I attach photo files "ok.jpg" and "broken.jpg"
    When I submit the report and the upload of "broken.jpg" fails
    Then the Snag is still created
    And I stay on the reporting flow
    And I see a per-photo error naming "broken.jpg"
    And I see a link to the created Snag

  Scenario: Photo cap of five is enforced
    Given I am on the Report a Snag page
    When I attach six photo files
    Then I cannot submit with more than five photos
