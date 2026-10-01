using System.Collections.Concurrent;
using System.Net;
using System.Text;
using System.Text.Json;
using ClankerWorld.Simulation.Cognition;
using ClankerWorld.Simulation.Kernel;
using ClankerWorld.Simulation.Playtest;
using ClankerWorld.Simulation.Society;
using ClankerWorld.Viewer.Observation;

namespace ClankerWorld.Simulation.Tests;

public sealed class TownHeirWillTests
{
    [Fact]
    public async Task ANaturalDeathOnTheNormalPathCanLeaveGoodsToTheTownWarehouseInOneModelRequest()
    {
        var handler = new WillModelHandler();
        using var client = new HttpClient(handler);
        var model = new OpenAiCompatibleDecisionProvider(client, () => "synthetic-key",
            new Uri("https://model.test/v1/chat/completions"), "synthetic-model");
        const string deceasedId = "founder:00000000000000000000000000000001";
        IDecisionProvider Provider(string id) => id == deceasedId ? model : new DeterministicDecisionProvider();
        using var generated = NormalPathWorld.CreateGenerated("will-town-heir", Provider);
        var state = generated.ExportState();
        var society = state.Society.Society;
        var lastDay = Assert.IsType<SocietyDayLifecycle>(society.Config.DayLifecycle).MaximumDay;
        // The founder reaches the last day of life on the next tick and dies of old age then.
        var birth = society.LifeTickAt(society.WorldTick + 1) - lastDay * society.Config.TicksPerLifecycleAge;
        var inventory = InventoryFixture.AddLot(society.Inventory, "will-wood", "wood", deceasedId, 4);
        inventory = InventoryFixture.AddLot(inventory, "will-food", "food", deceasedId, 2);
        society = society with
        {
            Inventory = inventory,
            Inhabitants = society.Inhabitants.Select(person => person.Id == deceasedId ? person with
            {
                BirthTick = society.LifeClock is null ? birth : person.BirthTick,
                BirthLifeTick = society.LifeClock is null ? null : birth,
                AgeBand = SocietyAgeBand.Elder,
                LastLifecycleYearChecked = lastDay - 1,
            } : person).ToArray(),
        };
        using var world = PrivateWorldRuntime.Restore(state with { Society = state.Society with { Society = society } }, Provider);

        Assert.True((await world.AdvanceOneTickNonBlockingAsync()).Advanced);
        var estate = Assert.Single(world.Society.Estates);
        Assert.Equal(deceasedId, estate.DeceasedId);
        Assert.Equal(TownBorderRules.FirstTownId, Assert.Single(world.ExportState().DeceasedInhabitants!).TownId);
        for (var attempt = 0; attempt < 40 && world.Society.GetEstate(estate.Id).WillStatus is null or "pending"; attempt++)
        {
            Assert.True((await world.AdvanceOneTickNonBlockingAsync()).Advanced);
            await Task.Delay(10);
        }
        estate = world.Society.GetEstate(estate.Id);
        Assert.Equal("accepted", estate.WillStatus);
        Assert.Equal("Keep the fire lit.", estate.FinalWords);
        Assert.Equal(TownBorderRules.FirstTownId, estate.WillHeirIds![0]);
        Assert.Equal(
        [
            new SocietyWillBequest("will-food", TownBorderRules.FirstTownId, 2),
            new SocietyWillBequest("will-wood", TownBorderRules.FirstTownId, 4),
        ], estate.WillBequests);
        var body = Assert.Single(handler.Bodies);
        using (var payload = JsonDocument.Parse(body))
        {
            var prompt = payload.RootElement.GetProperty("messages")[0].GetProperty("content").GetString()! +
                payload.RootElement.GetProperty("messages")[1].GetProperty("content").GetString()!;
            Assert.Null(RetiredWording.Find(prompt));
            Assert.DoesNotContain("will-wood", prompt, StringComparison.Ordinal);
        }
        var profile = new OwnerWorldObservationStore(world).GetSnapshot().Inhabitants.Single(item => item.Id == deceasedId).FinalWill!;
        var town = profile.Heirs[0];
        Assert.Equal(("First Town", true), (town.Name, town.IsTown));
        Assert.Equal([new ViewerInventoryEntry("food", 2), new ViewerInventoryEntry("wood", 4)], town.Items);

        // Bounded test clock: settle now instead of after the escrow days.
        var saved = world.ExportState();
        var shortened = saved.Society.Society with
        {
            Estates = saved.Society.Society.Estates.Select(item => item with { ExpiryTick = saved.Society.Society.WorldTick + 1 }).ToArray(),
        };
        using var settling = PrivateWorldRuntime.Restore(PrivateWorldRuntimeCodec.Decode(PrivateWorldRuntimeCodec.Encode(
            saved with { Society = saved.Society with { Society = shortened } })), Provider);
        Assert.True((await settling.AdvanceOneTickNonBlockingAsync()).Advanced);
        var settled = settling.Society;
        Assert.True(settled.GetEstate(estate.Id).Settled);
        var wood = Assert.Single(settled.Inventory.Lots, lot => lot.ProvenanceLotId == "will-wood");
        Assert.Equal((TownBorderRules.FirstTownId, "first-town-warehouse", 4), (wood.OwnerId, wood.StorageBuildingId, wood.Quantity));
        var food = settled.Inventory.Lots.Where(lot => lot.ProvenanceLotId == "will-food").ToArray();
        Assert.Equal(2, food.Sum(lot => lot.Quantity));
        Assert.All(food, lot => Assert.Contains(lot.OwnerId, estate.BeneficiaryIds));
        var personHeir = estate.WillHeirIds[1];
        Assert.Equal(food.Select(lot => lot.OwnerId).Append(personHeir).Distinct().Order(StringComparer.Ordinal),
            settled.Memories.Where(memory => memory.Id.StartsWith("final-words:", StringComparison.Ordinal))
                .Select(memory => memory.OwnerId).Order(StringComparer.Ordinal));
        Assert.Single(handler.Bodies);
    }

    [Fact]
    public void SettlementDeliversWhatTheTownCanKeepAndSendsTheRestToTheHousehold()
    {
        var config = new SocietyConfig(TicksPerWorldDay: 2, DaysPerWorldYear: 2, BaseNaturalMortalityBasisPoints: 0);
        var checkpoint = SocietyFixture.CreateGenesis("town-heir-fixture",
        [
            SocietyFixture.CreateFounder("alice", "Alice", "model:a", config: config),
            SocietyFixture.CreateFounder("bob", "Bob", "model:b", config: config),
            SocietyFixture.CreateFounder("cara", "Cara", "model:c", config: config),
        ], config: config);
        checkpoint = SocietyFixture.CreateHousehold(checkpoint, "home", "The Home", ["alice", "bob"]).Checkpoint;
        var inventory = InventoryFixture.AddLot(checkpoint.Inventory, "wood-lot", "wood", "alice", 5);
        inventory = InventoryFixture.AddLot(inventory, "bread-lot", "bread", "alice", 3);
        checkpoint = checkpoint with { Inventory = inventory };
        checkpoint = SocietyFixture.Kill(checkpoint, "alice", SocietyDeathCause.Hazard).Checkpoint;
        var estateId = "estate:alice:0";
        checkpoint = SocietyFixture.MarkWillStarted(checkpoint, estateId).Checkpoint;
        checkpoint = SocietyFixture.ResolveWill(checkpoint, estateId,
            new SocietyWillDirective(["town:first", "cara"], "items",
                new Dictionary<string, string> { ["wood-lot"] = "town:first", ["bread-lot"] = "town:first" }),
            "accepted", "Share the bread.", new HashSet<string>(["town:first"])).Checkpoint;
        Assert.Equal("accepted", checkpoint.GetEstate(estateId).WillStatus);
        checkpoint = SocietyFixture.Kill(checkpoint, "cara", SocietyDeathCause.Hazard).Checkpoint;

        var settled = SocietyFixture.AdvanceTo(checkpoint, config.EstateEscrowDays * config.TicksPerWorldDay,
            [new SocietyTownStore("town:first", "warehouse", 2, new HashSet<string>(["bread"]))]).Checkpoint;

        Assert.True(settled.GetEstate(estateId).Settled);
        Assert.Equal(
        [
            ("bob", "bread-lot", 3, (string?)null),
            ("bob", "wood-lot", 3, null),
            ("town:first", "wood-lot", 2, "warehouse"),
        ], settled.Inventory.Lots.Where(lot => lot.ProvenanceLotId is "wood-lot" or "bread-lot")
            .Select(lot => (lot.OwnerId, lot.ProvenanceLotId!, lot.Quantity, lot.StorageBuildingId))
            .OrderBy(item => item.OwnerId, StringComparer.Ordinal).ThenBy(item => item.Item2, StringComparer.Ordinal));
        var memory = Assert.Single(settled.Memories, item => item.Id.StartsWith("final-words:", StringComparison.Ordinal));
        Assert.Equal(("bob", "alice", "Alice's final words were: 'Share the bread.'", "private"),
            (memory.OwnerId, memory.SubjectId, memory.Summary, memory.Visibility));
        Assert.Equal(settled, SocietyCheckpointCodec.Decode(SocietyCheckpointCodec.Encode(settled)), SocietyCheckpointComparer.Instance);
    }

    [Fact]
    public void AnHeirWhoAlsoTakesAHouseholdShareOfTheSameLotGetsOneMergedLot()
    {
        var config = new SocietyConfig(TicksPerWorldDay: 2, DaysPerWorldYear: 2, BaseNaturalMortalityBasisPoints: 0);
        var checkpoint = SocietyFixture.CreateGenesis("merged-heir-fixture",
        [
            SocietyFixture.CreateFounder("alice", "Alice", "model:a", config: config),
            SocietyFixture.CreateFounder("bob", "Bob", "model:b", config: config),
        ], config: config);
        checkpoint = SocietyFixture.CreateHousehold(checkpoint, "home", "The Home", ["alice", "bob"]).Checkpoint;
        checkpoint = checkpoint with { Inventory = InventoryFixture.AddLot(checkpoint.Inventory, "bread-lot", "bread", "alice", 5) };
        checkpoint = SocietyFixture.Kill(checkpoint, "alice", SocietyDeathCause.Hazard).Checkpoint;
        const string estateId = "estate:alice:0";
        checkpoint = SocietyFixture.MarkWillStarted(checkpoint, estateId).Checkpoint;
        checkpoint = SocietyFixture.ResolveWill(checkpoint, estateId,
            new SocietyWillDirective(["town:first", "bob"], "equal"), "accepted",
            townHeirIds: new HashSet<string>(["town:first"])).Checkpoint;
        Assert.Equal([new SocietyWillBequest("bread-lot", "town:first", 3), new SocietyWillBequest("bread-lot", "bob", 2)],
            checkpoint.GetEstate(estateId).WillBequests);

        var settled = SocietyFixture.AdvanceTo(checkpoint, config.EstateEscrowDays * config.TicksPerWorldDay,
            [new SocietyTownStore("town:first", "warehouse", 10, new HashSet<string>(["bread"]))]).Checkpoint;

        var bread = Assert.Single(settled.Inventory.Lots, lot => lot.ProvenanceLotId == "bread-lot");
        Assert.Equal(("bob", 5, "bread-lot#estate:estate:alice:0:bob"), (bread.OwnerId, bread.Quantity, bread.Id));
        Assert.Empty(settled.Memories);
    }

    [Fact]
    public void TheTownKeepsAJugWithItsWaterButAPotOfFoodGoesToTheHousehold()
    {
        var config = new SocietyConfig(TicksPerWorldDay: 2, DaysPerWorldYear: 2, BaseNaturalMortalityBasisPoints: 0);
        var checkpoint = SocietyFixture.CreateGenesis("vessel-heir-fixture",
        [
            SocietyFixture.CreateFounder("alice", "Alice", "model:a", config: config),
            SocietyFixture.CreateFounder("bob", "Bob", "model:b", config: config),
        ], config: config);
        checkpoint = SocietyFixture.CreateHousehold(checkpoint, "home", "The Home", ["alice", "bob"]).Checkpoint;
        var inventory = InventoryFixture.AddLot(checkpoint.Inventory, "jug", InventoryContainerRules.WaterJug, "alice", 1);
        inventory = InventoryFixture.AddLot(inventory, "water", InventoryContainerRules.FreshWater, "alice", 4, containerLotId: "jug");
        inventory = InventoryFixture.AddLot(inventory, "pot", InventoryContainerRules.StoragePot, "alice", 1);
        inventory = InventoryFixture.AddLot(inventory, "berries", "berries", "alice", 3, containerLotId: "pot");
        checkpoint = checkpoint with { Inventory = inventory };
        checkpoint = SocietyFixture.Kill(checkpoint, "alice", SocietyDeathCause.Hazard).Checkpoint;
        const string estateId = "estate:alice:0";
        checkpoint = SocietyFixture.MarkWillStarted(checkpoint, estateId).Checkpoint;
        checkpoint = SocietyFixture.ResolveWill(checkpoint, estateId,
            new SocietyWillDirective(["town:first"], "equal"), "accepted",
            townHeirIds: new HashSet<string>(["town:first"])).Checkpoint;
        Assert.Equal(
        [
            new SocietyWillBequest("jug", "town:first", 1),
            new SocietyWillBequest("water", "town:first", 4),
            new SocietyWillBequest("pot", "town:first", 1),
            new SocietyWillBequest("berries", "town:first", 3),
        ], checkpoint.GetEstate(estateId).WillBequests);

        var settled = SocietyFixture.AdvanceTo(checkpoint, config.EstateEscrowDays * config.TicksPerWorldDay,
            [new SocietyTownStore("town:first", "warehouse", 10, new HashSet<string>(["berries"]))]).Checkpoint;

        Assert.Equal(
        [
            ("berries", "bob", (string?)null, 3, "pot"),
            ("jug", "town:first", "warehouse", 1, null),
            ("pot", "bob", null, 1, null),
            ("water", "town:first", "warehouse", 4, "jug"),
        ], settled.Inventory.Lots.Where(lot => lot.Id is "berries" or "jug" or "pot" or "water")
            .Select(lot => (lot.Id, lot.OwnerId, lot.StorageBuildingId, lot.Quantity, lot.ContainerLotId)));
    }

    private sealed class SocietyCheckpointComparer : IEqualityComparer<SocietyCheckpoint>
    {
        public static readonly SocietyCheckpointComparer Instance = new();

        public bool Equals(SocietyCheckpoint? x, SocietyCheckpoint? y) =>
            x is not null && y is not null &&
            SocietyCheckpointCodec.StateDigest(x) == SocietyCheckpointCodec.StateDigest(y);

        public int GetHashCode(SocietyCheckpoint obj) => SocietyCheckpointCodec.StateDigest(obj).GetHashCode(StringComparison.Ordinal);
    }

    /// <summary>A synthetic model that leaves food and wood to the Town and names one person.</summary>
    private sealed class WillModelHandler : HttpMessageHandler
    {
        public ConcurrentQueue<string> Bodies { get; } = new();

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var body = await request.Content!.ReadAsStringAsync(cancellationToken);
            Bodies.Enqueue(body);
            using var payload = JsonDocument.Parse(body);
            using var user = JsonDocument.Parse(payload.RootElement.GetProperty("messages")[1].GetProperty("content").GetString()!);
            var heirs = user.RootElement.GetProperty("possible_heirs").EnumerateArray()
                .Select(item => item.GetProperty("id").GetString()!).ToArray();
            var town = heirs.Single(id => id.StartsWith("will:town:", StringComparison.Ordinal));
            var person = heirs.First(id => id.StartsWith("will:heir:", StringComparison.Ordinal));
            var items = user.RootElement.GetProperty("estate").EnumerateArray()
                .ToDictionary(item => item.GetProperty("id").GetString()!, _ => town);
            var answer = JsonSerializer.Serialize(new Dictionary<string, object>
            {
                ["selected_candidate_id"] = "will:heirs",
                ["confidence"] = 0.9,
                ["heirs"] = new[] { town, person },
                ["split"] = "items",
                ["items"] = items,
                ["final_words"] = "  Keep the\nfire lit.  ",
            });
            var reply = JsonSerializer.Serialize(new { choices = new[] { new { message = new { content = answer } } } });
            return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(reply, Encoding.UTF8, "application/json") };
        }
    }
}
