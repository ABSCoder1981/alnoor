using System.ComponentModel.DataAnnotations;

namespace ALNoor.Api.Features.LiveKit;

/// <summary>
/// LiveKit connection settings. Url is given to clients; ApiKey/ApiSecret never leave the server.
/// Set via environment variables (LiveKit__Url, LiveKit__ApiKey, LiveKit__ApiSecret) or user-secrets.
/// </summary>
public sealed class LiveKitOptions
{
    public const string SectionName = "LiveKit";

    /// <summary>Minimum secret length: HS256 needs a key of at least 256 bits (32 bytes).</summary>
    public const int MinimumSecretLength = 32;

    /// <summary>WebSocket URL clients connect to, e.g. wss://alnoor.livekit.cloud or ws://192.168.1.8:7880 in development.</summary>
    [Required]
    public Uri? Url { get; set; }

    [Required]
    public string ApiKey { get; set; } = string.Empty;

    [Required]
    [MinLength(MinimumSecretLength)]
    public string ApiSecret { get; set; } = string.Empty;
}
