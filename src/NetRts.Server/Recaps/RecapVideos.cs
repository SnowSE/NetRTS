using Azure.Storage.Blobs;
using Azure.Storage.Blobs.Models;
using Azure.Storage.Sas;
using Microsoft.Extensions.Options;

namespace NetRts.Server.Recaps;

/// <summary>
/// Turns an episode's video (a blob name in the recaps container) into a link that plays it: a
/// read-only SAS URL good for a few hours, so the container itself stays private. Signed with the
/// account key when the connection has one (Azurite locally), otherwise with a user delegation key
/// from the server's managed identity.
/// </summary>
public sealed class RecapVideos(IServiceProvider services, IOptions<RecapOptions> recap, TimeProvider time)
{
    private static readonly TimeSpan LinkLifetime = TimeSpan.FromHours(4);
    private readonly SemaphoreSlim _keyGate = new(1, 1);
    private UserDelegationKey? _key;

    private BlobServiceClient? Client => services.GetService<BlobServiceClient>();

    /// <summary>Whether videos stored as blob names can be played (blob storage is configured).</summary>
    public bool Configured => Client is not null;

    /// <summary>A playable URL for a blob name or absolute URL, or null if it can't be served.</summary>
    public async Task<Uri?> UrlAsync(string video, CancellationToken ct)
    {
        if (Uri.TryCreate(video, UriKind.Absolute, out var absolute) && absolute.Scheme is "https" or "http")
        {
            return absolute;
        }

        if (Client is not { } client || string.IsNullOrWhiteSpace(video))
        {
            return null;
        }

        var blob = client.GetBlobContainerClient(recap.Value.Container).GetBlobClient(video);
        var expires = time.GetUtcNow() + LinkLifetime;
        if (blob.CanGenerateSasUri)
        {
            return blob.GenerateSasUri(BlobSasPermissions.Read, expires);
        }

        var sas = new BlobSasBuilder(BlobSasPermissions.Read, expires)
        {
            BlobContainerName = recap.Value.Container,
            BlobName = video,
            Resource = "b",
        };
        var key = await DelegationKeyAsync(client, ct);
        return new BlobUriBuilder(blob.Uri) { Sas = sas.ToSasQueryParameters(key, client.AccountName) }.ToUri();
    }

    /// <summary>A user delegation key valid well past any link signed with it; renewed when it gets close.</summary>
    private async Task<UserDelegationKey> DelegationKeyAsync(BlobServiceClient client, CancellationToken ct)
    {
        await _keyGate.WaitAsync(ct);
        try
        {
            var now = time.GetUtcNow();
            if (_key is null || _key.SignedExpiresOn - now < LinkLifetime + TimeSpan.FromMinutes(10))
            {
                _key = (await client.GetUserDelegationKeyAsync(now.AddMinutes(-5), now.AddDays(1), ct)).Value;
            }

            return _key;
        }
        finally
        {
            _keyGate.Release();
        }
    }
}
