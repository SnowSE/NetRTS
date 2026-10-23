namespace NetRts.Server.Recaps;

/// <summary>
/// Game changes for the hosts to announce. Each item airs once, in the episode covering the day it
/// shipped. Add new ones at the top.
/// </summary>
public static class RecapNews
{
    private sealed record Item(DateOnly Day, string Headline, string Details, string SnowHeadline, string SnowDetails,
        string[] Facts, string[] SnowFacts);

    private static readonly Item[] Items =
    [
        new(new DateOnly(2026, 10, 23),
            "New unit: the Trebuchet",
            "Research SiegeEngineering at a TechLab, then build a SiegeWorkshop to train Trebuchets (API names: SiegeEngineering, "
            + "SiegeWorkshop, Trebuchet). It costs 350 ore, takes 45 ticks to build and is slow, but its range of 7 tiles outranges "
            + "guard towers (5). It can't hit anything within 3 tiles, so it needs an escort. Its shell lands on a tile 3 ticks after "
            + "firing and hits every enemy standing there, so units that keep moving dodge it; buildings can't. Details in the bot guide.",
            "New unit: the Snow Mobile",
            "The Snow Mobile, Snow College's old Snap-on tool truck in navy and orange with a cannon bolted to the roof, has joined "
            + "the brawl. Take Welding Technology at the GRSC Makerspace, then build the Washburn Welding Shop on the Richfield "
            + "campus to roll them out (API names: SiegeEngineering, SiegeWorkshop, Trebuchet). It costs 350 grubs, takes 45 ticks "
            + "to build and is slow, but it shells from 7 tiles out, beyond The Bell Tower's 5. It can't hit anything within 3 "
            + "tiles, so it needs an escort. Its cannonball lands on a tile 3 ticks after firing and hits every enemy there, so "
            + "badgers that keep moving dodge it; buildings can't. Details in the bot guide.",
            ["SiegeEngineering, then a SiegeWorkshop", "350 ore, 45 ticks, slow", "Range 7 (towers: 5), not within 3", "Shells land 3 ticks later: keep moving"],
            ["Welding Technology, then the Washburn Welding Shop", "350 grubs, 45 ticks, slow", "Range 7 (Bell Tower: 5), not within 3", "Cannonballs land 3 ticks later: keep moving"]),
    ];

    /// <summary>What shipped on <paramref name="day"/>, in the server's brand.</summary>
    public static IReadOnlyList<RecapNewsDto> For(DateOnly day, bool snow) =>
        Items.Where(i => i.Day == day)
             .Select(i => new RecapNewsDto
             {
                 Headline = snow ? i.SnowHeadline : i.Headline,
                 Details = snow ? i.SnowDetails : i.Details,
                 Facts = snow ? i.SnowFacts : i.Facts,
             })
             .ToList();
}
