using System.Globalization;
using System.Xml.Linq;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using NetRts.Server.Data;

namespace NetRts.Server.Recaps;

/// <summary>
/// The recap show as a podcast: an RSS feed (with Apple's podcast tags) of every episode that has an
/// MP3, for people who'd rather listen than watch. Podcast apps subscribe to /recaps/podcast.xml; each
/// episode's audio is at /recaps/{id}/audio.mp3, which redirects to a fresh link to the private blob.
/// </summary>
public static class RecapPodcast
{
    private static readonly XNamespace Itunes = "http://www.itunes.com/dtds/podcast-1.0.dtd";

    public static string ShowName(RecapOptions recap) =>
        string.Equals(recap.Brand, "classic", StringComparison.OrdinalIgnoreCase) ? "NetRTS Nightly" : "Badger Brawl Tonight";

    public static async Task<IResult> FeedAsync(HttpContext http, NetRtsDb db, IOptions<NetRtsOptions> options, IOptions<RecapOptions> recap,
        CancellationToken ct)
    {
        var site = (string.IsNullOrWhiteSpace(options.Value.PublicUrl) ? $"{http.Request.Scheme}://{http.Request.Host}" : options.Value.PublicUrl).TrimEnd('/');
        var show = ShowName(recap.Value);
        var about = "The nightly highlights show for our bot-programming RTS league: Zip the Scout, Sarge the Soldier and "
                    + "Quill the Archer run down the day's matches, new bots, the ladder, the upsets and who's gone missing.";
        var episodes = await db.RecapEpisodes.AsNoTracking().Where(e => e.Audio != "").OrderByDescending(e => e.Date).Take(365).ToListAsync(ct);
        var image = episodes.FirstOrDefault(e => e.Thumbnail != "") is { } latest ? $"{site}/recaps/{latest.Id}/thumbnail" : null;

        var channel = new XElement("channel",
            new XElement("title", show),
            new XElement("link", $"{site}/#/recaps"),
            new XElement("language", "en-us"),
            new XElement("description", about),
            new XElement(Itunes + "author", show),
            new XElement(Itunes + "summary", about),
            new XElement(Itunes + "explicit", "false"),
            new XElement(Itunes + "type", "episodic"),
            new XElement(Itunes + "category", new XAttribute("text", "Leisure"), new XElement(Itunes + "category", new XAttribute("text", "Video Games"))),
            image is null ? null : new XElement(Itunes + "image", new XAttribute("href", image)),
            image is null ? null : new XElement("image", new XElement("url", image), new XElement("title", show), new XElement("link", $"{site}/#/recaps")),
            episodes.Select(e => new XElement("item",
                new XElement("title", e.Title),
                new XElement("description", e.Summary),
                new XElement("link", $"{site}/#/recaps/{e.Id}"),
                new XElement("guid", new XAttribute("isPermaLink", "false"), e.Id.ToString()),
                new XElement("pubDate", AiredAt(e, options.Value.Zone, recap.Value.AnnounceAt).ToString("r", CultureInfo.InvariantCulture)),
                new XElement("enclosure", new XAttribute("url", $"{site}/recaps/{e.Id}/audio.mp3"),
                    new XAttribute("length", e.AudioBytes.ToString(CultureInfo.InvariantCulture)), new XAttribute("type", "audio/mpeg")),
                new XElement(Itunes + "duration", e.DurationSeconds.ToString(CultureInfo.InvariantCulture)),
                new XElement(Itunes + "episodeType", "full"),
                e.Thumbnail == "" ? null : new XElement(Itunes + "image", new XAttribute("href", $"{site}/recaps/{e.Id}/thumbnail")))));

        var rss = new XElement("rss", new XAttribute("version", "2.0"), new XAttribute(XNamespace.Xmlns + "itunes", Itunes), channel);
        return Results.Text("<?xml version=\"1.0\" encoding=\"utf-8\"?>\n" + rss, "application/rss+xml; charset=utf-8");
    }

    /// <summary>When the episode goes out: the morning after the day it covers, at the Discord announcement time.</summary>
    private static DateTimeOffset AiredAt(RecapEpisodeRecord e, TimeZoneInfo zone, TimeOnly announceAt)
    {
        var local = DateOnly.FromDateTime(e.Date).AddDays(1).ToDateTime(announceAt);
        return new DateTimeOffset(TimeZoneInfo.ConvertTimeToUtc(local, zone), TimeSpan.Zero);
    }
}
