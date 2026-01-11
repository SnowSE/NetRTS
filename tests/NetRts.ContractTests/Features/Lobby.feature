Feature: Lobby Management
    As a player
    I want to create and join lobbies
    So that I can play matches with others

    Background:
        Given a player "Player1" is registered
        And a player "Player2" is registered

    Scenario: Successfully create a lobby
        Given I am authenticated as "Player1"
        When I create a lobby named "Pro Match"
        Then the lobby is created successfully
        And I am the host of the lobby

    Scenario: Join an open lobby
        Given "Player1" has created a lobby named "Join Me"
        And I am authenticated as "Player2"
        When I join the lobby named "Join Me"
        Then I am added to the lobby successfully
        And the lobby now has 2 players

    Scenario: Cannot join a full lobby
        Given "Player1" has created a lobby named "Full Lobby"
        And "Player2" has joined the lobby named "Full Lobby"
        And a player "Player3" is registered
        And I am authenticated as "Player3"
        When I attempt to join the lobby named "Full Lobby"
        Then I receive an error "Lobby is full"

    Scenario: Host can update lobby settings
        Given I am authenticated as "Player1"
        And I have created a lobby named "Settings Test"
        When I update the lobby settings to map size 150x150
        Then the lobby settings are updated successfully

    Scenario: Host can start the match
        Given "Player1" has created a lobby named "Start Test"
        And "Player2" has joined the lobby named "Start Test"
        And I am authenticated as "Player1"
        When I start the match for lobby "Start Test"
        Then the match is started successfully
        And the lobby is closed

    Scenario: Player can leave a lobby
        Given "Player1" has created a lobby named "Leave Test"
        And I am authenticated as "Player2"
        And I have joined the lobby named "Leave Test"
        When I leave the lobby "Leave Test"
        Then I am no longer in the lobby
        And the lobby now has 1 player
