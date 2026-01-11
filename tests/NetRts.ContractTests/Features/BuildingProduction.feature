Feature: Building Construction and Unit Production
  As a bot player
  I want to construct buildings and produce units
  So that I can expand my base and build an army

  Background:
    Given a match exists with two players
    And player 1 is authenticated

  Scenario: Bot queues building construction command
    Given player 1 has worker units at position (10,10) with 500 resources
    And the tile at position (30,30) is unoccupied and passable
    When player 1 queues a build command for a Barracks at position (30,30)
    Then the command is accepted and queued
    And the response indicates 1 command queued

  Scenario: Workers construct building over multiple ticks
    Given player 1 has worker units at position (10,10) with 500 resources
    And player 1 has queued a build command for a Barracks at position (30,30)
    When the build command is executed
    Then a building is created at position (30,30) with 0% construction progress
    And player 1's resources are decreased by the building cost
    When 10 game ticks pass
    Then the building construction progress increases
    When the building reaches 100% construction progress
    Then the building becomes operational

  Scenario: Bot queues unit production from operational building
    Given player 1 has an operational Barracks at position (30,30) with 300 resources
    When player 1 queues a produce command for a Soldier from the barracks
    Then the command is accepted and queued
    And the response indicates 1 command queued

  Scenario: Building produces unit over multiple ticks
    Given player 1 has an operational Barracks at position (30,30) with 300 resources
    And player 1 has queued a produce command for a Soldier
    When the produce command is executed
    Then the production is added to the building's queue
    And player 1's resources are decreased by the unit cost
    When production timer reaches zero
    Then a new Soldier unit spawns adjacent to the barracks
    And the unit belongs to player 1

  Scenario: Cannot build on occupied tile
    Given player 1 has worker units at position (10,10) with 500 resources
    And there is an existing building at position (30,30)
    When player 1 queues a build command for a Barracks at position (30,30)
    Then the command is rejected due to validation failure
    And the error indicates the tile is occupied

  Scenario: Cannot build without sufficient resources
    Given player 1 has worker units at position (10,10) with 50 resources
    When player 1 queues a build command for a Barracks at position (30,30)
    Then the command is rejected due to validation failure
    And the error indicates insufficient resources

  Scenario: Cannot produce units from non-operational building
    Given player 1 has a Barracks under construction at position (30,30)
    When player 1 queues a produce command for a Soldier from the barracks
    Then the command is rejected due to validation failure
    And the error indicates the building is not operational

  Scenario: Cannot produce units without sufficient resources
    Given player 1 has an operational Barracks at position (30,30) with 20 resources
    When player 1 queues a produce command for a Soldier from the barracks
    Then the command is rejected due to validation failure
    And the error indicates insufficient resources

  Scenario: Building production queue processes FIFO
    Given player 1 has an operational Barracks at position (30,30) with 600 resources
    When player 1 queues production for 3 Soldiers
    And the produce command is executed
    Then all 3 production orders are added to the queue
    When the first production completes
    Then a Soldier spawns adjacent to the barracks
    And 2 production orders remain in the queue
    When the second production completes
    Then another Soldier spawns
    And 1 production order remains in the queue

  Scenario: Multiple buildings produce independently
    Given player 1 has an operational Barracks at position (30,30)
    And player 1 has an operational CommandCenter at position (10,10)
    And player 1 has 1000 resources
    When player 1 queues a Soldier from the Barracks
    And player 1 queues a Worker from the CommandCenter
    And the produce command is executed
    Then both buildings produce units simultaneously
    When production timer reaches zero
    Then the Soldier spawns near the Barracks
    And the Worker spawns near the CommandCenter
