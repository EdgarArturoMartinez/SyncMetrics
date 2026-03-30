namespace SyncMetrics.Pipeline.UnitTests;

/// <summary>
/// Loads JSON fixture files embedded into the test assembly.
/// Fixtures live in {Source}/TestData/ and are marked as EmbeddedResource in the .csproj.
/// Using EmbeddedResource keeps tests self-contained — no file path fragility.
/// </summary>
internal static class TestFixtures
{
    public static string LoadJson(string fileName) => LoadJson("OpenMeteo", fileName);

    public static string LoadJson(string source, string fileName)
    {
        var assembly = typeof(TestFixtures).Assembly;
        var resourceName = $"SyncMetrics.Pipeline.UnitTests.{source}.TestData.{fileName}";

        using var stream = assembly.GetManifestResourceStream(resourceName)
            ?? throw new InvalidOperationException(
                $"Embedded resource '{resourceName}' not found. " +
                $"Ensure the file exists under {source}/TestData/ and is marked as EmbeddedResource.");

        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }
}
