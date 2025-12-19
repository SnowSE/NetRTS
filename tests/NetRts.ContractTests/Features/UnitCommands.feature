Feature: Unit Command Queuing
  As a bot developer
  I want to queue commands for my units
  So that my units execute actions like movement, attack, and resource gathering

  Scenario: Bot queues movement command for worker units
    Given a match with player 1 having worker units at position (10,10)
    When player 1 queues a move command for their workers to position (20,20)
    Then the command is accepted and queued
    And after the next game tick the workers are closer to position (20,20)

  Scenario: Bot queues resource gathering command
    Given a match with player 1 having worker units near a resource deposit
    When player 1 queues a gather command targeting the resource deposit
    Then the command is accepted and queued
    And after the next game tick the workers begin gathering resources
    And player 1 resource count increases

  Scenario: Bot queues attack command for soldiers
    Given a match with player 1 having soldier units and player 2 having visible enemy units
    When player 1 queues an attack command targeting the enemy unit
    Then the command is accepted and queued
    And after the next game tick the soldiers move toward the target and attack if in range

  Scenario: Command queue enforces size limit
    Given a match where player 1 has already queued 500 commands
    When player 1 attempts to queue another command
    Then the system rejects the command with queue full error
    And the error message indicates the maximum queue size

  Scenario: Commands process in FIFO order
    Given a match with player 1 having worker units
    When player 1 queues three movement commands in sequence
    Then after processing the workers execute the first command first
    And subsequent commands execute in the order they were queued

  Scenario: Invalid command is rejected
    Given a match in progress
    When player 1 queues a command for a unit they do not own
    Then the system rejects the command with authorization error
    And the command is not added to the queue

  Scenario: Command rate limiting is enforced
    Given a match in progress
    When player 1 sends more than 10 command requests per second
    Then the system rejects excess requests with 429 Too Many Requests
    And the response indicates to retry after delay
