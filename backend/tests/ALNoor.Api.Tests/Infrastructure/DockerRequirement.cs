namespace ALNoor.Api.Tests.Infrastructure;

/// <summary>
/// Tests that need Docker are skipped locally when Docker is missing, but fail in CI (where Docker
/// must exist), so a green CI run always means they really executed.
/// </summary>
internal static class DockerRequirement
{
    public static void SkipOrFailIfUnavailable(bool available, string what, string? startError)
    {
        if (available)
        {
            return;
        }

        var message = $"{what} not available (Docker required): {startError}";
        if (string.Equals(Environment.GetEnvironmentVariable("CI"), "true", StringComparison.OrdinalIgnoreCase))
        {
            Assert.Fail(message);
        }

        Assert.Skip(message);
    }
}
