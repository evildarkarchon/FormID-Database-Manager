using Xunit;

namespace FormID_Database_Manager.Tests;

// Define test collections to control parallelization
// Tests in the same collection run sequentially, different collections run in parallel

[CollectionDefinition("Database Tests", DisableParallelization = true)]
public class DatabaseTestCollection
{
    // This collection is for tests that use the database
    // They should not run in parallel to avoid conflicts
}

[CollectionDefinition("UI Tests", DisableParallelization = true)]
public class UITestCollection
{
    // This collection is for UI tests that may share UI resources
    // They should not run in parallel to avoid conflicts
}

[CollectionDefinition("Integration Tests", DisableParallelization = true)]
public class IntegrationTestCollection
{
    // Integration tests run sequentially to avoid resource contention during coverage runs
}

[CollectionDefinition("Performance Tests", DisableParallelization = true)]
public class PerformanceTestCollection
{
    // Performance tests should run sequentially for accurate measurements
}

// Unit tests don't need a collection - they run in parallel by default
