using System.Runtime.InteropServices;
using Xunit;

namespace FTMS.Api.IntegrationTests.Infrastructure;

/// <summary>
/// Whether a Docker daemon is reachable.
///
/// design: doc 08 decision 4 - integration tests run against real SQL Server via
/// Testcontainers, never SQLite, because our design leans on rowversion, filtered indexes,
/// ledger tables and the migration pipeline and SQLite can validate none of them.
///
/// That decision stands. This check exists only so a developer machine without Docker reports
/// these tests as SKIPPED rather than FAILED, which is honest: nothing was proven, as opposed
/// to something was proven broken. CI always has Docker, so CI always runs them for real.
/// </summary>
internal static class DockerAvailability
{
    private static readonly Lazy<bool> Available = new(Probe);

    internal const string SkipReason =
        "Docker is not available on this machine, so the Testcontainers SQL Server cannot start. "
        + "These tests run for real in CI. Install Docker Desktop, or start the repo's "
        + "docker-compose SQL Server, to run them locally.";

    internal static bool IsAvailable => Available.Value;

    /// <summary>
    /// Call at the top of any test that needs the container. Throws xUnit's skip signal when
    /// Docker is absent, which also stops the test touching an uninitialised fixture.
    /// </summary>
    internal static void RequireDocker() => Skip.IfNot(IsAvailable, SkipReason);

    private static bool Probe()
    {
        try
        {
            // The daemon listens on a named pipe on Windows and a unix socket elsewhere.
            // Checking for the endpoint is far quicker than letting Testcontainers time out.
            if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            {
                return Directory.Exists(@"\\.\pipe\") &&
                    Directory.GetFiles(@"\\.\pipe\").Any(pipe =>
                        pipe.Contains("docker", StringComparison.OrdinalIgnoreCase));
            }

            return File.Exists("/var/run/docker.sock")
                || Environment.GetEnvironmentVariable("DOCKER_HOST") is not null;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return false;
        }
    }
}
