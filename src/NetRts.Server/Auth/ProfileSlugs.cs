using System.Globalization;
using System.Text;
using Microsoft.EntityFrameworkCore;
using NetRts.Server.Data;

namespace NetRts.Server.Auth;

/// <summary>
/// The names in public profile URLs (#/player/jordan): each verified person's display name, lower case,
/// letters and digits joined by dashes. When two people's names come out the same, whoever joined
/// first keeps it and the next is jordan-2, then jordan-3. A slug follows its owner's name, so it
/// changes if they change their nickname.
/// </summary>
public sealed class ProfileSlugs
{
    private readonly Dictionary<Guid, string> _byAccount;
    private readonly Dictionary<string, Guid> _byslug;

    private ProfileSlugs(Dictionary<Guid, string> byAccount)
    {
        _byAccount = byAccount;
        _byslug = byAccount.ToDictionary(kv => kv.Value, kv => kv.Key, StringComparer.Ordinal);
    }

    /// <summary>The slugs of everyone verified right now.</summary>
    public static async Task<ProfileSlugs> LoadAsync(NetRtsDb db, DiscordOptions discord, CancellationToken ct)
    {
        var accounts = (await db.Accounts.AsNoTracking().ToListAsync(ct))
            .Where(a => Accounts.IsVerified(a, discord))
            .OrderBy(a => a.CreatedAt).ThenBy(a => a.Id);
        var byAccount = new Dictionary<Guid, string>();
        var taken = new HashSet<string>(StringComparer.Ordinal);
        foreach (var account in accounts)
        {
            var slug = Slugify(account.DisplayName);
            var candidate = slug;
            for (var n = 2; !taken.Add(candidate); n++)
            {
                candidate = $"{slug}-{n}";
            }

            byAccount[account.Id] = candidate;
        }

        return new ProfileSlugs(byAccount);
    }

    /// <summary>The account's slug, or null if they aren't verified.</summary>
    public string? Of(Guid? accountId) => accountId is { } id ? _byAccount.GetValueOrDefault(id) : null;

    /// <summary>The account a slug (or, for old links, an account id) names.</summary>
    public Guid? Find(string key) =>
        _byslug.TryGetValue(key.ToLowerInvariant(), out var id) ? id
        : Guid.TryParse(key, out var guid) && _byAccount.ContainsKey(guid) ? guid
        : null;

    /// <summary>"Zoë O'Brien" → "zoe-o-brien"; a name with no letters or digits → "player".</summary>
    public static string Slugify(string name)
    {
        var sb = new StringBuilder(name.Length);
        foreach (var c in name.Normalize(NormalizationForm.FormD))
        {
            if (CharUnicodeInfo.GetUnicodeCategory(c) == UnicodeCategory.NonSpacingMark)
            {
                continue;
            }

            if (char.IsAsciiLetterOrDigit(c))
            {
                sb.Append(char.ToLowerInvariant(c));
            }
            else if (sb.Length > 0 && sb[^1] != '-')
            {
                sb.Append('-');
            }
        }

        var slug = sb.ToString().Trim('-');
        return slug.Length == 0 ? "player" : slug.Length > 40 ? slug[..40].TrimEnd('-') : slug;
    }
}
