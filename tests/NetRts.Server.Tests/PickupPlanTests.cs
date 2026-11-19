using NetRts.Protocol;
using NetRts.Server.Pickup;

namespace NetRts.Server.Tests;

public class PickupPlanTests
{
    private static PickupPlanDto Plan(params PickupBuyDto[] start) => new() { Start = start };

    [Fact]
    public void Every_deck_is_a_valid_plan()
    {
        Assert.Equal(["rush", "turtle", "boom"], PickupPlans.Decks.Select(d => d.Id));
        Assert.All(PickupPlans.Decks, d => Assert.Null(PickupPlans.Validate(d.Plan)));
    }

    [Fact]
    public void A_start_pile_over_1500_grubs_is_refused()
    {
        Assert.Contains("1,500", PickupPlans.Validate(Plan(new PickupBuyDto("Soldier", 15), new PickupBuyDto("Scout", 1))));
        Assert.Null(PickupPlans.Validate(Plan(new PickupBuyDto("Soldier", 15))));
    }

    [Fact]
    public void Four_of_one_building_is_refused() =>
        Assert.NotNull(PickupPlans.Validate(Plan(new PickupBuyDto("GuardTower", 4))));

    [Fact]
    public void Seven_steps_are_refused() =>
        Assert.NotNull(PickupPlans.Validate(new PickupPlanDto
        {
            Steps = Enumerable.Range(1, 7).Select(i => new PickupStepDto { Kind = "workers", Count = 5 + i }).ToList(),
        }));

    [Fact]
    public void Forever_takes_one_to_three_cards()
    {
        Assert.NotNull(PickupPlans.Validate(new PickupPlanDto { Forever = [] }));
        Assert.NotNull(PickupPlans.Validate(new PickupPlanDto { Forever = ["Soldier", "Soldier", "Archer", "Scout"] }));
        Assert.Null(PickupPlans.Validate(new PickupPlanDto { Forever = ["Worker", "GuardTower", "Weapons1"] }));
    }

    [Fact]
    public void Unknown_items_and_wrong_kinds_are_refused()
    {
        Assert.NotNull(PickupPlans.Validate(Plan(new PickupBuyDto("Dragon", 1))));
        Assert.NotNull(PickupPlans.Validate(Plan(new PickupBuyDto("Trebuchet", 1))));
        Assert.NotNull(PickupPlans.Validate(new PickupPlanDto { Steps = [new() { Kind = "train", Item = "Barracks", Count = 3 }] }));
        Assert.NotNull(PickupPlans.Validate(new PickupPlanDto { Steps = [new() { Kind = "build", Item = "Soldier" }] }));
        Assert.NotNull(PickupPlans.Validate(new PickupPlanDto { Steps = [new() { Kind = "dance" }] }));
        Assert.NotNull(PickupPlans.Validate(new PickupPlanDto { Attack = new() { Target = "moon" } }));
    }

    [Fact]
    public void The_rush_kit_spends_everything_and_adds_the_free_housing()
    {
        var kit = PickupPlans.KitFor(PickupPlans.Decks.Single(d => d.Id == "rush").Plan);

        Assert.Equal(0, kit.Resources);
        Assert.Equal(10, kit.Units.Count(u => u == UnitType.Soldier));
        Assert.Equal(4, kit.Units.Count(u => u == UnitType.Archer));
        Assert.Equal([BuildingType.Barracks], kit.Buildings);
    }
}
