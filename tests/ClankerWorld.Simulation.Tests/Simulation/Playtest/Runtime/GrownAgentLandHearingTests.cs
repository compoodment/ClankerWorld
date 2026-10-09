using System.Collections.Concurrent;
using ClankerWorld.Simulation.Cognition;
using ClankerWorld.Simulation.Harness;
using ClankerWorld.Simulation.Playtest;
using ClankerWorld.Simulation.Society;
using ClankerWorld.Simulation.World;

namespace ClankerWorld.Simulation.Tests;

public sealed partial class GrownAgentHelperMemoryTests
{
    private const string NativeHearingJudge = "founder:00000000000000000000000000000002";
    private const string NativeHearingHouse = "first-town-house-b";
    private const int NativeHearingDay = 40;
    private static readonly Lazy<Task<byte[]>> NativeHearingGovernment = new(() => CreateNativeHearingGovernmentAsync(false));
    private static readonly Lazy<Task<byte[]>> NativeJudgeGovernment = new(() => CreateNativeHearingGovernmentAsync(true));

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task NativeBornAdultCanInspectARealLandRight(bool nativeBorn)
    {
        var state = PrivateWorldRuntimeCodec.Decode(await ConversationAdult.Value);
        var birth = Assert.Single(state.Society.Society.Births);
        Assert.True(birth.ChildId.Length > 128);
        var actor = nativeBorn ? birth.ChildId : birth.PrimaryCaregiverId;
        var person = state.Society.Society.GetInhabitant(actor);
        Assert.Equal(SocietyAgeBand.Adult, person.AgeBand);
        var right = state.HouseholdLandUseRights!.First(item => item.HouseholdId == person.HouseholdId);
        var tick = state.Society.Society.WorldTick;
        TownLandCaseParty[] parties = [new("native-owner-party", "household", person.HouseholdId, right.TownId, [actor])];
        var hearings = TownLandHearingRules.File(TownLandHearingState.Create(), right.TownId,
            new(actor, "dispute", "Please confirm my household's actual land right.", new("confirm"), tick),
            right.Tiles, [right], parties, tick, 40, "native-land-inspection-notice");
        var item = Assert.Single(hearings.Cases);
        TownCivicReceipt[] receipts = [new(actor, item.Revisions[0].NoticeId, tick)];
        hearings = TownLandHearingRules.Inspect(hearings, item.Id, 1, actor, tick);
        Assert.Equal(actor, Assert.Single(hearings.Cases[0].Reads).AgentId);
        var evidence = new TownLandEvidence("native-right-record", 1, "record", "record_inspection", actor,
            right.Id, TownLandHearingRules.Version(right), tick, actor, tick, "I inspected this actual household land right.");
        foreach (var malformed in new[] { "", " " + actor, actor + "\0" })
        {
            Assert.Throws<InvalidOperationException>(() => TownLandHearingRules.Inspect(hearings, item.Id, 1, malformed, tick));
            Assert.Throws<InvalidOperationException>(() => TownLandHearingRules.AddEvidence(hearings, item.Id, 1, evidence with { SourceAgentId = malformed }, receipts));
            Assert.Throws<InvalidOperationException>(() => TownLandHearingRules.AddEvidence(hearings, item.Id, 1, evidence with { SubmittedByAgentId = malformed }, receipts));
        }
        Assert.Throws<InvalidOperationException>(() => TownLandHearingRules.Inspect(hearings, item.Id, 2, actor, tick));
        Assert.Throws<InvalidOperationException>(() => TownLandHearingRules.Inspect(hearings, item.Id, 1, actor, tick - 1));
        Assert.Throws<InvalidOperationException>(() => TownLandHearingRules.AddEvidence(hearings, item.Id, 1, evidence, []));
        foreach (var bounded in new[] { evidence with { Id = new string('x', 129) }, evidence with { SourceRecordId = new string('x', 129) },
                     evidence with { SourceVersion = new string('x', 129) }, evidence with { Text = new string('x', TownLandHearingRules.MaximumTextLength + 1) } })
            Assert.Throws<InvalidOperationException>(() => TownLandHearingRules.AddEvidence(hearings, item.Id, 1, bounded, receipts));
        hearings = TownLandHearingRules.AddEvidence(hearings, item.Id, 1, evidence, receipts);
        Assert.Equal(evidence, Assert.Single(hearings.Cases[0].Evidence));
    }

    [Theory]
    [InlineData(true, false)]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false, true)]
    public async Task NativeBornAdultPersonallyInspectsAndAcceptsAnAuthoritativePropertyGrant(bool nativeBorn, bool nativeJudge, bool laterGeneration = false)
    {
        var state = PrivateWorldRuntimeCodec.Decode(await (laterGeneration ? LaterHearingGovernment.Value :
            nativeJudge ? NativeJudgeGovernment.Value : NativeHearingGovernment.Value));
        var birth = state.Society.Society.Births.OrderBy(item => item.CommittedTick).Last();
        if (laterGeneration) Assert.True(birth.ChildId.Length > 256);
        var actor = nativeBorn ? birth.ChildId : birth.PrimaryCaregiverId;
        Assert.True(birth.ChildId.Length > 128);
        using (var displaced = PrivateWorldRuntime.Restore(state, _ => new NativeLandHearingChoices()))
        {
            Assert.True(displaced.DisplaceAdult(actor));
            state = displaced.ExportState();
        }
        const string recipient = "household:native-hearing-recipient";
        var society = SocietyFixture.CreateHousehold(state.Society.Society, recipient, "Receiving household", [actor]).Checkpoint;
        var choices = new NativeLandHearingChoices
        {
            Request = true,
            Judge = nativeJudge ? birth.ChildId : NativeHearingJudge,
            Filer = birth.PrimaryCaregiverId,
            IgnoredLandObserver = !nativeBorn && !nativeJudge ? birth.ChildId : null
        };
        using var world = PrivateWorldRuntime.Restore(state with { Society = state.Society with { Society = society } }, _ => choices);
        var house = world.WorldSimulation.Buildings.Single(item => item.InstanceId == NativeHearingHouse);
        var former = world.Society.GetHousehold(house.HouseholdId!).MemberIds.ToArray();
        foreach (var member in former) Assert.True(world.DisplaceAdult(member));
        Assert.Empty(world.Society.GetHousehold(house.HouseholdId!).MemberIds);
        await NativeHearingUntil(world, () => world.Towns[0].LandHearings.Cases.Count == 1, 20);
        choices.Consent = true;
        choices.Rule = true;
        await NativeHearingUntil(world, () => world.Towns[0].LandHearings.Cases[0].Status == "settled", 25);
        Assert.Null(world.WorldSimulation.Buildings.Single(item => item.InstanceId == house.InstanceId).HouseholdId);
        Assert.Equal("reclaim", Assert.Single(world.Towns[0].LandHearings.Cases[0].Rulings).Outcome.Kind);
        choices.Target = recipient;
        await NativeHearingUntil(world, () => world.Towns[0].LandHearings.Cases.Count == 2, 20);
        choices.Request = false;
        var pending = PrivateWorldRuntimeCodec.Encode(world.ExportState());
        using var replay = PrivateWorldRuntime.Restore(PrivateWorldRuntimeCodec.Decode(pending),
            _ => new NativeLandHearingChoices { Consent = true, Rule = true, Judge = choices.Judge, IgnoredLandObserver = choices.IgnoredLandObserver });
        Assert.Equal(pending, PrivateWorldRuntimeCodec.Encode(replay.ExportState()));
        Assert.False((await world.AdvanceOneTickAsync(() => false)).Advanced);
        Assert.Equal(pending, PrivateWorldRuntimeCodec.Encode(world.ExportState()));
        // A recipient who also represents the Town can still owe the separate
        // Town response opportunity. Allow its actual full notice window.
        for (var step = 0; step < NativeHearingDay + 10 && world.Towns[0].LandHearings.Cases[1].Status == "pending"; step++)
        {
            NativeHearingPromptAll(world);
            NativeHearingPromptAll(replay);
            Assert.True((await world.AdvanceOneTickAsync()).Advanced);
            Assert.True((await replay.AdvanceOneTickAsync()).Advanced);
            Assert.Equal(PrivateWorldRuntimeCodec.Encode(world.ExportState()), PrivateWorldRuntimeCodec.Encode(replay.ExportState()));
        }
        var item = world.Towns[0].LandHearings.Cases[1];
        Assert.Equal("settled", item.Status);
        Assert.Equal("grant", Assert.Single(item.Rulings).Outcome.Kind);
        Assert.Equal(choices.Judge, Assert.Single(item.Rulings).Judge.AgentId);
        var transfer = Assert.IsType<TownPropertyTransfer>(item.Property!.Transfer);
        Assert.Equal(recipient, transfer.ResultBuilding.HouseholdId);
        Assert.All(transfer.ResultLots, lot => Assert.Equal(recipient, lot.OwnerId));
        Assert.Contains(item.Property.Consents, consent => consent.AgentId == actor && consent.Agreed);
        Assert.Contains(actor, item.Revisions[0].Parties.SelectMany(party => party.AdultIds));
        Assert.Contains(actor, Assert.Single(item.Rulings).Parties.SelectMany(party => party.AdultIds));
        Assert.Contains(actor, item.DirectStakeIds);
        var record = Assert.Single(item.Evidence, evidence => evidence.SourceRecordId == TownPropertyRules.RecordId(item, 1));
        Assert.Contains(item.Reads, read => read.AgentId == actor && read.Revision == 1 && read.EvidenceIds.Contains(record.Id));
        Assert.Contains(world.Towns[0].Governance!.Knowledge, receipt => receipt.AgentId == actor && receipt.NoticeId == item.Revisions[0].NoticeId);
        Assert.Contains(choices.Selected, choice => choice.Actor == actor && choice.Choice.Contains("|hearing_inspect|", StringComparison.Ordinal));
        Assert.Contains(choices.Selected, choice => choice.Actor == actor && choice.Choice.Contains("|hearing_property_accept|", StringComparison.Ordinal));
        world.Validate();
        var saved = PrivateWorldRuntimeCodec.Encode(world.ExportState());
        using var restored = PrivateWorldRuntime.Restore(PrivateWorldRuntimeCodec.Decode(saved), _ => new NativeLandHearingChoices());
        Assert.Equal(saved, PrivateWorldRuntimeCodec.Encode(restored.ExportState()));
        var unknownReads = item with { Reads = item.Reads.Select(read => read.AgentId == actor ? read with { AgentId = actor + ":unknown" } : read).ToArray() };
        var unknownConsent = item with
        {
            Property = item.Property with
            {
                Consents = item.Property.Consents.Select(consent =>
            consent.AgentId == actor ? consent with { AgentId = actor + ":unknown" } : consent).ToArray()
            }
        };
        var invalidCases = new List<TownLandCase> { unknownReads, unknownConsent };
        if (laterGeneration)
        {
            foreach (var invalidIds in new[] { new[] { actor, actor }, new[] { actor, choices.Judge }.Order(StringComparer.Ordinal).Reverse().ToArray(),
                         new[] { " " + actor }, new[] { actor + "\0" }, new[] { actor + ":unknown" } })
            {
                invalidCases.Add(item with { DirectStakeIds = invalidIds });
                invalidCases.Add(item with
                {
                    Revisions = item.Revisions.Select(revision => revision with
                    { Parties = revision.Parties.Select(party => party.AdultIds.Contains(actor) ? party with { AdultIds = invalidIds } : party).ToArray() }).ToArray()
                });
                invalidCases.Add(item with
                {
                    Rulings = item.Rulings.Select(ruling => ruling with
                    { Parties = ruling.Parties.Select(party => party.AdultIds.Contains(actor) ? party with { AdultIds = invalidIds } : party).ToArray() }).ToArray()
                });
            }
        }
        foreach (var invalid in invalidCases)
        {
            var current = world.ExportState();
            current = current with
            {
                Towns = current.Towns!.Select(town => town with
                {
                    LandHearings = town.LandHearings with
                    { Cases = town.LandHearings.Cases.Select(entry => entry.Id == item.Id ? invalid : entry).ToArray() }
                }).ToArray()
            };
            Assert.Throws<InvalidDataException>(() => PrivateWorldRuntimeCodec.Decode(PrivateWorldRuntimeCodec.Encode(current)));
            Assert.Equal(saved, PrivateWorldRuntimeCodec.Encode(world.ExportState()));
        }
    }

    private static async Task<byte[]> CreateNativeHearingGovernmentAsync(bool nativeJudge, PrivateWorldRuntimeState? initialState = null)
    {
        var state = NativeHearingCalendar(initialState ?? PrivateWorldRuntimeCodec.Decode(await ConversationAdult.Value));
        var choices = new NativeLandHearingChoices
        {
            Elect = true,
            Judge = nativeJudge ? Assert.Single(state.Society.Society.Births).ChildId : NativeHearingJudge
        };
        using var world = PrivateWorldRuntime.Restore(state, _ => choices);
        world.Resume();
        await NativeHearingUntil(world, () => world.Towns[0].Government!.Offices.Any(office => office.HolderId == choices.Judge), NativeHearingDay * 3);
        Assert.Equal("land", Assert.Single(world.Towns[0].Government!.Offices).Mandates);
        return PrivateWorldRuntimeCodec.Encode(world.ExportState());
    }

    private static PrivateWorldRuntimeState NativeHearingCalendar(PrivateWorldRuntimeState state)
    {
        var society = state.Society.Society;
        const int day = NativeHearingDay;
        var oldDay = society.Config.TicksPerWorldDay;
        var life = society.LifeTickAt(society.WorldTick);
        var worldConfig = state.WorldSystems!.Config with { TicksPerDay = day, CalendarOffsetTicks = 0 };
        // Compress only the test's calendar and age intervals. Keep actual birth
        // records, full native identities, Houses, stock and household ownership.
        society = society with
        {
            Config = society.Config with { TicksPerWorldDay = day },
            Inhabitants = society.Inhabitants.Select(person => person with
            { BirthLifeTick = life - (life - (person.BirthLifeTick ?? person.BirthTick)) * day / oldDay }).ToArray(),
        };
        return state with
        {
            JevEnabled = false,
            RoutineHelper = RoutineHelperSettings.Off,
            Society = state.Society with { Society = society },
            Continuity = state.Continuity! with
            {
                Couples = state.Continuity.Couples.Select(couple => couple with
                { DeadlineTick = society.WorldTick + (couple.DeadlineTick - society.WorldTick) * day / oldDay }).ToArray(),
            },
            AnimalWorld = state.AnimalWorld! with
            {
                Animals = state.AnimalWorld.Animals.Select(animal => animal with
                {
                    BornTick = animal.BornTick < 0 ? animal.BornTick * day / oldDay : animal.BornTick,
                    ProductProgressTicks = animal.ProductProgressTicks * day / oldDay,
                    CareUntilTick = animal.CareUntilTick > society.WorldTick ?
                        society.WorldTick + (animal.CareUntilTick - society.WorldTick) * day / oldDay : animal.CareUntilTick,
                    Pregnancy = animal.Pregnancy is { } pregnancy ? pregnancy with
                    { ProgressTicks = pregnancy.ProgressTicks * day / oldDay } : null,
                }).ToArray(),
            },
            WorldSystems = RegionalWeatherRules.Initialize(state.WorldSystems! with
            {
                Config = worldConfig,
                Climate = WeatherRules.Advance(WeatherRules.CreateGenesis(state.WorldSeed, worldConfig),
                state.WorldSystems.WorldTick, state.WorldSeed, worldConfig),
                RegionalWeather = null
            }, state.Map),
            Inhabitants = state.Inhabitants.Select(person => person with
            {
                Position = state.Towns![0].OriginSite!.Value,
                Project = null,
                LastDecisionContext = null,
                HungerBasisPoints = 9_500,
                Survival = person.Survival! with { WarmthBasisPoints = 10_000, IllnessBasisPoints = 0 }
            }).ToArray(),
        };
    }

    private static void NativeHearingPromptAll(PrivateWorldRuntime world)
    {
        var index = 0;
        foreach (var person in world.Inhabitants)
            world.SubmitInstruction(new("native-hearing-" + world.WorldTick + "-" + index++, "owner:test", person.InhabitantId,
                OwnerInstructionKind.Suggestive, "Consider the current public hearing and make your own informed choice."));
    }

    private static async Task NativeHearingUntil(PrivateWorldRuntime world, Func<bool> done, int limit)
    {
        for (var step = 0; step < limit && !done(); step++)
        {
            NativeHearingPromptAll(world);
            var result = await world.AdvanceOneTickAsync();
            Assert.True(result.Advanced, System.Text.Json.JsonSerializer.Serialize(result));
        }
        Assert.True(done(), "The native hearing boundary was not reached: " + string.Join("; ", world.ExportState().Events.TakeLast(8).Select(item => item.Kind + ":" + item.Detail)));
    }

    private sealed class NativeLandHearingChoices : IDecisionProvider
    {
        public DecisionProviderKind Kind => DecisionProviderKind.LargeLanguageModel;
        public long ProviderEpoch => 1;
        public string Judge { get; init; } = NativeHearingJudge;
        public string Filer { get; init; } = "founder:00000000000000000000000000000001";
        public string? IgnoredLandObserver { get; init; }
        public bool Elect { get; init; }
        public bool Request { get; set; }
        public string? Target { get; set; }
        public bool Consent { get; set; }
        public bool Rule { get; set; }
        public HashSet<string> CaseCandidates { get; init; } = new(StringComparer.Ordinal);
        public Func<string, string?>? CaseVote { get; set; }
        public ConcurrentQueue<(string Actor, string Choice)> Selected { get; } = new();

        public ValueTask<CognitionDecisionResponse> DecideAsync(CognitionDecisionRequest request, CancellationToken cancellationToken = default)
        {
            var observation = request.Observation;
            var choices = observation.Candidates;
            CognitionCandidate? Find(string action) => choices.FirstOrDefault(candidate => candidate.Id.Contains("|" + action + "|", StringComparison.Ordinal));
            var selected = Find("read") ?? (observation.InhabitantId == IgnoredLandObserver ? null : Find("hearing_inspect")) ?? Find("yes");
            if (Elect)
            {
                // Only Judge registers; select the actually offered ballot,
                // including its bounded token for a native-born candidate.
                selected ??= Find("government_yes") ?? Find("mayor_vote");
                if (observation.InhabitantId == Judge)
                    selected ??= choices.FirstOrDefault(candidate => candidate.Id.Contains("|mayor_register|land|", StringComparison.Ordinal)) ??
                        choices.FirstOrDefault(candidate => candidate.Id.Contains("|government_propose|council+mayor|", StringComparison.Ordinal));
            }
            if (Request && observation.InhabitantId == Filer)
                selected ??= choices.FirstOrDefault(candidate => candidate.Id.Contains("|hearing_property_request|" + NativeHearingHouse + "|", StringComparison.Ordinal) &&
                    (Target is null ? candidate.Id.EndsWith("|town", StringComparison.Ordinal) : candidate.Description.Contains("grant household " + Target + " ", StringComparison.Ordinal)));
            if (Consent) selected ??= Find("hearing_property_accept");
            if (CaseCandidates.Contains(observation.InhabitantId)) selected ??= Find("hearing_judge_register");
            if (CaseVote?.Invoke(observation.InhabitantId) is { } candidateName)
                selected ??= choices.FirstOrDefault(candidate => candidate.Id.Contains("|hearing_judge_vote|", StringComparison.Ordinal) &&
                    candidate.Description.Contains("ballot for " + candidateName + " as judge", StringComparison.Ordinal));
            selected ??= Find("hearing_answer");
            if (Rule && observation.InhabitantId == Judge)
                selected ??= choices.FirstOrDefault(candidate => candidate.Id.Contains("|hearing_rule|", StringComparison.Ordinal) &&
                    (candidate.Id.EndsWith("|reclaim", StringComparison.Ordinal) || candidate.Id.EndsWith("|grant", StringComparison.Ordinal)));
            selected ??= choices.Single(candidate => candidate.Id == "safe_idle");
            Selected.Enqueue((observation.InhabitantId, selected.Id));
            var evidence = selected.Description.Split([' ', ';', ','], StringSplitOptions.RemoveEmptyEntries)
                .Where(token => token.StartsWith("land-evidence:", StringComparison.Ordinal)).Select(token => token.Split('=')[0]).Distinct(StringComparer.Ordinal).ToArray();
            return ValueTask.FromResult(new CognitionDecisionResponse(request.RequestId, observation.InhabitantId, Kind, ProviderEpoch,
                observation.RunEpoch, observation.DecisionGeneration, observation.ObservationDigest, selected.Id, 1,
                choices.ToDictionary(candidate => candidate.Id, candidate => candidate.Id == selected.Id ? 1d : 0d, StringComparer.Ordinal),
                CivicLandHearing: new(Statement: "Decide this noticed property request using actual ownership records and personal agreements.", EvidenceIds: evidence)));
        }
    }
}
