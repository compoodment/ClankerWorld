using ClankerWorld.Simulation.Cognition;
using ClankerWorld.Simulation.Harness;
using ClankerWorld.Simulation.Kernel;
using ClankerWorld.Simulation.Playtest;
using ClankerWorld.Simulation.Society;

namespace ClankerWorld.Simulation.Tests;

public sealed class ChildCarriedMilkTests
{
    private static readonly Lazy<Task<byte[]>> Family = new(CreateFamily);

    // Existing adult and boat checks miss the child's ordinary candidate and final age gates.
    [Theory]
    [InlineData("child-milk")]
    [InlineData("adult-milk")]
    [InlineData("child-bread")]
    [InlineData("broken-jug")]
    [InlineData("reserved-milk")]
    [InlineData("spoiled-milk")]
    public async Task HungryResidentCanConsumeTheirOwnCarriedFood(string scenario)
    {
        var state = PrivateWorldRuntimeCodec.Decode(await Family.Value);
        var child = Assert.Single(state.Society.Society.Births).ChildId;
        var actor = scenario == "adult-milk" ? state.Society.Society.Births[0].PrimaryCaregiverId : child;
        var milk = scenario != "child-bread";
        var usable = scenario is "child-milk" or "adult-milk" or "child-bread";
        var inventory = state.Society.Society.Inventory;
        var home = state.Society.Society.GetInhabitant(actor).HouseholdId!;
        var members = state.Society.Society.GetHousehold(home).MemberIds.ToHashSet(StringComparer.Ordinal);
        inventory = inventory with
        {
            Lots = inventory.Lots.Where(lot => lot.OwnerId != home && !members.Contains(lot.OwnerId)).ToArray(),
        };
        if (milk) inventory = InventoryFixture.AddLot(inventory, "child-milk-jug", "water_jug", actor, 1);
        inventory = InventoryFixture.AddLot(inventory, "child-carried-food", milk ? "milk" : "bread", actor, 2,
            containerLotId: milk ? "child-milk-jug" : null);
        if (scenario == "reserved-milk")
            inventory = InventoryFixture.Reserve(inventory, "child-milk-held", actor, "child-carried-food", 2,
                "other-personal-work", long.MaxValue);
        if (scenario == "broken-jug") inventory = inventory with
        { Lots = inventory.Lots.Select(lot => lot.Id == "child-milk-jug" ? lot with { ConditionBasisPoints = 0 } : lot).ToArray() };
        if (scenario == "spoiled-milk") inventory = inventory with
        { Lots = inventory.Lots.Select(lot => lot.Id == "child-carried-food" ? lot with { FreshnessBasisPoints = 0 } : lot).ToArray() };
        state = state with
        {
            Society = state.Society with { Society = state.Society.Society with { Inventory = inventory } },
            Inhabitants = state.Inhabitants.Select(person => person with
            {
                HungerBasisPoints = person.InhabitantId == actor ? 1_500 : 10_000,
                LastDecisionContext = null,
                Project = null,
                Survival = new(10_000),
            }).ToArray(),
        };
        var choices = new FoodChoices(actor, milk ? "drink_milk" : "consume_food");
        using var world = PrivateWorldRuntime.Restore(PrivateWorldRuntimeCodec.Decode(PrivateWorldRuntimeCodec.Encode(state)), _ => choices);
        var bytes = PrivateWorldRuntimeCodec.Encode(world.ExportState());
        using var replay = PrivateWorldRuntime.Restore(PrivateWorldRuntimeCodec.Decode(bytes),
            _ => new FoodChoices(actor, choices.Wanted));
        Assert.False((await world.AdvanceOneTickAsync(commitPermitted: () => false)).Advanced);
        Assert.Equal(bytes, PrivateWorldRuntimeCodec.Encode(world.ExportState()));
        for (var tick = 0; tick < 4; tick++)
        {
            Assert.True((await world.AdvanceOneTickAsync()).Advanced);
            Assert.True((await replay.AdvanceOneTickAsync()).Advanced);
            Assert.Equal(PrivateWorldRuntimeCodec.Encode(world.ExportState()), PrivateWorldRuntimeCodec.Encode(replay.ExportState()));
        }
        var saved = PrivateWorldRuntimeCodec.Encode(world.ExportState());
        using var restored = PrivateWorldRuntime.Restore(PrivateWorldRuntimeCodec.Decode(saved));
        Assert.Equal(saved, PrivateWorldRuntimeCodec.Encode(restored.ExportState()));
        Assert.Equal(usable, choices.Seen.Contains(choices.Wanted));
        Assert.Equal(usable ? 1 : 2, world.Society.Inventory.GetLot("child-carried-food").Quantity);
        Assert.Equal(actor, world.Society.Inventory.GetLot("child-carried-food").OwnerId);
        var fullness = world.Inhabitants.Single(person => person.InhabitantId == actor).HungerBasisPoints;
        Assert.True(usable ? fullness > 4_000 : fullness < 1_500);
        if (milk)
        {
            Assert.Equal(usable ? 1 : 0, world.ExportState().Events.Count(item => item.Kind == "milk_drunk" && item.Detail == actor));
            var jug = world.Society.Inventory.GetLot("child-milk-jug");
            Assert.Equal((actor, 1, scenario == "broken-jug" ? 0 : 10_000), (jug.OwnerId, jug.Quantity, jug.ConditionBasisPoints));
        }
        if (actor == child)
            Assert.DoesNotContain(choices.Seen, id => id.StartsWith("animal:", StringComparison.Ordinal));
        world.Validate();
    }

    private static async Task<byte[]> CreateFamily()
    {
        using var initial = NormalPathWorld.CreateGenerated("child-carried-milk-audit", _ => new FoodChoices("", "safe_idle"));
        Assert.True((await initial.AdvanceOneTickAsync()).Advanced);
        var state = initial.ExportState();
        var checkpoint = state.Society.Society;
        var home = checkpoint.GetHousehold("household:camp-alpha");
        var parent = home.MemberIds[0];
        var partner = home.MemberIds[1];
        checkpoint = SocietyFixture.ProposeRelationship(checkpoint, new("milk-parents", 1,
            SocietyRelationshipType.Partnership, parent, partner, checkpoint.WorldTick)).Checkpoint;
        checkpoint = SocietyFixture.AcceptRelationship(checkpoint, "milk-parents", 1, partner).Checkpoint;
        checkpoint = ChosenBirthNameTestFixture.NameParent(checkpoint, parent);
        checkpoint = checkpoint with { Inventory = InventoryFixture.AddLot(checkpoint.Inventory, "milk-birth-food", "food", home.Id, 4) };
        var birth = SocietyFixture.CommitBirth(checkpoint, new("milk-birth", 1, parent, partner, home.Id,
            home.MemberIds, [parent, partner], "milk-birth-food", 4, checkpoint.WorldTick,
            ChildName: ChosenBirthNameTestFixture.ChildName(checkpoint, parent, "Robin"), PrimaryCaregiverId: parent));
        var child = Assert.IsType<string>(birth.CreatedId);
        checkpoint = birth.Checkpoint;
        var age = checkpoint.LifeTickAt(checkpoint.WorldTick) - 4 * checkpoint.Config.TicksPerLifecycleAge;
        checkpoint = checkpoint with
        {
            Inhabitants = checkpoint.Inhabitants.Select(person => person.Id == child ? person with
            {
                BirthTick = age,
                BirthLifeTick = checkpoint.LifeClock is null ? null : age,
                AgeBand = SocietyAgeBand.Child,
                LastLifecycleYearChecked = 4,
            } : person).ToArray(),
        };
        var position = state.Map.FootNeighbors(state.Inhabitants.Single(person => person.InhabitantId == parent).Position)
            .First(point => state.Map.IsPassable(point) && state.Inhabitants.All(person => person.Position != point));
        state = state with
        {
            JevEnabled = false,
            RoutineHelper = RoutineHelperSettings.Off,
            Society = state.Society with { Society = checkpoint },
            Inhabitants = state.Inhabitants.Append(new(child, position, 10_000, 0, "curious", "grow")).ToArray(),
            Towns = state.Towns!.Select(town => town.ResidentIds.Contains(parent)
                ? town with { ResidentIds = town.ResidentIds.Append(child).Order(StringComparer.Ordinal).ToArray() } : town).ToArray(),
        };
        return PrivateWorldRuntimeCodec.Encode(state);
    }

    private sealed class FoodChoices(string actor, string wanted) : IDecisionProvider
    {
        public string Wanted => wanted;
        public HashSet<string> Seen { get; } = new(StringComparer.Ordinal);
        public DecisionProviderKind Kind => DecisionProviderKind.Deterministic;
        public long ProviderEpoch => 0;
        public ValueTask<CognitionDecisionResponse> DecideAsync(CognitionDecisionRequest request, CancellationToken cancellationToken = default)
        {
            var chosen = "safe_idle";
            if (request.Observation.InhabitantId == actor)
            {
                foreach (var candidate in request.Observation.Candidates) Seen.Add(candidate.Id);
                if (Seen.Contains(wanted) && request.Observation.Candidates.Any(candidate => candidate.Id == wanted)) chosen = wanted;
            }
            return ValueTask.FromResult(new CognitionDecisionResponse(request.RequestId, request.Observation.InhabitantId, Kind,
                ProviderEpoch, request.Observation.RunEpoch, request.Observation.DecisionGeneration, request.Observation.ObservationDigest,
                chosen, 1, new Dictionary<string, double> { [chosen] = 1 }));
        }
    }
}
