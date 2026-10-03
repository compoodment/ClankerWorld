using ClankerWorld.Simulation.Harness;
using ClankerWorld.Simulation.Kernel;
using ClankerWorld.Simulation.Playtest;
using ClankerWorld.Simulation.Society;
using ClankerWorld.Simulation.World;

namespace ClankerWorld.Simulation.Tests;

public sealed partial class SettlementParenthoodTests
{
    [Theory]
    [InlineData("last_place", false)]
    [InlineData("full", false)]
    [InlineData("other_town", false)]
    [InlineData("no_town", false)]
    [InlineData("last_place", true)]
    [InlineData("full", true)]
    [InlineData("other_town", true)]
    [InlineData("no_town", true)]
    public async Task GuardianAcceptanceMovesHouseholdOnlyToALegalPlaceInTheSameRecordedTown(string scenario, bool ordered)
    {
        using var initial = NormalPathWorld.CreateGenerated("guardian-relocation", _ => new ParentProvider("safe_idle"));
        var state = initial.ExportState();
        var originHouse = state.WorldSimulation!.Buildings.Single(item => item.InstanceId == "first-town-house-a");
        var destinationHouse = state.WorldSimulation.Buildings.Single(item => item.InstanceId == "first-town-house-b");
        var originHousehold = originHouse.HouseholdId!;
        var destinationHousehold = destinationHouse.HouseholdId!;
        var society = state.Society.Society;
        var parents = society.GetHousehold(originHousehold).MemberIds.Order(StringComparer.Ordinal).ToArray();
        var guardians = society.GetHousehold(destinationHousehold).MemberIds.Order(StringComparer.Ordinal).ToArray();
        Assert.Equal(2, parents.Length);
        Assert.Equal(2, guardians.Length);
        var adult = guardians[0];
        foreach (var pair in new[] { parents, guardians })
        {
            var requestId = "relocation-partnership:" + pair[0];
            society = SocietyFixture.ProposeRelationship(society,
                new(requestId, 1, SocietyRelationshipType.Partnership, pair[0], pair[1], society.WorldTick)).Checkpoint;
            society = SocietyFixture.AcceptRelationship(society, requestId, 1, pair[1]).Checkpoint;
        }

        var inventory = InventoryFixture.CreateGenesis([]) with { WorldTick = society.WorldTick };
        inventory = InventoryFixture.AddLot(inventory, "orphan-birth-food", "food", originHousehold, 4,
            storageBuildingId: originHouse.InstanceId);
        inventory = InventoryFixture.AddLot(inventory, "guardian-birth-food", "food", destinationHousehold, 12,
            storageBuildingId: destinationHouse.InstanceId);
        society = society with { Inventory = inventory };
        var orphanBirth = SocietyFixture.CommitBirth(society, new SocietyBirthRequest(
            "relocation-orphan", 1, parents[0], parents[1], originHousehold, parents, parents,
            "orphan-birth-food", 4, society.WorldTick, ChildName: "Orphan", PrimaryCaregiverId: parents[0]));
        var child = Assert.IsType<string>(orphanBirth.CreatedId);
        society = orphanBirth.Checkpoint;
        // A grandparent is asked first wherever they live, so the same adult is offered in every scenario.
        var grandparent = new SocietyRelationship("relocation-grandparent", 1,
            SocietyRelationshipType.BiologicalParentage, adult, parents[0],
            SocietyRelationshipState.Accepted, SocietyConsentState.ProtectedLifecycle,
            society.WorldTick, society.WorldTick, "family");
        society = society with
        {
            Relationships = society.Relationships.Append(grandparent).OrderBy(edge => edge.Id, StringComparer.Ordinal).ToArray(),
        };
        var addedChildren = new List<(string Id, GridPoint HousePosition)> { (child, originHouse.Position) };
        var destinationChildCount = scenario == "full" ? 2 : 1;
        for (var index = 0; index < destinationChildCount; index++)
        {
            var birth = SocietyFixture.CommitBirth(society, new SocietyBirthRequest(
                "relocation-resident:" + index, 1, guardians[0], guardians[1], destinationHousehold,
                guardians, guardians, "guardian-birth-food", 4, society.WorldTick,
                ChildName: "Resident" + index, PrimaryCaregiverId: adult));
            society = birth.Checkpoint;
            addedChildren.Add((Assert.IsType<string>(birth.CreatedId), destinationHouse.Position));
        }

        var deceased = new List<PlaytestDeceasedInhabitantState>(state.DeceasedInhabitants ?? []);
        foreach (var parent in parents)
        {
            society = SocietyFixture.Kill(society, parent, SocietyDeathCause.Accident).Checkpoint;
            var person = society.GetInhabitant(parent);
            deceased.Add(new(parent, person.DeathTick!.Value, society.AgeAt(person, person.DeathTick.Value),
                state.Inhabitants.Single(item => item.InhabitantId == parent)));
        }
        Assert.Null(society.GetInhabitant(child).PrimaryCaregiverId);
        var people = state.Inhabitants.Where(person => !parents.Contains(person.InhabitantId, StringComparer.Ordinal)).ToList();
        foreach (var newborn in addedChildren)
        {
            var tile = state.Map.Tiles.Select(item => item.Position)
                .Where(point => state.Map.IsBuildable(point) && !people.Any(person => person.Position == point) &&
                    !state.Map.Resources.Any(resource => resource.Position == point) &&
                    !state.Map.CampObjects.Any(camp => camp.Position == point))
                .OrderBy(point => Math.Abs(point.X - newborn.HousePosition.X) + Math.Abs(point.Y - newborn.HousePosition.Y))
                .ThenBy(point => point.Y).ThenBy(point => point.X).First();
            people.Add(new(newborn.Id, tile, 10_000, 0, "curious", "grow with the household",
                Survival: new SurvivalCondition(WarmthBasisPoints: 10_000)));
        }
        var activeIds = society.Inhabitants.Where(person => person.Status == SocietyInhabitantStatus.Active)
            .Select(person => person.Id).Order(StringComparer.Ordinal).ToArray();
        state = state with
        {
            Society = state.Society with { Society = society },
            Survival = new SettlementSurvivalState(society.WorldTick, []),
            DeceasedInhabitants = deceased.ToArray(),
            Inhabitants = people.Select(person => person with
            {
                HungerBasisPoints = 10_000,
                Survival = new SurvivalCondition(WarmthBasisPoints: 10_000),
                Project = null,
                LastDecisionContext = null,
            }).ToArray(),
            Towns = state.Towns!.Select(town => town with { ResidentIds = activeIds }).ToArray(),
        };
        var originalTownId = state.Towns!.Single().Id;
        if (scenario == "other_town")
        {
            const string otherTownId = "town:guardian-other";
            var otherResidents = society.GetHousehold(destinationHousehold).MemberIds
                .Where(id => society.GetInhabitant(id).Status == SocietyInhabitantStatus.Active)
                .Order(StringComparer.Ordinal).ToArray();
            var otherBuildings = state.WorldSimulation.Buildings.Where(building => building.HouseholdId == destinationHousehold)
                .Select(building => building.InstanceId).Order(StringComparer.Ordinal).ToArray();
            var firstTown = state.Towns.Single();
            state = state with
            {
                Towns = [firstTown with
                {
                    ResidentIds = firstTown.ResidentIds.Except(otherResidents, StringComparer.Ordinal).ToArray(),
                    AssignedBuildingIds = firstTown.AssignedBuildingIds.Except(otherBuildings, StringComparer.Ordinal).ToArray(),
                }, new TownRuntimeState(otherTownId, "Other Town", "founded", 0, otherResidents, otherBuildings,
                    firstTown.BorderTiles, destinationHouse.Position, Government: TownGovernmentState.Create())],
                WorldSimulation = state.WorldSimulation with
                {
                    Buildings = state.WorldSimulation.Buildings.Select(building => otherBuildings.Contains(building.InstanceId,
                        StringComparer.Ordinal) ? building with { TownId = otherTownId } : building).ToArray(),
                },
            };
        }
        else if (scenario == "no_town")
        {
            state = state with
            {
                Towns = state.Towns.Select(town => town with
                {
                    ResidentIds = town.ResidentIds.Where(id => id != child && id != adult).ToArray(),
                }).ToArray(),
            };
        }
        state = SettlementWeatherTestFixture.WithWeather(WithAdultCouncils(state), WeatherKind.Clear);
        var proposedResidents = society.GetHousehold(destinationHousehold).MemberIds.Select(society.GetInhabitant)
            .Append(society.GetInhabitant(child) with { DomesticFamilyUnitId = society.GetInhabitant(adult).DomesticFamilyUnitId });
        var capacity = HouseResidentCapacityRules.Calculate(proposedResidents, 1, 1);
        Assert.Equal(4, capacity.Limit);
        Assert.Equal(scenario == "full" ? 5 : 4, capacity.ResidentCount);
        Assert.Equal(scenario == "full", capacity.IsOvercrowded);

        if (ordered)
        {
            using var search = PrivateWorldRuntime.Restore(state, _ => new ParentProvider("safe_idle"));
            Assert.True((await search.AdvanceOneTickAsync()).Advanced);
            search.SubmitInstruction(GuardianRequest("relocation-order", adult, child));
            state = search.ExportState();
            Assert.Equal("waiting", Assert.Single(state.Instructions!).Order!.Status);
        }
        var provider = new ParentProvider(ordered ? "safe_idle" : "guardian_accept:");
        using var world = PrivateWorldRuntime.Restore(PrivateWorldRuntimeCodec.Decode(PrivateWorldRuntimeCodec.Encode(state)),
            actor => actor == adult ? provider : new ParentProvider("safe_idle"));
        var beforeAcceptance = PrivateWorldRuntimeCodec.Encode(world.ExportState());
        var ticks = 0;
        while (ticks < 40 && world.Society.GetInhabitant(child).PrimaryCaregiverId != adult)
        {
            Assert.True((await world.AdvanceOneTickAsync()).Advanced);
            ticks++;
        }
        if (ordered)
            Assert.Equal("finished", Assert.Single(world.ExportState().Instructions!).Order!.Status);
        else
            Assert.Contains(provider.SeenCandidates, candidate => candidate.Id == "guardian_accept:" + child);
        var result = world.Society.GetInhabitant(child);
        Assert.Equal(adult, result.PrimaryCaregiverId);
        Assert.Equal(scenario == "last_place" ? destinationHousehold : originHousehold, result.HouseholdId);
        Assert.Equal(parents[0], world.Society.Births.Single(birth => birth.ChildId == child).PrimaryCaregiverId);
        Assert.Equal(parents.Order(StringComparer.Ordinal), world.Society.Relationships
            .Where(edge => edge.Type == SocietyRelationshipType.BiologicalParentage && edge.TargetId == child)
            .Select(edge => edge.ProposerId).Order(StringComparer.Ordinal));
        var care = Assert.Single(world.Society.Relationships, edge => edge.Type == SocietyRelationshipType.Caregiver &&
            edge.ProposerId == adult && edge.TargetId == child && edge.State == SocietyRelationshipState.Accepted);
        Assert.Equal(SocietyConsentState.Accepted, care.Consent);
        Assert.Equal([adult], care.AcceptedParties);
        Assert.Null(world.Inhabitants.Single(person => person.InhabitantId == child).GuardianSearch);
        Assert.Single(world.ExportState().Events, item => item.Kind == "guardian_assigned" && item.Detail == child);
        if (scenario == "no_town")
            Assert.DoesNotContain(world.ExportState().Towns!, town => town.ResidentIds.Contains(child) || town.ResidentIds.Contains(adult));
        else Assert.Equal(originalTownId, world.ExportState().Towns!.Single(town => town.ResidentIds.Contains(child)).Id);
        var careGroup = SocietyFixture.MovingCareGroup(world.Society, adult);
        if (scenario == "last_place") Assert.Contains(child, careGroup);
        else
        {
            // A guardian whose child stayed in another household can still leave their own, and the child stays put.
            Assert.DoesNotContain(child, careGroup);
            var left = SocietyFixture.LeaveHousehold(world.Society, adult);
            Assert.Equal(adult, left.CreatedId);
            Assert.Null(left.Checkpoint.GetInhabitant(adult).HouseholdId);
            Assert.Equal(originHousehold, left.Checkpoint.GetInhabitant(child).HouseholdId);
        }
        var encoded = PrivateWorldRuntimeCodec.Encode(world.ExportState());
        Assert.Equal(encoded, PrivateWorldRuntimeCodec.Encode(PrivateWorldRuntimeCodec.Decode(encoded)));
        using var replay = PrivateWorldRuntime.Restore(PrivateWorldRuntimeCodec.Decode(beforeAcceptance),
            actor => actor == adult ? new ParentProvider(ordered ? "safe_idle" : "guardian_accept:") : new ParentProvider("safe_idle"));
        for (var tick = 0; tick < ticks; tick++) Assert.True((await replay.AdvanceOneTickAsync()).Advanced);
        Assert.Equal(encoded, PrivateWorldRuntimeCodec.Encode(replay.ExportState()));
    }

    /// <summary>Gives each Town the all-adult Council its current adult residents require.</summary>
    private static PrivateWorldRuntimeState WithAdultCouncils(PrivateWorldRuntimeState state) => state with
    {
        Towns = state.Towns!.Select(town => town with
        {
            Governance = TownGovernanceState.Create(town.ResidentIds.Where(id =>
                state.Society.Society.Inhabitants.Any(person => person.Id == id &&
                    person.Status == SocietyInhabitantStatus.Active &&
                    person.AgeBand is SocietyAgeBand.Adult or SocietyAgeBand.Elder))),
        }).ToArray(),
    };
}
