using System.Collections.Concurrent;
using ALNoor.Api.Features.LiveKit;

namespace ALNoor.Api.Tests.Infrastructure;

/// <summary>In-memory LiveKit rooms for endpoint tests. Tests add participants and inspect the calls made.</summary>
public sealed class FakeLiveKitRoomService : ILiveKitRoomService
{
    private readonly ConcurrentDictionary<string, List<LiveKitParticipant>> _rooms = new();

    public ConcurrentQueue<string> Calls { get; } = new();

    /// <summary>When set, every call fails as if LiveKit were down.</summary>
    public bool Unavailable { get; set; }

    public bool RoomExists(string room) => _rooms.ContainsKey(room);

    public void AddParticipant(string room, Guid userId, string name, bool micPublished = true, bool micMuted = false, bool cameraOn = false)
    {
        var tracks = new List<LiveKitTrack>();
        if (micPublished)
        {
            tracks.Add(new LiveKitTrack($"TR_mic_{userId:N}", "MICROPHONE", micMuted));
        }

        if (cameraOn)
        {
            tracks.Add(new LiveKitTrack($"TR_cam_{userId:N}", "CAMERA", false));
        }

        var list = _rooms.GetOrAdd(room, _ => []);
        lock (list)
        {
            list.RemoveAll(p => p.Identity == userId.ToString());
            list.Add(new LiveKitParticipant(userId.ToString(), name, DateTimeOffset.UtcNow, tracks, new Dictionary<string, string>()));
        }
    }

    public IReadOnlyList<LiveKitParticipant> Participants(string room) =>
        _rooms.TryGetValue(room, out var list) ? [.. list] : [];

    private void Check(string call)
    {
        Calls.Enqueue(call);
        if (Unavailable)
        {
            throw new LiveKitUnavailableException("fake: LiveKit unavailable");
        }
    }

    public Task CreateRoomAsync(string room, CancellationToken ct)
    {
        Check($"CreateRoom {room}");
        _rooms.TryAdd(room, []);
        return Task.CompletedTask;
    }

    public Task<IReadOnlyList<LiveKitParticipant>> ListParticipantsAsync(string room, CancellationToken ct)
    {
        Check($"ListParticipants {room}");
        return Task.FromResult(Participants(room));
    }

    public Task MuteTrackAsync(string room, string identity, string trackSid, CancellationToken ct)
    {
        Check($"Mute {room} {identity} {trackSid}");
        if (_rooms.TryGetValue(room, out var list))
        {
            lock (list)
            {
                var i = list.FindIndex(p => p.Identity == identity);
                if (i >= 0)
                {
                    var p = list[i];
                    list[i] = p with { Tracks = p.Tracks.Select(t => t.Sid == trackSid ? t with { Muted = true } : t).ToList() };
                }
            }
        }

        return Task.CompletedTask;
    }

    public Task RemoveParticipantAsync(string room, string identity, CancellationToken ct)
    {
        Check($"Remove {room} {identity}");
        if (_rooms.TryGetValue(room, out var list))
        {
            lock (list)
            {
                list.RemoveAll(p => p.Identity == identity);
            }
        }

        return Task.CompletedTask;
    }

    public Task SendNoticeAsync(string room, string type, IReadOnlyList<string> identities, CancellationToken ct)
    {
        Check($"Notice {room} {type} [{string.Join(",", identities)}]");
        return Task.CompletedTask;
    }

    public Task DeleteRoomAsync(string room, CancellationToken ct)
    {
        Check($"DeleteRoom {room}");
        _rooms.TryRemove(room, out _);
        return Task.CompletedTask;
    }
}
