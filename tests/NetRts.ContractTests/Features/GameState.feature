Feature: Game State Retrieval
  As a bot developer
  I want to retrieve the current game state
  So that my bot can make strategic decisions based on visible information

  Scenario: Bot retrieves initial game state
    Given a new match has started with two players
    When player 1 requests current game state
    Then the response includes their starting units
    And the response includes their resource amounts
    And the response includes visible map tiles within vision range
    And the response includes fog of war indicators

  Scenario: Bot sees only entities within vision range
    Given a match with player 1 units at position (10,10) with vision range 5
    When player 1 requests game state
    Then they see all tiles within 5 tiles of (10,10)
    And they see all entities within 5 tiles of (10,10)
    And they do not see tiles at distance 6 or greater from their units

  Scenario: Fog of war hides opponent units
    Given a match where player 2 units are outside player 1 vision range
    When player 1 requests game state
    Then player 2 units are not included in the response
    And the response only includes player 1 units and visible neutral entities

  Scenario: Invalid authentication is rejected
    Given a match is in progress
    When a bot requests game state with invalid authentication
    Then the system rejects the request with 401 Unauthorized
    And the error message indicates authentication failure
