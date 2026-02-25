using System.Diagnostics;
using System.Text.Json;

namespace ClaudeAdoCompanion.Services;

/// <summary>
/// Gets Azure DevOps bearer tokens from the az CLI.
/// Caches tokens until 5 minutes before expiry.
/// </summary>
public class AzCliTokenService
{
    private const string AdoResource = "499b84ac-1321-427f-aa17-267ca6975798";

    private readonly ILogger<AzCliTokenService> _logger;
    private readonly SemaphoreSlim _lock = new(1, 1);

    private string? _cachedToken;
    private DateTimeOffset _expiresOn = DateTimeOffset.MinValue;

    public AzCliTokenService(ILogger<AzCliTokenService> logger)
    {
        _logger = logger;
    }

    public async Task<string> GetTokenAsync()
    {
        // Return cached token if still valid (with 5 min buffer)
        if (_cachedToken != null && DateTimeOffset.UtcNow < _expiresOn.AddMinutes(-5))
        {
            return _cachedToken;
        }

        await _lock.WaitAsync();
        try
        {
            // Double-check after acquiring lock
            if (_cachedToken != null && DateTimeOffset.UtcNow < _expiresOn.AddMinutes(-5))
            {
                return _cachedToken;
            }

            _logger.LogInformation("Refreshing ADO token via az CLI");

            var psi = new ProcessStartInfo
            {
                FileName = "cmd.exe",
                Arguments = $"/c az account get-access-token --resource {AdoResource} --output json",
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true,
            };

            using var process = Process.Start(psi)
                ?? throw new InvalidOperationException("Failed to start az CLI process");

            var stdout = await process.StandardOutput.ReadToEndAsync();
            var stderr = await process.StandardError.ReadToEndAsync();
            await process.WaitForExitAsync();

            if (process.ExitCode != 0)
            {
                _logger.LogError("az CLI failed (exit {ExitCode}): {Stderr}", process.ExitCode, stderr);
                throw new InvalidOperationException(
                    $"az account get-access-token failed (exit {process.ExitCode}). " +
                    "Run 'az login' to authenticate. " + stderr);
            }

            using var doc = JsonDocument.Parse(stdout);
            var root = doc.RootElement;

            _cachedToken = root.GetProperty("accessToken").GetString()
                ?? throw new InvalidOperationException("accessToken was null in az CLI response");

            if (root.TryGetProperty("expiresOn", out var expiresOnProp))
            {
                // az CLI returns expiresOn as a local time string like "2026-02-15 18:30:00.000000"
                if (DateTimeOffset.TryParse(expiresOnProp.GetString(), out var parsed))
                {
                    _expiresOn = parsed;
                }
                else
                {
                    // Default to 1 hour from now if we can't parse
                    _expiresOn = DateTimeOffset.UtcNow.AddHours(1);
                }
            }

            _logger.LogInformation("ADO token refreshed, expires at {ExpiresOn}", _expiresOn);
            return _cachedToken;
        }
        finally
        {
            _lock.Release();
        }
    }
}
