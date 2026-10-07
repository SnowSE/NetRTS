namespace NetRts.Engine;

/// <summary>
/// SplitMix64 generator. Used instead of <see cref="Random"/> so a seed produces the
/// same map on every .NET version and platform — replays depend on it.
/// </summary>
internal sealed class Rng(int seed)
{
    private ulong _state = (ulong)(uint)seed * 0x9E3779B97F4A7C15UL + 0xD1B54A32D192ED03UL;

    public ulong NextULong()
    {
        var z = _state += 0x9E3779B97F4A7C15UL;
        z = (z ^ (z >> 30)) * 0xBF58476D1CE4E5B9UL;
        z = (z ^ (z >> 27)) * 0x94D049BB133111EBUL;
        return z ^ (z >> 31);
    }

    /// <summary>Uniform integer in [minInclusive, maxExclusive).</summary>
    public int Next(int minInclusive, int maxExclusive)
    {
        if (maxExclusive <= minInclusive)
        {
            return minInclusive;
        }

        var span = (ulong)(maxExclusive - minInclusive);
        return minInclusive + (int)(NextULong() % span);
    }
}
