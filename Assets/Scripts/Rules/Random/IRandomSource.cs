namespace RithmaticDuels.Rules
{
    /// <summary>All randomness in the rules goes through this, so duels and tests are reproducible.</summary>
    public interface IRandomSource
    {
        /// <summary>Uniform integer in [0, maxExclusive).</summary>
        int NextInt(int maxExclusive);
    }
}
