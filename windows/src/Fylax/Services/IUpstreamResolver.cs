namespace Fylax.Services;

public interface IUpstreamResolver
{
    Task<byte[]> ResolveAsync(byte[] query, int length, bool preferTcp, CancellationToken cancellationToken);
}
