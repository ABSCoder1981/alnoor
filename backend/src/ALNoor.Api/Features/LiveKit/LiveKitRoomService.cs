using System.Globalization;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.Options;

namespace ALNoor.Api.Features.LiveKit;

public sealed record LiveKitTrack(string Sid, string Source, bool Muted);

public sealed record LiveKitParticipant(
    string Identity,
    string Name,
    DateTimeOffset? JoinedAt,
    IReadOnlyList<LiveKitTrack> Tracks,
    IReadOnlyDictionary<string, string> Attributes)
{
    public LiveKitTrack? Microphone => Tracks.FirstOrDefault(t => t.Source == "MICROPHONE");

    public LiveKitTrack? Camera => Tracks.FirstOrDefault(t => t.Source == "CAMERA");
}

/// <summary>LiveKit could not be reached or refused a request (mapped to 502 RTC_UNAVAILABLE).</summary>
public sealed class LiveKitUnavailableException(string message, Exception? inner = null) : Exception(message, inner);

/// <summary>Server-side LiveKit RoomService calls used by sessions and moderation (PRD 14.4, 14.5).</summary>
public interface ILiveKitRoomService
{
    Task CreateRoomAsync(string room, CancellationToken ct);

    /// <summary>Participants currently in the room; empty if the room does not exist.</summary>
    Task<IReadOnlyList<LiveKitParticipant>> ListParticipantsAsync(string room, CancellationToken ct);

    Task MuteTrackAsync(string room, string identity, string trackSid, CancellationToken ct);

    /// <summary>Removes a participant; does nothing if they are not in the room.</summary>
    Task RemoveParticipantAsync(string room, string identity, CancellationToken ct);

    /// <summary>Closes the room and disconnects everyone; does nothing if the room no longer exists.</summary>
    Task DeleteRoomAsync(string room, CancellationToken ct);

    /// <summary>
    /// Sends a small server message (topic "alnoor.moderation") so clients can explain what happened, e.g.
    /// "The teacher muted you". Clients cannot send data themselves (PRD 14.3). Empty identities = everyone.
    /// </summary>
    Task SendNoticeAsync(string room, string type, IReadOnlyList<string> identities, CancellationToken ct);
}

/// <summary>
/// Calls LiveKit's Twirp HTTP API (POST /twirp/livekit.RoomService/&lt;Method&gt;, JSON) with a short-lived server token.
/// See docs/adr/0001-livekit-dotnet.md for why this is not a third-party SDK.
/// </summary>
public sealed class LiveKitRoomService(HttpClient http, LiveKitTokenService tokens, IOptions<LiveKitOptions> options) : ILiveKitRoomService
{
    /// <summary>PRD 14.4 room settings.</summary>
    public const int MaxParticipants = 10;
    public const int EmptyTimeoutSeconds = 600;
    public const int DepartureTimeoutSeconds = 60;

    /// <summary>ws://host:7880 → http://host:7880, wss:// → https://.</summary>
    public static Uri HttpBase(Uri url)
    {
        ArgumentNullException.ThrowIfNull(url);
        var builder = new UriBuilder(url) { Scheme = url.Scheme == "wss" ? "https" : "http", Path = "/" };
        if (url.IsDefaultPort)
        {
            builder.Port = -1;
        }

        return builder.Uri;
    }

    public Task CreateRoomAsync(string room, CancellationToken ct) =>
        SendAsync("CreateRoom", room, new
        {
            name = room,
            empty_timeout = EmptyTimeoutSeconds,
            departure_timeout = DepartureTimeoutSeconds,
            max_participants = MaxParticipants,
        }, ignoreNotFound: false, ct);

    public async Task<IReadOnlyList<LiveKitParticipant>> ListParticipantsAsync(string room, CancellationToken ct)
    {
        var body = await SendAsync("ListParticipants", room, new { room }, ignoreNotFound: true, ct);
        if (body is null || !body.Value.TryGetProperty("participants", out var list) || list.ValueKind != JsonValueKind.Array)
        {
            return [];
        }

        return list.EnumerateArray().Select(ParseParticipant).ToList();
    }

    public Task MuteTrackAsync(string room, string identity, string trackSid, CancellationToken ct) =>
        SendAsync("MutePublishedTrack", room, new { room, identity, track_sid = trackSid, muted = true }, ignoreNotFound: false, ct);

    public Task RemoveParticipantAsync(string room, string identity, CancellationToken ct) =>
        SendAsync("RemoveParticipant", room, new { room, identity }, ignoreNotFound: true, ct);

    public Task DeleteRoomAsync(string room, CancellationToken ct) =>
        SendAsync("DeleteRoom", room, new { room }, ignoreNotFound: true, ct);

    public const string NoticeTopic = "alnoor.moderation";

    public Task SendNoticeAsync(string room, string type, IReadOnlyList<string> identities, CancellationToken ct)
    {
        var data = Convert.ToBase64String(JsonSerializer.SerializeToUtf8Bytes(new { type }));
        return SendAsync("SendData", room, new { room, data, kind = "RELIABLE", topic = NoticeTopic, destination_identities = identities }, ignoreNotFound: true, ct);
    }

    private async Task<JsonElement?> SendAsync(string method, string room, object payload, bool ignoreNotFound, CancellationToken ct)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, new Uri(HttpBase(options.Value.Url!), $"twirp/livekit.RoomService/{method}"))
        {
            Content = JsonContent.Create(payload),
        };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", tokens.CreateServerToken(room));

        HttpResponseMessage response;
        try
        {
            response = await http.SendAsync(request, ct);
        }
        catch (HttpRequestException ex)
        {
            throw new LiveKitUnavailableException($"LiveKit {method} failed: {ex.Message}", ex);
        }
        catch (TaskCanceledException ex) when (!ct.IsCancellationRequested)
        {
            throw new LiveKitUnavailableException($"LiveKit {method} timed out.", ex);
        }

        using (response)
        {
            if (response.IsSuccessStatusCode)
            {
                return await response.Content.ReadFromJsonAsync<JsonElement>(ct);
            }

            if (ignoreNotFound && response.StatusCode == HttpStatusCode.NotFound)
            {
                return null;
            }

            // Twirp error bodies are {"code":"...","msg":"..."}; log-safe (no secrets).
            var detail = await response.Content.ReadAsStringAsync(ct);
            throw new LiveKitUnavailableException($"LiveKit {method} returned {(int)response.StatusCode}: {detail[..Math.Min(200, detail.Length)]}");
        }
    }

    private static LiveKitParticipant ParseParticipant(JsonElement p)
    {
        string Text(JsonElement e, string name) => e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString()! : string.Empty;

        DateTimeOffset? joined = null;
        if (p.TryGetProperty("joined_at", out var j))
        {
            // int64 is a JSON string in protobuf JSON; accept a number too.
            var seconds = j.ValueKind == JsonValueKind.String && long.TryParse(j.GetString(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var s) ? s
                : j.ValueKind == JsonValueKind.Number ? j.GetInt64() : 0;
            joined = seconds > 0 ? DateTimeOffset.FromUnixTimeSeconds(seconds) : null;
        }

        var tracks = p.TryGetProperty("tracks", out var t) && t.ValueKind == JsonValueKind.Array
            ? t.EnumerateArray().Select(x => new LiveKitTrack(
                Text(x, "sid"),
                Text(x, "source"),
                x.TryGetProperty("muted", out var m) && m.ValueKind == JsonValueKind.True)).ToList()
            : [];

        var attributes = p.TryGetProperty("attributes", out var a) && a.ValueKind == JsonValueKind.Object
            ? a.EnumerateObject().Where(x => x.Value.ValueKind == JsonValueKind.String).ToDictionary(x => x.Name, x => x.Value.GetString()!)
            : new Dictionary<string, string>();

        return new LiveKitParticipant(Text(p, "identity"), Text(p, "name"), joined, tracks, attributes);
    }
}
