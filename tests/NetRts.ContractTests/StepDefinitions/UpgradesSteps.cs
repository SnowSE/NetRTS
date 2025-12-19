using System.IdentityModel.Tokens.Jwt;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Text;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.IdentityModel.Tokens;
using NetRts.Application.Interfaces;
using NetRts.Contracts.Requests;
using NetRts.Contracts.Responses;
using NetRts.ContractTests.Support;
using NetRts.Domain.Entities;
using NetRts.Domain.Enums;
using NetRts.Domain.ValueObjects;
using NetRts.Infrastructure.Caching;
using Reqnroll;

namespace NetRts.ContractTests.StepDefinitions;

[Binding]
public class UpgradesSteps
{
    private readonly TestContext _context;
    private readonly ScenarioContext _scenarioContext;

    public UpgradesSteps(TestContext context, ScenarioContext scenarioContext)
    {
        _context = context;
        _scenarioContext = scenarioContext;
    }

    [Given(@"player 1 has an operational TechLab at position \((\d+),(\d+)\) with (\d+) resources")]
    public void GivenPlayer1HasAnOperationalTechLabAtPositionWithResources(int x, int y, int resources)
    {
        var factory = _scenarioContext.ScenarioContainer.Resolve<ApiWebApplicationFactory>();
        var gameStateCache = factory.Services.GetRequiredService<GameStateCache>();

        // Set player resources
        var match = gameStateCache.GetMatch(_context.MatchId);
        match!.SetPlayerResources(_context.Player1Id, resources);
        gameStateCache.AddOrUpdate(match);

        // Create operational TechLab
        var techLab = new Building(1, _context.MatchId, _context.Player1Id, BuildingType.TechLab, new Position(x, y));
        techLab.CompleteConstruction();

        var buildings = gameStateCache.GetBuildingsForMatch(_context.MatchId);
        buildings.Add(techLab);
        gameStateCache.SetBuildingsForMatch(_context.MatchId, buildings);

        _scenarioContext["TechLabId"] = (int)techLab.Id;
        _scenarioContext["TechLabPosition"] = new Position(x, y);
        _scenarioContext["InitialResources"] = resources;
    }

    [Given(@"player 1 has soldier units at position \((\d+),(\d+)\)")]
    public void GivenPlayer1HasSoldierUnitsAtPosition(int x, int y)
    {
        var factory = _scenarioContext.ScenarioContainer.Resolve<ApiWebApplicationFactory>();
        var gameStateCache = factory.Services.GetRequiredService<GameStateCache>();

        var units = new List<Unit>
        {
            new Unit(10, _context.MatchId, _context.Player1Id, UnitType.Soldier, new Position(x, y)),
            new Unit(11, _context.MatchId, _context.Player1Id, UnitType.Soldier, new Position(x, y)),
            new Unit(12, _context.MatchId, _context.Player1Id, UnitType.Soldier, new Position(x, y))
        };

        gameStateCache.SetUnitsForMatch(_context.MatchId, units);
        _scenarioContext["InitialSoldierIds"] = units.Select(u => u.Id).ToList();
        _scenarioContext["SoldierPosition"] = new Position(x, y);
    }

    [Given(@"player 1 has completed the MeleeDamage upgrade")]
    public async Task GivenPlayer1HasCompletedTheMeleeDamageUpgrade()
    {
        await WhenPlayer1CompletesMeleeDamageUpgrade();
    }

    [Given(@"player 1 has an operational Barracks at position \((\d+),(\d+)\)")]
    public void GivenPlayer1HasAnOperationalBarracksAtPosition(int x, int y)
    {
        var factory = _scenarioContext.ScenarioContainer.Resolve<ApiWebApplicationFactory>();
        var gameStateCache = factory.Services.GetRequiredService<GameStateCache>();

        var barracks = new Building(2, _context.MatchId, _context.Player1Id, BuildingType.Barracks, new Position(x, y));
        barracks.CompleteConstruction();

        var buildings = gameStateCache.GetBuildingsForMatch(_context.MatchId);
        buildings.Add(barracks);
        gameStateCache.SetBuildingsForMatch(_context.MatchId, buildings);

        _scenarioContext["BarracksId"] = (int)barracks.Id;
        _scenarioContext["BarracksPosition"] = new Position(x, y);
    }

    [Given(@"player 1 has (\d+) resources")]
    public void GivenPlayer1HasResources(int resources)
    {
        var factory = _scenarioContext.ScenarioContainer.Resolve<ApiWebApplicationFactory>();
        var gameStateCache = factory.Services.GetRequiredService<GameStateCache>();

        var match = gameStateCache.GetMatch(_context.MatchId);
        match!.SetPlayerResources(_context.Player1Id, resources);
        gameStateCache.AddOrUpdate(match);

        _scenarioContext["InitialResources"] = resources;
    }

    [Given(@"player 1 has a TechLab under construction at position \((\d+),(\d+)\)")]
    public void GivenPlayer1HasATechLabUnderConstructionAtPosition(int x, int y)
    {
        var factory = _scenarioContext.ScenarioContainer.Resolve<ApiWebApplicationFactory>();
        var gameStateCache = factory.Services.GetRequiredService<GameStateCache>();

        var techLab = new Building(1, _context.MatchId, _context.Player1Id, BuildingType.TechLab, new Position(x, y));
        // Don't complete construction - leave it at 0%

        var buildings = gameStateCache.GetBuildingsForMatch(_context.MatchId);
        buildings.Add(techLab);
        gameStateCache.SetBuildingsForMatch(_context.MatchId, buildings);

        _scenarioContext["TechLabId"] = (int)techLab.Id;
    }

    [Given(@"player 1 has queued research for MeleeDamage upgrade")]
    public async Task GivenPlayer1HasQueuedResearchForMeleeDamageUpgrade()
    {
        await WhenPlayer1QueuesAResearchCommandForMeleeDamageUpgrade();
    }

    [When(@"player 1 queues a research command for MeleeDamage upgrade")]
    public async Task WhenPlayer1QueuesAResearchCommandForMeleeDamageUpgrade()
    {
        var factory = _scenarioContext.ScenarioContainer.Resolve<ApiWebApplicationFactory>();
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", _context.Player1Token);

        var techLabId = (int)_scenarioContext["TechLabId"];

        var request = new QueueCommandsRequest
        {
            Commands = new[]
            {
                new CommandDto
                {
                    Type = "Research",
                    BuildingId = techLabId,
                    UpgradeType = "MeleeDamage"
                }
            }
        };

        var response = await client.PostAsJsonAsync($"/api/v1/matches/{_context.MatchId}/commands", request);
        _context.LastResponse = response;

        if (response.IsSuccessStatusCode)
        {
            _context.LastCommandResponse = await response.Content.ReadFromJsonAsync<QueueCommandsResponse>();
        }
    }

    [When(@"player 1 queues a research command for MeleeDamage upgrade from the barracks")]
    public async Task WhenPlayer1QueuesAResearchCommandForMeleeDamageUpgradeFromTheBarracks()
    {
        var factory = _scenarioContext.ScenarioContainer.Resolve<ApiWebApplicationFactory>();
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", _context.Player1Token);

        var barracksId = (int)_scenarioContext["BarracksId"];

        var request = new QueueCommandsRequest
        {
            Commands = new[]
            {
                new CommandDto
                {
                    Type = "Research",
                    BuildingId = barracksId,
                    UpgradeType = "MeleeDamage"
                }
            }
        };

        var response = await client.PostAsJsonAsync($"/api/v1/matches/{_context.MatchId}/commands", request);
        _context.LastResponse = response;

        if (response.IsSuccessStatusCode)
        {
            _context.LastCommandResponse = await response.Content.ReadFromJsonAsync<QueueCommandsResponse>();
        }
    }

    [When(@"the research command is executed")]
    public async Task WhenTheResearchCommandIsExecuted()
    {
        var factory = _scenarioContext.ScenarioContainer.Resolve<ApiWebApplicationFactory>();
        var tickProcessor = factory.Services.GetRequiredService<IGameTickProcessor>();

        await tickProcessor.ProcessTickAsync();

        _scenarioContext["ResearchExecuted"] = true;
    }

    [When(@"the upgrade reaches 100% research progress")]
    public async Task WhenTheUpgradeReaches100PercentResearchProgress()
    {
        var factory = _scenarioContext.ScenarioContainer.Resolve<ApiWebApplicationFactory>();
        var gameStateCache = factory.Services.GetRequiredService<GameStateCache>();
        var tickProcessor = factory.Services.GetRequiredService<IGameTickProcessor>();

        // Keep processing ticks until upgrade is complete
        for (int i = 0; i < 200; i++)
        {
            await tickProcessor.ProcessTickAsync();

            var match = gameStateCache.GetMatch(_context.MatchId);
            var player = match!.Players.First(p => p.PlayerId == _context.Player1Id);

            // Check if upgrade exists and is completed
            var upgrade = player.Upgrades.FirstOrDefault(u => u.UpgradeType.ToString() == "MeleeDamage");
            if (upgrade?.IsCompleted == true)
            {
                _scenarioContext["CompletedUpgrade"] = upgrade;
                break;
            }
        }
    }

    [When(@"player 1 produces a new Soldier unit")]
    public async Task WhenPlayer1ProducesANewSoldierUnit()
    {
        var factory = _scenarioContext.ScenarioContainer.Resolve<ApiWebApplicationFactory>();
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", _context.Player1Token);

        var barracksId = (int)_scenarioContext["BarracksId"];

        var request = new QueueCommandsRequest
        {
            Commands = new[]
            {
                new CommandDto
                {
                    Type = "Produce",
                    BuildingId = barracksId,
                    UnitType = "Soldier"
                }
            }
        };

        var response = await client.PostAsJsonAsync($"/api/v1/matches/{_context.MatchId}/commands", request);
        _context.LastResponse = response;

        // Execute the command and wait for production to complete
        var tickProcessor = factory.Services.GetRequiredService<IGameTickProcessor>();
        var gameStateCache = factory.Services.GetRequiredService<GameStateCache>();

        var initialUnitCount = gameStateCache.GetUnitsForMatch(_context.MatchId).Count;

        for (int i = 0; i < 100; i++)
        {
            await tickProcessor.ProcessTickAsync();

            var currentUnitCount = gameStateCache.GetUnitsForMatch(_context.MatchId).Count;
            if (currentUnitCount > initialUnitCount)
            {
                var units = gameStateCache.GetUnitsForMatch(_context.MatchId);
                var newSoldier = units.OrderByDescending(u => u.Id).First(u => u.Type == UnitType.Soldier);
                _scenarioContext["NewSoldier"] = newSoldier;
                break;
            }
        }
    }

    [When(@"player 1 queues research for RangedDamage2 without RangedDamage1")]
    public async Task WhenPlayer1QueuesResearchForRangedDamage2WithoutRangedDamage1()
    {
        var factory = _scenarioContext.ScenarioContainer.Resolve<ApiWebApplicationFactory>();
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", _context.Player1Token);

        var techLabId = (int)_scenarioContext["TechLabId"];

        var request = new QueueCommandsRequest
        {
            Commands = new[]
            {
                new CommandDto
                {
                    Type = "Research",
                    BuildingId = techLabId,
                    UpgradeType = "RangedDamage2"
                }
            }
        };

        var response = await client.PostAsJsonAsync($"/api/v1/matches/{_context.MatchId}/commands", request);
        _context.LastResponse = response;
    }

    [When(@"player 1 researches MeleeDamage upgrade")]
    public async Task WhenPlayer1ResearchesMeleeDamageUpgrade()
    {
        await WhenPlayer1QueuesAResearchCommandForMeleeDamageUpgrade();
        await WhenTheResearchCommandIsExecuted();
        await WhenTheUpgradeReaches100PercentResearchProgress();
    }

    [When(@"player 1 researches ArmorUpgrade upgrade")]
    public async Task WhenPlayer1ResearchesArmorUpgradeUpgrade()
    {
        var factory = _scenarioContext.ScenarioContainer.Resolve<ApiWebApplicationFactory>();
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", _context.Player1Token);

        var techLabId = (int)_scenarioContext["TechLabId"];

        var request = new QueueCommandsRequest
        {
            Commands = new[]
            {
                new CommandDto
                {
                    Type = "Research",
                    BuildingId = techLabId,
                    UpgradeType = "ArmorUpgrade"
                }
            }
        };

        var response = await client.PostAsJsonAsync($"/api/v1/matches/{_context.MatchId}/commands", request);

        var tickProcessor = factory.Services.GetRequiredService<IGameTickProcessor>();
        var gameStateCache = factory.Services.GetRequiredService<GameStateCache>();

        await tickProcessor.ProcessTickAsync();

        // Keep processing ticks until armor upgrade is complete
        for (int i = 0; i < 200; i++)
        {
            await tickProcessor.ProcessTickAsync();

            var match = gameStateCache.GetMatch(_context.MatchId);
            var player = match!.Players.First(p => p.PlayerId == _context.Player1Id);

            var upgrade = player.Upgrades.FirstOrDefault(u => u.UpgradeType.ToString() == "ArmorUpgrade");
            if (upgrade?.IsCompleted == true)
            {
                break;
            }
        }
    }

    [When(@"player 1 completes MeleeDamage upgrade")]
    public async Task WhenPlayer1CompletesMeleeDamageUpgrade()
    {
        await WhenPlayer1ResearchesMeleeDamageUpgrade();
    }

    [When(@"player 1 completes MeleeDamage2 upgrade")]
    public async Task WhenPlayer1CompletesMeleeDamage2Upgrade()
    {
        var factory = _scenarioContext.ScenarioContainer.Resolve<ApiWebApplicationFactory>();
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", _context.Player1Token);

        var techLabId = (int)_scenarioContext["TechLabId"];

        var request = new QueueCommandsRequest
        {
            Commands = new[]
            {
                new CommandDto
                {
                    Type = "Research",
                    BuildingId = techLabId,
                    UpgradeType = "MeleeDamage2"
                }
            }
        };

        var response = await client.PostAsJsonAsync($"/api/v1/matches/{_context.MatchId}/commands", request);

        var tickProcessor = factory.Services.GetRequiredService<IGameTickProcessor>();
        var gameStateCache = factory.Services.GetRequiredService<GameStateCache>();

        await tickProcessor.ProcessTickAsync();

        // Keep processing ticks until MeleeDamage2 upgrade is complete
        for (int i = 0; i < 200; i++)
        {
            await tickProcessor.ProcessTickAsync();

            var match = gameStateCache.GetMatch(_context.MatchId);
            var player = match!.Players.First(p => p.PlayerId == _context.Player1Id);

            var upgrade = player.Upgrades.FirstOrDefault(u => u.UpgradeType.ToString() == "MeleeDamage2");
            if (upgrade?.IsCompleted == true)
            {
                break;
            }
        }
    }

    [Then(@"the command is accepted and queued")]
    public void ThenTheCommandIsAcceptedAndQueued()
    {
        _context.LastResponse.Should().NotBeNull();
        _context.LastResponse!.IsSuccessStatusCode.Should().BeTrue();
        _context.LastCommandResponse.Should().NotBeNull();
    }

    [Then(@"the response indicates (\d+) command queued")]
    public void ThenTheResponseIndicatesCommandQueued(int expectedCount)
    {
        _context.LastCommandResponse.Should().NotBeNull();
        _context.LastCommandResponse!.QueuedCount.Should().Be(expectedCount);
    }

    [Then(@"an upgrade is created with 0% research progress")]
    public void ThenAnUpgradeIsCreatedWith0PercentResearchProgress()
    {
        var factory = _scenarioContext.ScenarioContainer.Resolve<ApiWebApplicationFactory>();
        var gameStateCache = factory.Services.GetRequiredService<GameStateCache>();

        var match = gameStateCache.GetMatch(_context.MatchId);
        var player = match!.Players.First(p => p.PlayerId == _context.Player1Id);

        var upgrade = player.Upgrades.FirstOrDefault(u => u.UpgradeType.ToString() == "MeleeDamage");

        upgrade.Should().NotBeNull();
        upgrade!.ResearchProgress.Should().Be(0);
        upgrade.IsCompleted.Should().BeFalse();
    }

    [Then(@"player 1's resources are decreased by the upgrade cost")]
    public void ThenPlayer1ResourcesAreDecreasedByTheUpgradeCost()
    {
        var factory = _scenarioContext.ScenarioContainer.Resolve<ApiWebApplicationFactory>();
        var gameStateCache = factory.Services.GetRequiredService<GameStateCache>();

        var match = gameStateCache.GetMatch(_context.MatchId);
        var currentResources = match!.GetPlayerResources(_context.Player1Id);
        var initialResources = (int)_scenarioContext["InitialResources"];

        currentResources.Should().BeLessThan(initialResources);
    }

    [Then(@"the upgrade research progress increases")]
    public void ThenTheUpgradeResearchProgressIncreases()
    {
        var factory = _scenarioContext.ScenarioContainer.Resolve<ApiWebApplicationFactory>();
        var gameStateCache = factory.Services.GetRequiredService<GameStateCache>();

        var match = gameStateCache.GetMatch(_context.MatchId);
        var player = match!.Players.First(p => p.PlayerId == _context.Player1Id);

        var upgrade = player.Upgrades.FirstOrDefault(u => u.UpgradeType.ToString() == "MeleeDamage");

        upgrade.Should().NotBeNull();
        upgrade!.ResearchProgress.Should().BeGreaterThan(0);
    }

    [Then(@"the upgrade is marked as completed")]
    public void ThenTheUpgradeIsMarkedAsCompleted()
    {
        var factory = _scenarioContext.ScenarioContainer.Resolve<ApiWebApplicationFactory>();
        var gameStateCache = factory.Services.GetRequiredService<GameStateCache>();

        var match = gameStateCache.GetMatch(_context.MatchId);
        var player = match!.Players.First(p => p.PlayerId == _context.Player1Id);

        var upgrade = player.Upgrades.FirstOrDefault(u => u.UpgradeType.ToString() == "MeleeDamage");

        upgrade.Should().NotBeNull();
        upgrade!.IsCompleted.Should().BeTrue();
        upgrade.ResearchProgress.Should().Be(100);
    }

    [Then(@"all soldier units have increased attack damage")]
    public void ThenAllSoldierUnitsHaveIncreasedAttackDamage()
    {
        var factory = _scenarioContext.ScenarioContainer.Resolve<ApiWebApplicationFactory>();
        var gameStateCache = factory.Services.GetRequiredService<GameStateCache>();

        var units = gameStateCache.GetUnitsForMatch(_context.MatchId);
        var soldiers = units.Where(u => u.Type == UnitType.Soldier && u.PlayerId == _context.Player1Id).ToList();

        soldiers.Should().NotBeEmpty();

        // Base damage for Soldier should be increased by upgrade
        // This assumes units have a method to get effective damage
        foreach (var soldier in soldiers)
        {
            soldier.AttackDamage.Should().BeGreaterThan(10); // Assuming base damage is 10
        }
    }

    [Then(@"the new unit spawns with upgraded attack damage")]
    public void ThenTheNewUnitSpawnsWithUpgradedAttackDamage()
    {
        var newSoldier = (Unit)_scenarioContext["NewSoldier"];
        newSoldier.AttackDamage.Should().BeGreaterThan(10); // Assuming base damage is 10
    }

    [Then(@"the error indicates building is not a TechLab")]
    public async Task ThenTheErrorIndicatesBuildingIsNotATechLab()
    {
        var content = await _context.LastResponse!.Content.ReadAsStringAsync();
        content.Should().ContainAny("TechLab", "techlab", "TECHLAB");
    }

    [Then(@"the error indicates prerequisite upgrade not completed")]
    public async Task ThenTheErrorIndicatesPrerequisiteUpgradeNotCompleted()
    {
        var content = await _context.LastResponse!.Content.ReadAsStringAsync();
        content.Should().ContainAny("prerequisite", "Prerequisite", "PREREQUISITE");
    }

    [Then(@"both upgrades are completed")]
    public void ThenBothUpgradesAreCompleted()
    {
        var factory = _scenarioContext.ScenarioContainer.Resolve<ApiWebApplicationFactory>();
        var gameStateCache = factory.Services.GetRequiredService<GameStateCache>();

        var match = gameStateCache.GetMatch(_context.MatchId);
        var player = match!.Players.First(p => p.PlayerId == _context.Player1Id);

        var meleeDamage = player.Upgrades.FirstOrDefault(u => u.UpgradeType.ToString() == "MeleeDamage");
        var armorUpgrade = player.Upgrades.FirstOrDefault(u => u.UpgradeType.ToString() == "ArmorUpgrade");

        meleeDamage.Should().NotBeNull();
        meleeDamage!.IsCompleted.Should().BeTrue();

        armorUpgrade.Should().NotBeNull();
        armorUpgrade!.IsCompleted.Should().BeTrue();
    }

    [Then(@"units have both increased damage and armor")]
    public void ThenUnitsHaveBothIncreasedDamageAndArmor()
    {
        var factory = _scenarioContext.ScenarioContainer.Resolve<ApiWebApplicationFactory>();
        var gameStateCache = factory.Services.GetRequiredService<GameStateCache>();

        var units = gameStateCache.GetUnitsForMatch(_context.MatchId);
        var soldiers = units.Where(u => u.Type == UnitType.Soldier && u.PlayerId == _context.Player1Id).ToList();

        soldiers.Should().NotBeEmpty();

        foreach (var soldier in soldiers)
        {
            soldier.AttackDamage.Should().BeGreaterThan(10); // Base + MeleeDamage upgrade
            soldier.Armor.Should().BeGreaterThan(0); // Base + ArmorUpgrade
        }
    }

    [Then(@"soldier units have attack damage increased by both tiers")]
    public void ThenSoldierUnitsHaveAttackDamageIncreasedByBothTiers()
    {
        var factory = _scenarioContext.ScenarioContainer.Resolve<ApiWebApplicationFactory>();
        var gameStateCache = factory.Services.GetRequiredService<GameStateCache>();

        var units = gameStateCache.GetUnitsForMatch(_context.MatchId);
        var soldiers = units.Where(u => u.Type == UnitType.Soldier && u.PlayerId == _context.Player1Id).ToList();

        soldiers.Should().NotBeEmpty();

        // With both MeleeDamage and MeleeDamage2, damage should be significantly higher
        foreach (var soldier in soldiers)
        {
            soldier.AttackDamage.Should().BeGreaterThan(15); // Base (10) + tier 1 + tier 2
        }
    }
}
