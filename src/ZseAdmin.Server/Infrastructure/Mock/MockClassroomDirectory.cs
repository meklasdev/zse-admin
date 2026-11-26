using ZseAdmin.Contracts;
using ZseAdmin.Server.Abstractions;

namespace ZseAdmin.Server.Infrastructure.Mock;

/// <summary>Pracownie z konfiguracji/AD (MVP: dane przykładowe).</summary>
public sealed class MockClassroomDirectory : IClassroomDirectory
{
    private readonly IClock _clock;

    public MockClassroomDirectory(IClock clock)
    {
        _clock = clock;
    }

    public Task<ClassroomQueryResult> ListAsync(CancellationToken cancellationToken = default)
    {
        var result = new ClassroomQueryResult(
            SchoolSeed.Classrooms(),
            QueryMetadata.Complete(_clock.UtcNow));

        return Task.FromResult(result);
    }
}
