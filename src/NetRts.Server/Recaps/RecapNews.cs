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
        new(new DateOnly(2026, 11, 5),
            "New scoring: every point counts",
            "League points changed. Until now only the top four of each heat scored, earning their margins over the bots below "
            + "them, so a runaway winner got nearly three times its score and everyone from fifth down got zero. Now every bot "
            + "earns its whole final score as points in every heat, win or lose: everything it gathers, builds and destroys counts, "
            + "wherever it finishes. The preseason, season and championship ladders were all recounted under the new rule tonight, "
            + "so today's ladder moves come from the recount, not from new matches: the top six stayed close, and bots that played "
            + "solid heats without finishing top four climbed. A bot's lowest heat of a league night is still dropped when it plays "
            + "two or more.",
            "New scoring: every point counts",
            "League points changed. Until now only the top four badgers of each heat scored, earning their margins over the "
            + "badgers below them, so a runaway winner got nearly three times its score and everyone from fifth down got zero. "
            + "Now every badger earns its whole final score as points in every heat, win or lose: every grub it gathers and "
            + "everything it builds and knocks down counts, wherever it finishes. The preseason, season and championship ladders "
            + "were all recounted under the new rule tonight, so today's ladder moves come from the recount, not from new brawls: "
            + "the top six stayed close, and badgers that played solid heats without finishing top four climbed. A badger's lowest "
            + "heat of a league night is still dropped when it plays two or more.",
            ["Points = your whole score, every heat", "Win or lose, nobody gets zero for showing up and playing", "All ladders recounted tonight", "Lowest heat of a night still dropped"],
            ["Points = your whole score, every heat", "Win or lose, nobody gets zero for showing up and playing", "All ladders recounted tonight", "Lowest heat of a night still dropped"]),
        new(new DateOnly(2026, 11, 5),
            "League nights: join again every round",
            "League matches now keep one name all night, like week3, and bots join them one round at a time. To play a round, "
            + "join the match by its name; when your heat is over, join again for the next round (you can rejoin the moment your "
            + "heat ends). A bot that waits without rejoining is told JOIN_NEXT_ROUND. Heats are named week3-r1, or week3-r1-h1 "
            + "and week3-r1-h2 when a round has several. The bot runner rejoins by itself. Update your bot before the next league night.",
            "League nights: join again every round",
            "League brawls now keep one name all night, like week3, and badgers join them one round at a time. To play a round, "
            + "join the brawl by its name; when your heat is over, join again for the next round (you can rejoin the moment your "
            + "heat ends). A badger that waits without rejoining is told JOIN_NEXT_ROUND. Heats are named week3-r1, or week3-r1-h1 "
            + "and week3-r1-h2 when a round has several. The bot runner rejoins by itself. Update your badger before the next league night.",
            ["One name all night, e.g. week3", "Join again after every heat", "Waiting without rejoining: JOIN_NEXT_ROUND", "Heats: week3-r1, week3-r1-h1…"],
            ["One name all night, e.g. week3", "Join again after every heat", "Waiting without rejoining: JOIN_NEXT_ROUND", "Heats: week3-r1, week3-r1-h1…"]),
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
