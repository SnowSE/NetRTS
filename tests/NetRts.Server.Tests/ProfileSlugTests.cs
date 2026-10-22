using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using NetRts.Server.Auth;
using NetRts.Server.Data;

namespace NetRts.Server.Tests;

public class ProfileSlugTests(ServerFactory factory) : IClassFixture<ServerFactory>
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Theory]
    [InlineData("Jordan", "jordan")]
    [InlineData("Zoë O'Brien", "zoe-o-brien")]
    [InlineData("  ~Cool__Coder~ ", "cool-coder")]
    [InlineData("🦡🦡", "player")]
    public void Slugs_are_lower_case_letters_and_digits_joined_by_dashes(string name, string slug) =>
        Assert.Equal(slug, ProfileSlugs.Slugify(name));

    [Fact]
    public async Task People_with_the_same_name_get_numbered_slugs_in_the_order_they_joined()
    {
        var name = $"Twin {Guid.NewGuid():N}"[..13];
        var first = await factory.NewAccountAsync(nickname: name);
        var second = await factory.NewAccountAsync(nickname: name);

        await using var scope = factory.Services.CreateAsyncScope();
        var slugs = await ProfileSlugs.LoadAsync(scope.ServiceProvider.GetRequiredService<NetRtsDb>(),
            scope.ServiceProvider.GetRequiredService<IOptions<DiscordOptions>>().Value, Ct);
        var slug = ProfileSlugs.Slugify(name);
        Assert.Equal(slug, slugs.Of(first.Id));
        Assert.Equal($"{slug}-2", slugs.Of(second.Id));
        Assert.Equal(second.Id, slugs.Find($"{slug}-2"));
        Assert.Equal(first.Id, slugs.Find(first.Id.ToString()));
    }
}
