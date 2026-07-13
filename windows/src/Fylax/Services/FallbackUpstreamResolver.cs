namespace Fylax.Services;

public sealed class FallbackUpstreamResolver : IUpstreamResolver
{
    private readonly IReadOnlyList<IUpstreamResolver> _resolvers;

    public FallbackUpstreamResolver(IReadOnlyList<IUpstreamResolver> resolvers)
    {
        _resolvers = resolvers;
    }

    public async Task<byte[]> ResolveAsync(byte[] query, int length, bool preferTcp, CancellationToken cancellationToken)
    {
        Exception? last = null;
        foreach (var resolver in _resolvers)
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                return await resolver.ResolveAsync(query, length, preferTcp, cancellationToken);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                last = ex;
            }
        }

        throw last ?? new InvalidOperationException("No upstream resolvers configured.");
    }
}
