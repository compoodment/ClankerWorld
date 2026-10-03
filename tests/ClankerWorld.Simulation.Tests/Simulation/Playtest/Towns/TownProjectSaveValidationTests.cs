using System.Text;
using System.Text.Json.Nodes;
using ClankerWorld.Simulation.Playtest;
using ClankerWorld.Viewer.Observation;

namespace ClankerWorld.Simulation.Tests;

public sealed class TownProjectSaveValidationTests
{
    [Fact]
    public async Task GenuineApprovalReloadsWhileForgedAuthorityScopeAndHistoryAreRefused()
    {
        using var scenario = await TownProjectScenario.ApprovedAsync();
        var healthy = PrivateWorldRuntimeCodec.Encode(scenario.World.ExportState());
        Assert.Equal(healthy, PrivateWorldRuntimeCodec.Encode(PrivateWorldRuntimeCodec.Decode(healthy)));
        foreach (var damage in new[] { "approval", "site", "budget", "duplicate", "work", "typed-payload", "future-transition" })
        {
            var document = JsonNode.Parse(healthy)!;
            var town = document["state"]!["towns"]![0]!;
            var project = town["projects"]![0]!;
            switch (damage)
            {
                case "approval": project["proposalId"] = "unrelated-proposal"; break;
                case "site":
                    project["plan"]!["site"]!["x"] = project["plan"]!["site"]!["x"]!.GetValue<int>() + 1;
                    project["plan"]!["entrance"]!["x"] = project["plan"]!["entrance"]!["x"]!.GetValue<int>() + 1;
                    break;
                case "budget": project["plan"]!["budget"]![0]!["amount"] = 25; break;
                case "duplicate": town["projects"]!.AsArray().Add(project.DeepClone()); break;
                case "work": project["workDone"] = 11; break;
                case "typed-payload": town["governance"]!["proposals"]![0]!.AsObject().Remove("project"); break;
                case "future-transition": project["lastTransitionTick"] = scenario.World.WorldTick + 1; break;
            }
            var damaged = Encoding.UTF8.GetBytes(document.ToJsonString());
            Assert.Throws<InvalidDataException>(() => PrivateWorldRuntimeCodec.Decode(damaged));
            Assert.Equal(healthy, PrivateWorldRuntimeCodec.Encode(scenario.World.ExportState()));
            if (damage == "approval") AssertDamagedFilePreserved(scenario.World.ExportState().WorldSeed, damaged);
        }
    }

    [Fact]
    public async Task GenuinePhysicalDeliveryReloadsWhileChangedReceiptsOriginsAndGroundAreRefused()
    {
        using var scenario = await TownProjectScenario.ApprovedAsync(initialTownStock: true);
        scenario.Policy.Supply = true;
        await scenario.UntilAsync(() => scenario.Project.Deliveries.Any(delivery => delivery.DeliveredTick is not null), 120);
        Assert.NotEqual("completed", scenario.Project.Stage);
        var delivery = scenario.Project.Deliveries.First(item => item.DeliveredTick is not null);
        var healthy = PrivateWorldRuntimeCodec.Encode(scenario.World.ExportState());
        Assert.Equal(healthy, PrivateWorldRuntimeCodec.Encode(PrivateWorldRuntimeCodec.Decode(healthy)));
        foreach (var damage in new[] { "receipt", "purpose", "source", "ground", "duplicate-delivery", "expiry" })
        {
            var document = JsonNode.Parse(healthy)!;
            var state = document["state"]!;
            var retained = state["towns"]![0]!["projects"]![0]!["deliveries"]!.AsArray()
                .Single(item => item!["id"]!.GetValue<string>() == delivery.Id)!;
            var inventory = state["society"]!["society"]!["inventory"]!;
            var receipt = inventory["reservations"]!.AsArray().Single(item =>
                item!["id"]!.GetValue<string>() == delivery.ReservationId)!;
            var lot = inventory["lots"]!.AsArray().Single(item => item!["id"]!.GetValue<string>() == delivery.LotId)!;
            switch (damage)
            {
                case "receipt": retained["reservationId"] = "unrelated-receipt"; break;
                case "purpose": receipt["purpose"] = "unrelated-town-work"; break;
                case "source": retained["sourceLotId"] = "unrelated-source"; break;
                case "ground": lot["groundPosition"]!["x"] = scenario.Project.Plan.Site.X + 1; break;
                case "duplicate-delivery": state["towns"]![0]!["projects"]![0]!["deliveries"]!.AsArray().Add(retained.DeepClone()); break;
                case "expiry": receipt["expiryTick"] = scenario.World.WorldTick + 100; break;
            }
            Assert.Throws<InvalidDataException>(() => PrivateWorldRuntimeCodec.Decode(Encoding.UTF8.GetBytes(document.ToJsonString())));
            Assert.Equal(healthy, PrivateWorldRuntimeCodec.Encode(scenario.World.ExportState()));
        }
    }

    private static void AssertDamagedFilePreserved(string seed, byte[] damaged)
    {
        var directory = Directory.CreateTempSubdirectory("town-project-damaged-");
        try
        {
            var path = Path.Combine(directory.FullName, "runtime.json");
            File.WriteAllBytes(path, damaged);
            Assert.Throws<InvalidDataException>(() => new PrivateWorldStateFile(path).LoadOrCreate(seed));
            Assert.Equal(damaged, File.ReadAllBytes(path));
        }
        finally { directory.Delete(recursive: true); }
    }

    internal static void AssertCompletedSourceCannotChange(PrivateWorldRuntimeState state)
    {
        Assert.Equal("completed", Assert.Single(state.Towns![0].Projects).Stage);
        var healthy = PrivateWorldRuntimeCodec.Encode(state);
        Assert.Equal(healthy, PrivateWorldRuntimeCodec.Encode(PrivateWorldRuntimeCodec.Decode(healthy)));
        foreach (var damage in new[] { "source", "receipt-identity" })
        {
            var damaged = JsonNode.Parse(healthy)!;
            var delivery = damaged["state"]!["towns"]![0]!["projects"]![0]!["deliveries"]![0]!;
            if (damage == "source") delivery["sourceLotId"] = "unrelated-spent-source";
            else
            {
                var originalReceipt = delivery["reservationId"]!.GetValue<string>();
                var receipt = damaged["state"]!["society"]!["society"]!["inventory"]!["reservations"]!.AsArray()
                    .Single(item => item!["id"]!.GetValue<string>() == originalReceipt)!;
                receipt["id"] = "unrelated-completed-receipt";
                delivery["reservationId"] = "unrelated-completed-receipt";
            }
            Assert.Throws<InvalidDataException>(() => PrivateWorldRuntimeCodec.Decode(Encoding.UTF8.GetBytes(damaged.ToJsonString())));
            Assert.Equal(healthy, PrivateWorldRuntimeCodec.Encode(state));
        }
    }
}
