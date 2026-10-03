using System.Collections.Concurrent;
using System.Text.RegularExpressions;
using ClankerWorld.Simulation.Cognition;
using ClankerWorld.Simulation.Harness;
using ClankerWorld.Simulation.Kernel;
using ClankerWorld.Simulation.Playtest;
using ClankerWorld.Simulation.World;

namespace ClankerWorld.Simulation.Tests;

internal static class NonviolentRuntimeFixture
{
    internal const string Judge = "founder:00000000000000000000000000000001";
    internal const string Stranger = "founder:00000000000000000000000000000002";
    internal const string Subject = "founder:00000000000000000000000000000003";
    internal const string Witness = "founder:00000000000000000000000000000004";
    internal const string ReturnLot = "voluntary-personal-wood";
    internal const int Day = 20;
    private static readonly Lazy<byte[]> Baseline = new(CreateBaseline);
    private static readonly Lazy<Task<byte[]>> Hearing = new(CreateReadyHearingAsync);
    private static readonly Lazy<Task<byte[]>> Accepted = new(CreateAcceptedRemedyAsync);

    internal static PrivateWorldRuntimeState Prepared() => PrivateWorldRuntimeCodec.Decode(Baseline.Value);
    internal static PrivateWorldRuntime Create(PrivateWorldRuntimeState state, IDecisionProvider provider) =>
        PrivateWorldRuntime.Restore(state, _ => provider);

    internal static async Task<PrivateWorldRuntimeState> ConductAsync()
    {
        var provider = new NonviolentTestProvider
        {
            Choose = observation => observation.InhabitantId == Subject
                ? observation.Candidates.FirstOrDefault(candidate => candidate.Id.Contains("|visit|", StringComparison.Ordinal)) : null
        };
        using var world = Create(Prepared(), provider);
        await UntilAsync(world, () => world.Towns[0].Nonviolent.ConductRecords.Count > 0, 6);
        var conduct = Assert.Single(world.Towns[0].Nonviolent.ConductRecords);
        Assert.Equal(Subject, conduct.ActorId);
        Assert.Equal("travel", conduct.ConductKind);
        Assert.Contains(world.ExportState().Events, item => item.Kind == "inhabitant_moved" && item.Detail.StartsWith(Subject + ":", StringComparison.Ordinal));
        return Strict(world.ExportState());
    }

    internal static async Task<PrivateWorldRuntimeState> FiledAsync()
    {
        var provider = FilingProvider();
        using var world = Create(await ConductAsync(), provider);
        await UntilAsync(world, () => world.Towns[0].Nonviolent.Cases.Count > 0, 8);
        return Strict(world.ExportState());
    }

    internal static async Task<PrivateWorldRuntimeState> ReadyToFindAsync() =>
        PrivateWorldRuntimeCodec.Decode(await Hearing.Value);

    internal static async Task<PrivateWorldRuntimeState> AcceptedRemedyAsync() =>
        PrivateWorldRuntimeCodec.Decode(await Accepted.Value);

    internal static CognitionNonviolentChoice FindingPayload(CognitionCandidate candidate) => new(
        Statement: "The actual observation and recorded rule support a warning, without changing anyone's property.",
        Uncertainty: "The evidence establishes passage, but does not establish the person's motive.",
        EvidenceIds: Regex.Matches(candidate.Description, @"case-evidence:[a-f0-9]{64}")
            .Select(match => match.Value).Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).ToArray());

    private static async Task<byte[]> CreateAcceptedRemedyAsync()
    {
        var provider = new NonviolentTestProvider
        {
            Choose = observation => observation.InhabitantId == Judge
                ? observation.Candidates.FirstOrDefault(candidate => candidate.Id.Contains("|law_case_find|", StringComparison.Ordinal) &&
                    candidate.Id.EndsWith("|warning", StringComparison.Ordinal)) : null,
            Payload = (_, candidate) => candidate.Id.Contains("|law_case_find|", StringComparison.Ordinal) ? FindingPayload(candidate) : null
        };
        PrivateWorldRuntimeState state;
        using (var hearing = Create(await ReadyToFindAsync(), provider))
        {
            await UntilAsync(hearing, () => hearing.Towns[0].Nonviolent.Cases[0].Findings.Count > 0, 8);
            var finding = Assert.Single(hearing.Towns[0].Nonviolent.Cases[0].Findings);
            Assert.Equal("warning", finding.Consequence);
            Assert.Equal(Judge, finding.Judge.AgentId);
            Assert.Equal("non_land_mayor", finding.Judge.Kind);
            state = Strict(hearing.ExportState());
        }
        var offered = false;
        var consent = new NonviolentTestProvider
        {
            Choose = observation =>
            {
                if (observation.InhabitantId != Subject) return null;
                var choice = observation.Candidates.FirstOrDefault(candidate => candidate.Id.Contains("|read|", StringComparison.Ordinal)) ??
                    observation.Candidates.FirstOrDefault(candidate => candidate.Id.Contains("|law_case_inspect|", StringComparison.Ordinal)) ??
                    observation.Candidates.FirstOrDefault(candidate => candidate.Id.Contains("|remedy_read|", StringComparison.Ordinal)) ??
                    observation.Candidates.FirstOrDefault(candidate => candidate.Id.Contains("|remedy_accept|", StringComparison.Ordinal));
                if (choice is null && !offered)
                {
                    choice = observation.Candidates.FirstOrDefault(candidate => candidate.Id.Contains("|remedy_offer|", StringComparison.Ordinal));
                    if (choice is not null) offered = true;
                }
                return choice;
            },
            Payload = (_, candidate) => candidate.Id.Contains("|remedy_offer|", StringComparison.Ordinal)
                ? new(Statement: "I offer one of my own pieces of wood, voluntarily.",
                    Terms: [new("return_goods", Subject, Witness, "wood", 1, ReturnLot)]) : null
        };
        using var world = Create(state, consent);
        Wake(world, Subject, "consider-voluntary-return");
        await UntilAsync(world, () => world.Towns[0].Nonviolent.Agreements.Count > 0, 12);
        Assert.Equal("pending", Assert.Single(world.Towns[0].Nonviolent.Agreements).Status);
        Assert.Empty(world.Towns[0].Nonviolent.Effects);
        Assert.Equal(2, world.Society.Inventory.GetLot(ReturnLot).Quantity);
        return PrivateWorldRuntimeCodec.Encode(Strict(world.ExportState()));
    }

    internal static void Wake(PrivateWorldRuntime world, string actor, string key) =>
        world.SubmitInstruction(new(key, "owner:test", actor, OwnerInstructionKind.Suggestive,
            "Consider the current public case and make your own choice."));

    private static async Task<byte[]> CreateReadyHearingAsync()
    {
        var state = await FiledAsync();
        var town = state.Towns![0];
        var tick = state.Society.Society.WorldTick;
        var (council, government) = TownGovernmentRules.RegisterMayor(town.Governance!, town.Government!,
            Judge, "land", town.ResidentIds, tick);
        (council, government) = TownGovernmentRules.Propose(council, government, town.Id, Judge,
            new(TownArrangementRules.Council, TownArrangementRules.Mayor), false, town.ResidentIds, tick, Day);
        foreach (var voter in town.ResidentIds.Take(3))
            government = TownGovernmentRules.Vote(government, government.Changes[^1].Id, voter, true, tick);
        (council, government) = TownGovernmentRules.Advance(council, government, town.Id, town.Name,
            state.WorldSeed, town.ResidentIds, tick, Day);
        var contest = Assert.IsType<TownMayoralContest>(government.Contest);
        foreach (var voter in town.ResidentIds)
            government = TownGovernmentRules.VoteMayor(government, TownGovernmentRules.RoundToken(contest), voter, Judge, tick);
        state = state with { Towns = [town with { Governance = council, Government = government }] };
        using (var clock = Create(state, new NonviolentTestProvider()))
        {
            await UntilAsync(clock, () => clock.Towns[0].Government!.Offices.Any(office => office.HolderId == Judge), Day + 3);
            state = clock.ExportState();
        }
        town = state.Towns![0];
        tick = state.Society.Society.WorldTick;
        (council, government) = TownGovernmentRules.Propose(town.Governance!, town.Government!, town.Id, Judge,
            town.Government!.Arrangement with { NonLand = TownArrangementRules.Mayor }, false, town.ResidentIds, tick, Day, "land");
        var change = government.Changes[^1];
        council = TownGovernanceRules.LearnNotices(council, Judge,
            council.Notices.Where(notice => notice.Kind == "government" && notice.SubjectId == change.Id).Select(notice => notice.Id), tick);
        (council, government) = TownGovernmentRules.AcceptNonLandDuties(council, government, change.Id, Judge, town.ResidentIds, tick);
        foreach (var voter in town.ResidentIds.Take(3))
            government = TownGovernmentRules.Vote(government, change.Id, voter, true, tick);
        (council, government) = TownGovernmentRules.Advance(council, government, town.Id, town.Name,
            state.WorldSeed, town.ResidentIds, tick, Day);
        Assert.Equal(Judge, TownGovernmentRules.CurrentNonLandAuthority(government, tick)!.HolderId);
        state = Strict(state with { Towns = [town with { Governance = council, Government = government }] });
        var provider = new NonviolentTestProvider
        {
            Choose = observation => observation.Candidates.FirstOrDefault(candidate => candidate.Id.Contains("|read|", StringComparison.Ordinal)) ??
                observation.Candidates.FirstOrDefault(candidate => candidate.Id.Contains("|law_case_inspect|", StringComparison.Ordinal)) ??
                observation.Candidates.FirstOrDefault(candidate => candidate.Id.Contains("|law_case_answer|", StringComparison.Ordinal)),
            Payload = (_, candidate) => candidate.Id.Contains("|law_case_answer|", StringComparison.Ordinal)
                ? new(Statement: "I answer for myself and acknowledge the reported passage, without promising goods.") : null
        };
        using var prepared = Create(state, provider);
        await UntilAsync(prepared, () => provider.Observations.Any(observation => observation.InhabitantId == Judge &&
            observation.Candidates.Any(candidate => candidate.Id.Contains("|law_case_find|", StringComparison.Ordinal))) &&
            prepared.Towns[0].Nonviolent.Cases[0].Responses.Any(response => response.AgentId == Subject), 16);
        Wake(prepared, Judge, "fresh-reasoned-choice");
        return PrivateWorldRuntimeCodec.Encode(Strict(prepared.ExportState()));
    }

    internal static NonviolentTestProvider FilingProvider(DecisionProviderKind kind = DecisionProviderKind.LargeLanguageModel,
        bool includePayload = true) => new(kind)
        {
            Choose = observation => observation.InhabitantId == Witness
                ? observation.Candidates.FirstOrDefault(candidate => candidate.Id.Contains("|law_case_file|", StringComparison.Ordinal)) : null,
            Payload = (_, choice) => includePayload && choice.Id.Contains("|law_case_file|", StringComparison.Ordinal)
                ? new(Statement: "I saw this person travel through the public notice place under the recorded rule.") : null
        };

    internal static async Task UntilAsync(PrivateWorldRuntime world, Func<bool> complete, int limit = 80)
    {
        for (var tick = 0; tick < limit && !complete(); tick++)
            Assert.True((await world.AdvanceOneTickAsync()).Advanced);
        Assert.True(complete(), "The expected native legal boundary was not reached within " + limit + " ticks.");
    }

    internal static PrivateWorldRuntimeState Strict(PrivateWorldRuntimeState state)
    {
        var bytes = PrivateWorldRuntimeCodec.Encode(state);
        var decoded = PrivateWorldRuntimeCodec.Decode(bytes);
        using var restored = Create(decoded, new NonviolentTestProvider());
        Assert.Equal(bytes, PrivateWorldRuntimeCodec.Encode(restored.ExportState()));
        return decoded;
    }

    private static byte[] CreateBaseline()
    {
        using var generated = NormalPathWorld.CreateGenerated("government-personal-path", _ => new NonviolentTestProvider());
        var state = generated.ExportState();
        var town = state.Towns![0];
        var board = town.OriginSite!.Value;
        var map = state.Map;
        var start = map.FootNeighbors(board).SelectMany(map.FootNeighbors).Distinct()
            .Where(point => map.FootDistance(point, board) == 2)
            .OrderBy(point => point.Y).ThenBy(point => point.X).First();
        var far = Enumerable.Range(0, map.Height).SelectMany(y => Enumerable.Range(0, map.Width).Select(x => new GridPoint(x, y)))
            .First(point => map.IsBuildable(point) && map.FootDistance(point, board) >= 8);
        var council = town.Governance!;
        var government = town.Government!;
        (council, government) = TownLawRules.ProposeAdoption(council, government, town.Id, Judge,
            "Paths: Take care when passing the public notice place.", TownLawRules.ResidentDuty, [], town.ResidentIds, 0, Day);
        foreach (var voter in town.ResidentIds.Take(3))
            council = TownGovernanceRules.VoteProposal(council, council.Proposals[^1].Id, voter, true, 0);
        (council, government) = TownLawRules.Enact(council, government, town.Id, town.Name, 0);
        foreach (var actor in new[] { Subject, Witness, Judge })
            council = TownGovernanceRules.LearnNotices(council, actor, council.Notices.Select(notice => notice.Id), 0);
        var society = state.Society.Society;
        var oldDay = society.Config.TicksPerWorldDay;
        var inventory = InventoryFixture.AddLot(society.Inventory, ReturnLot, "wood", Subject, 2);
        state = state with
        {
            Towns = [town with { Governance = council, Government = government }],
            Inhabitants = state.Inhabitants.Select(person => person with
            {
                Position = person.InhabitantId == Subject ? start : person.InhabitantId == Stranger ? far : board,
                HungerBasisPoints = 9_000,
                LastDecisionContext = null
            }).ToArray(),
            WorldSystems = RegionalWeatherRules.Initialize(state.WorldSystems! with
            { Config = state.WorldSystems.Config with { TicksPerDay = Day, CalendarOffsetTicks = 0 }, RegionalWeather = null }, map),
            Society = state.Society with
            {
                Society = society with
                {
                    Inventory = inventory,
                    Config = society.Config with { TicksPerWorldDay = Day },
                    Inhabitants = society.Inhabitants.Select(person => person with
                    {
                        BirthTick = person.BirthTick / oldDay * Day,
                        BirthLifeTick = person.BirthLifeTick is { } birth ? birth / oldDay * Day : null
                    }).ToArray()
                }
            }
        };
        using var validated = Create(state, new NonviolentTestProvider());
        return PrivateWorldRuntimeCodec.Encode(validated.ExportState());
    }
}

internal sealed class NonviolentTestProvider(DecisionProviderKind kind = DecisionProviderKind.LargeLanguageModel) : IDecisionProvider
{
    public DecisionProviderKind Kind => kind;
    public long ProviderEpoch => 1;
    internal Func<InhabitantObservation, CognitionCandidate?>? Choose { get; init; }
    internal Func<InhabitantObservation, CognitionCandidate, CognitionNonviolentChoice?>? Payload { get; init; }
    internal ConcurrentQueue<InhabitantObservation> Observations { get; } = new();
    internal ConcurrentQueue<string> Selected { get; } = new();

    public ValueTask<CognitionDecisionResponse> DecideAsync(CognitionDecisionRequest request, CancellationToken cancellationToken = default)
    {
        var observation = request.Observation;
        Observations.Enqueue(observation);
        var choice = Choose?.Invoke(observation) ?? observation.Candidates.Single(candidate => candidate.Id == "safe_idle");
        Selected.Enqueue(observation.InhabitantId + "|" + choice.Id);
        return ValueTask.FromResult(new CognitionDecisionResponse(request.RequestId, observation.InhabitantId, Kind, ProviderEpoch,
            observation.RunEpoch, observation.DecisionGeneration, observation.ObservationDigest, choice.Id, 1,
            observation.Candidates.ToDictionary(candidate => candidate.Id, candidate => candidate.Id == choice.Id ? 1d : 0d, StringComparer.Ordinal),
            CivicNonviolent: Payload?.Invoke(observation, choice)));
    }
}
