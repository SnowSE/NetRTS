Feature: Upgrades

  Scenario: Player researches an upgrade successfully
    Given a new match has started with two players
    And player 1 has an operational "TechLab" at position (20, 20) with 500 resources
    When player 1 queues a "Research" command for upgrade "WeaponDamage1" at building with ID 1
    And the game progresses for 30 ticks
    Then player 1 has the "WeaponDamage1" upgrade
    And player 1 has 400 resources
    And new "Soldier" units for player 1 have 20 attack damage

  Scenario: Player attempts to research an upgrade without enough resources
    Given a new match has started with two players
    And player 1 has an operational "TechLab" at position (20, 20) with 50 resources
    When player 1 queues a "Research" command for upgrade "WeaponDamage1" at building with ID 1
    Then the command fails with "Insufficient resources"

  Scenario: Player attempts to research a tier 2 upgrade without the prerequisite
    Given a new match has started with two players
    And player 1 has an operational "TechLab" at position (20, 20) with 1000 resources
    When player 1 queues a "Research" command for upgrade "WeaponDamage2" at building with ID 1
    Then the command fails with "Prerequisites not met"