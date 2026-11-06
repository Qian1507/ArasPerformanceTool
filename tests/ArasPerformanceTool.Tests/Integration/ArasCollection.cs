
using Xunit;

namespace ArasPerformanceTool.Tests.Integration;

[CollectionDefinition("Aras Integration")]
public sealed class ArasCollection
    : ICollectionFixture<ArasFixture>
{
}
