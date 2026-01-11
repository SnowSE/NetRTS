Feature: Match Scoring and Victory

  Scenario: Player wins by eliminating opponent Command Center
    Given a new match has started with two players
    And player 2 has a "CommandCenter" at position (90, 90)
    When player 1's units destroy player 2's "CommandCenter"
    And the game progresses for 1 tick
    Then the match status is "Completed"
    And player 1 is the winner
    And the match result is available with a valid score

  Scenario: Match ends by time limit
    Given a match has reached the maximum tick limit
    And player 1 has a higher total score than player 2
    When the game progresses for 1 tick
    Then the match status is "Completed"
    And player 1 is the winner
    And the match result shows player 1 has a higher score

  Scenario: Player cannot view result of an active match
    Given a new match has started with two players
    When player 1 requests the match result
    Then the request fails with status 409 Conflict
    And the error indicates "not completed"
