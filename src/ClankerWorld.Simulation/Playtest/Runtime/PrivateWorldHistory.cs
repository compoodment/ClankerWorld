using System.Text.Json;

namespace ClankerWorld.Simulation.Playtest;

public sealed record ArchivedEventBatch(string Name, long PreviousFloor, long ThroughEventId, JsonElement Events);
public sealed record PrivateWorldHistorySegment(string? Parent, IReadOnlyList<ArchivedEventBatch> Streams,
    IReadOnlyList<BoatTripRequest>? ClosedBoatRequests = null);
public sealed record PrivateWorldHistoryCompaction(PrivateWorldRuntimeState State, PrivateWorldHistorySegment Segment);

/// <summary>Pure compaction plan; persistence must durably archive the segment before installing its state.</summary>
public static class PrivateWorldHistory
{
    public const int RecentEventLimit = 1024;
    public const int CompactionThreshold = 2048;
    public const int RecentBoatRequestLimit = 40;

    public static PrivateWorldHistoryCompaction Prepare(PrivateWorldRuntimeState state)
    {
        ArgumentNullException.ThrowIfNull(state);
        var transport = state.BoatTransport;
        var closed = transport.Requests.Where(request => request.SettledTick is not null).ToArray();
        // An order's latest native request remains authority for arrival or
        // recovery. Older retries and ordinary requests can still be archived.
        var boundRequests = (state.Instructions ?? [])
            .Where(instruction => instruction.Order?.Action == "travel_by_boat")
            .Select(instruction => instruction.Order!.BoatTravel?.RequestId)
            .OfType<string>().ToHashSet(StringComparer.Ordinal);
        var retired = closed.Take(Math.Max(0, closed.Length - RecentBoatRequestLimit))
            .Where(request => !boundRequests.Contains(request.Id)).ToArray();
        if (retired.Length > 0)
        {
            var retiredIds = retired.Select(request => request.Id).ToHashSet(StringComparer.Ordinal);
            transport = transport with
            {
                Requests = transport.Requests.Where(request => !retiredIds.Contains(request.Id)).ToArray(),
                RetiredRequestRanges = MergeRetiredRanges(transport.RetiredRequestRanges, retired),
            };
        }
        var archived = new List<ArchivedEventBatch>();
        var world = Window("world", state.Events, item => item.EventId, state.EventHistoryFloor, archived);
        var society = state.Society.Society;
        var social = Window("society", society.Events, item => item.EventId, society.EventHistoryFloor, archived);
        var inventory = society.Inventory;
        var items = Window("inventory", inventory.Events, item => item.EventId, inventory.EventHistoryFloor, archived);
        var cognition = state.Society.Cognition;
        var scheduler = Window("scheduler", cognition.Events, item => item.EventId, cognition.EventHistoryFloor, archived);
        var runtimes = cognition.Runtimes.Select(runtime =>
        {
            var window = Window($"cognition:{runtime.InhabitantId}", runtime.Events, item => item.EventId, runtime.EventHistoryFloor, archived);
            return runtime with { Events = window.Events, EventHistoryFloor = window.Floor };
        }).ToArray();
        if (archived.Count == 0 && retired.Length == 0)
        {
            return new(state, new(state.HistoryArchiveHead, []));
        }
        var compacted = state with
        {
            SchemaVersion = PrivateWorldRuntime.StateSchemaVersion,
            BoatTransport = transport,
            Events = world.Events,
            EventHistoryFloor = world.Floor,
            Society = state.Society with
            {
                Society = society with
                {
                    Events = social.Events,
                    EventHistoryFloor = social.Floor,
                    Inventory = inventory with { Events = items.Events, EventHistoryFloor = items.Floor },
                },
                Cognition = cognition with { Events = scheduler.Events, EventHistoryFloor = scheduler.Floor, Runtimes = runtimes },
            },
        };
        return new(compacted, new(state.HistoryArchiveHead, archived, retired.Length == 0 ? null : retired));
    }

    private static List<RetiredBoatRequestRange> MergeRetiredRanges(
        IReadOnlyList<RetiredBoatRequestRange> previous, IReadOnlyList<BoatTripRequest> retired)
    {
        // A low-sequence trip can remain active while later trips finish. Keep
        // those gaps explicit rather than treating a high-water mark as authority.
        var ranges = new List<RetiredBoatRequestRange>();
        foreach (var range in previous.Concat(retired.Select(request =>
                     new RetiredBoatRequestRange(request.Sequence, request.Sequence))).OrderBy(range => range.FirstSequence))
        {
            if (ranges.Count > 0 && ranges[^1].LastSequence < long.MaxValue &&
                range.FirstSequence == ranges[^1].LastSequence + 1)
                ranges[^1] = ranges[^1] with { LastSequence = range.LastSequence };
            else ranges.Add(range);
        }
        return ranges;
    }

    private static (IReadOnlyList<T> Events, long Floor) Window<T>(string name, IReadOnlyList<T> events,
        Func<T, long> eventId, long floor, List<ArchivedEventBatch> archived)
    {
        if (events.Count <= CompactionThreshold)
        {
            return (events, floor);
        }
        var count = events.Count - RecentEventLimit;
        var prefix = events.Take(count).ToArray();
        var through = eventId(prefix[^1]);
        archived.Add(new(name, floor, through, JsonSerializer.SerializeToElement(prefix)));
        return (events.Skip(count).ToArray(), through);
    }
}
