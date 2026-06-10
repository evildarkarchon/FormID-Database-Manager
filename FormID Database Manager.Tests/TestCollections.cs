using Xunit;

namespace FormID_Database_Manager.Tests;

[CollectionDefinition("Database Tests", DisableParallelization = true)]
public sealed class DatabaseTestCollection
{
}

[CollectionDefinition("Integration Tests", DisableParallelization = true)]
public sealed class IntegrationTestCollection
{
}

[CollectionDefinition("Performance Tests", DisableParallelization = true)]
public sealed class PerformanceTestCollection
{
}

[CollectionDefinition("UI Tests", DisableParallelization = true)]
public sealed class UiTestCollection
{
}
