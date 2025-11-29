using Xunit;
using FlowVault.BackendHost.Services;
using FlowVault.BackendHost.Persistence;
using Microsoft.Extensions.Logging.Abstractions;

namespace FlowVault.Tests;

public class IndexerTests
{
    [Fact]
    public async Task IndexerDetectsCSharpFunctions()
    {
        // Arrange
        var tempDir = Path.Combine(Path.GetTempPath(), "FlowVaultTest_" + Guid.NewGuid());
        Directory.CreateDirectory(tempDir);

        var testFile = Path.Combine(tempDir, "Test.cs");
        await File.WriteAllTextAsync(testFile, @"
namespace TestNamespace
{
    public class TestClass
    {
        public void TestMethod() { }
        private int Calculate(int x, int y) => x + y;
    }
}");

        try
        {
            var db = new DatabaseService(NullLogger<DatabaseService>.Instance);
            var indexer = new IndexerService(NullLogger<IndexerService>.Instance, db);

            var projectId = Guid.NewGuid();

            // Act
            await indexer.ReindexProjectAsync(tempDir, projectId, CancellationToken.None);

            // Assert
            var files = await db.SearchFilesAsync("Test", projectId);
            Assert.Single(files);

            var file = files[0];
            Assert.Equal("csharp", file.Language);
            Assert.Contains(file.Functions, f => f.Name == "TestMethod");
            Assert.Contains(file.Functions, f => f.Name == "Calculate");
            Assert.Contains(file.Classes, c => c.Name == "TestClass");
        }
        finally
        {
            Directory.Delete(tempDir, true);
        }
    }

    [Fact]
    public async Task IndexerDetectsTODOs()
    {
        // Arrange
        var tempDir = Path.Combine(Path.GetTempPath(), "FlowVaultTest_" + Guid.NewGuid());
        Directory.CreateDirectory(tempDir);

        var testFile = Path.Combine(tempDir, "WithTodos.cs");
        await File.WriteAllTextAsync(testFile, @"
// TODO: Fix this bug
public class MyClass
{
    // TODO: Add validation
    public void Process() { }
}");

        try
        {
            var db = new DatabaseService(NullLogger<DatabaseService>.Instance);
            var indexer = new IndexerService(NullLogger<IndexerService>.Instance, db);

            var projectId = Guid.NewGuid();

            // Act
            await indexer.ReindexProjectAsync(tempDir, projectId, CancellationToken.None);

            // Assert
            var files = await db.SearchFilesAsync("WithTodos", projectId);
            Assert.Single(files);

            var file = files[0];
            Assert.Equal(2, file.TodoCount);
        }
        finally
        {
            Directory.Delete(tempDir, true);
        }
    }

    [Fact]
    public async Task IndexerCalculatesHotspotScore()
    {
        // Arrange
        var tempDir = Path.Combine(Path.GetTempPath(), "FlowVaultTest_" + Guid.NewGuid());
        Directory.CreateDirectory(tempDir);

        // Create a file with many functions (should have higher hotspot)
        var complexFile = Path.Combine(tempDir, "Complex.cs");
        await File.WriteAllTextAsync(complexFile, @"
public class Complex
{
    public void Method1() { }
    public void Method2() { }
    public void Method3() { }
    public void Method4() { }
    public void Method5() { }
}");

        // Create a simple file
        var simpleFile = Path.Combine(tempDir, "Simple.cs");
        await File.WriteAllTextAsync(simpleFile, @"
public class Simple
{
    public void OnlyMethod() { }
}");

        try
        {
            var db = new DatabaseService(NullLogger<DatabaseService>.Instance);
            var indexer = new IndexerService(NullLogger<IndexerService>.Instance, db);

            var projectId = Guid.NewGuid();

            // Act
            await indexer.ReindexProjectAsync(tempDir, projectId, CancellationToken.None);

            // Assert
            var complexResult = await db.GetFileSummaryAsync(complexFile);
            var simpleResult = await db.GetFileSummaryAsync(simpleFile);

            Assert.NotNull(complexResult);
            Assert.NotNull(simpleResult);
            Assert.True(complexResult.HotspotScore > simpleResult.HotspotScore,
                "Complex file should have higher hotspot score");
        }
        finally
        {
            Directory.Delete(tempDir, true);
        }
    }
}
