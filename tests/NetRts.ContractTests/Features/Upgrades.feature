Feature: Research Upgrades
  As a bot player
  I want to research upgrades at tech buildings
  So that I can enhance my units and buildings

  Background:
    Given a match exists with two players
    And player 1 is authenticated

  Scenario: Bot queues research command at TechLab
    Given player 1 has an operational TechLab at position (40,40) with 500 resources
    When player 1 queues a research command for MeleeDamage upgrade
    Then the command is accepted and queued
    And the response indicates 1 command queued

  Scenario: Research progresses over multiple ticks
    Given player 1 has an operational TechLab at position (40,40) with 500 resources
    And player 1 has queued research for MeleeDamage upgrade
    When the research command is executed
    Then an upgrade is created with 0% research progress
    And player 1's resources are decreased by the upgrade cost
    When 10 game ticks pass
    Then the upgrade research progress increases
    When the upgrade reaches 100% research progress
    Then the upgrade is marked as completed

  Scenario: Completed upgrade applies to existing units
    Given player 1 has an operational TechLab at position (40,40) with 500 resources
    And player 1 has soldier units at position (20,20)
    And player 1 has completed the MeleeDamage upgrade
    Then all soldier units have increased attack damage

  Scenario: Newly produced units receive active upgrades
    Given player 1 has an operational Barracks at position (30,30)
    And player 1 has completed the MeleeDamage upgrade
    And player 1 has 200 resources
    When player 1 produces a new Soldier unit
    Then the new unit spawns with upgraded attack damage

  Scenario: Cannot research without TechLab
    Given player 1 has an operational Barracks at position (30,30) with 500 resources
    When player 1 queues a research command for MeleeDamage upgrade from the barracks
    Then the command is rejected due to validation failure
    And the error indicates building is not a TechLab

  Scenario: Cannot research without sufficient resources
    Given player 1 has an operational TechLab at position (40,40) with 50 resources
    When player 1 queues a research command for MeleeDamage upgrade
    Then the command is rejected due to validation failure
    And the error indicates insufficient resources

  Scenario: Cannot research non-operational building
    Given player 1 has a TechLab under construction at position (40,40)
    When player 1 queues a research command for MeleeDamage upgrade
    Then the command is rejected due to validation failure
    And the error indicates the building is not operational

  Scenario: Tier 2 upgrade requires prerequisite
    Given player 1 has an operational TechLab at position (40,40) with 1000 resources
    When player 1 queues research for RangedDamage2 without RangedDamage1
    Then the command is rejected due to validation failure
    And the error indicates prerequisite upgrade not completed

  Scenario: Multiple upgrade types can be researched
    Given player 1 has an operational TechLab at position (40,40) with 1500 resources
    When player 1 researches MeleeDamage upgrade
    And player 1 researches ArmorUpgrade upgrade
    Then both upgrades are completed
    And units have both increased damage and armor

  Scenario: Upgrade effects stack with multiple tiers
    Given player 1 has an operational TechLab at position (40,40) with 2000 resources
    And player 1 has soldier units at position (20,20)
    When player 1 completes MeleeDamage upgrade
    And player 1 completes MeleeDamage2 upgrade
    Then soldier units have attack damage increased by both tiers
