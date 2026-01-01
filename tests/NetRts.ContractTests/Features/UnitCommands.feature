Feature: Unit Command Queue
  As a bot developer
  I want to queue movement and resource gathering commands for my units
  So that my bot can control units and execute strategies

  Scenario: Bot queues movement command for worker
    Given a match with player 1 controlling worker units at position (10,10)
    When player 1 queues a move command to position (15,15)
    Then the command is accepted and queued
    And after the next game tick the worker has moved toward (15,15)

  Scenario: Bot queues gather resource command
    Given a match with player 1 worker at position (10,10)
    And a resource deposit at position (12,10)
    When player 1 queues a gather command targeting the resource deposit
    Then the command is accepted and queued
    And after the next game tick the worker is moving toward the deposit

  Scenario: Bot queues attack command
    Given a match with player 1 soldier at position (10,10)
    And player 2 unit at position (12,10)
    When player 1 queues an attack command targeting player 2 unit
    Then the command is accepted and queued
    And after the next game tick the soldier is attacking the target

  Scenario: Bot cannot queue commands for opponent units
    Given a match with player 1 and player 2
    When player 1 attempts to queue a command for player 2 units
    Then the command is rejected with authorization error

  Scenario: Bot cannot queue commands with invalid targets
    Given a match with player 1 worker at position (10,10)
    When player 1 queues a move command to position (-5,200)
    Then the command is rejected with validation error

  Scenario: Command queue enforces size limit
    Given a match with player 1 units
    And player 1 has 500 commands already queued
    When player 1 attempts to queue another command
    Then the command is rejected with queue full error
