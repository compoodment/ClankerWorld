using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using ClankerWorld.Simulation.Content;
using ClankerWorld.Simulation.Cognition;
using ClankerWorld.Simulation.Harness;
using ClankerWorld.Simulation.Kernel;
using ClankerWorld.Simulation.Society;
using ClankerWorld.Simulation.World;

namespace ClankerWorld.Simulation.Playtest;

public sealed partial class PrivateWorldRuntime
{
    private static WorldSystemsState CreateWorldSystems(string worldSeed, SeededMap map,
        WorldStartPace startPace = WorldStartPace.Legacy, bool regionalWeather = true)
    {
        var config = WorldStartPaceRules.WorldSystems(startPace);
        var resources = map.Resources
            .Select(resource => resource.TreeKind is not null
                ? TreeGrowthRules.GeneratedTree(resource)
                : resource.IsRenewable
                ? new EcologyResource(
                    resource.Id,
                    resource.Kind,
                    resource.Position,
                    true,
                    8,
                    12,
                    2,
                    1,
                    SeasonKind.Spring,
                    1,
                    EcologyResourceState.Available)
                : new EcologyResource(
                    resource.Id,
                    resource.Kind,
                    resource.Position,
                    false,
                    3,
                    3,
                    0,
                    0,
                    SeasonKind.Spring,
                    0,
                    EcologyResourceState.Available))
            .ToArray();
        var culture = new CultureState(
            [new CultureDefinition("camp", "Camp", ["cooperation", "survival"])],
            [
                new CultureAssignment("founder-scout", "camp", ["mapping"]),
                new CultureAssignment("founder-mira", "camp", ["harvest"]),
                new CultureAssignment("founder-rowan", "camp", ["building"]),
                new CultureAssignment("founder-ilya", "camp", ["memory"]),
            ]);
        var factions = new FactionState(
            [new FactionDefinition("camp-alpha", "Camp Alpha", ["camp"])],
            [
                new FactionStanding("founder-scout", "camp-alpha", 0),
                new FactionStanding("founder-mira", "camp-alpha", 0),
                new FactionStanding("founder-rowan", "camp-alpha", 0),
                new FactionStanding("founder-ilya", "camp-alpha", 0),
            ],
            [new LawRule("camp-no-theft", "camp-alpha", LawActionKind.Theft, LawSeverity.Major, 500, 25)],
            []);
        var currency = new CurrencyState(
            [new CurrencyDefinition("copper", "Copper", "cp")],
            [new CurrencyAccount("camp-wallet", HouseholdId, "copper", 100)],
            []);
        var chunkSize = map.Width > ChunkRules.DefaultChunkSize || map.Height > ChunkRules.DefaultChunkSize
            ? GeographyGenerator.ChunkSize : ChunkRules.DefaultChunkSize;
        var chunks = new List<ChunkManifest>();
        for (var top = 0; top < map.Height; top += chunkSize)
            for (var left = 0; left < map.Width; left += chunkSize)
            {
                var coordinate = new ChunkCoordinate(left / chunkSize, top / chunkSize);
                var chunkWidth = Math.Min(chunkSize, map.Width - left);
                var chunkHeight = Math.Min(chunkSize, map.Height - top);
                chunks.Add(ChunkManifestCodec.WithDigest(new ChunkManifest(
                    coordinate,
                    chunkSize,
                    chunkWidth,
                    chunkHeight,
                    map.Width > ChunkRules.DefaultChunkSize || map.Height > ChunkRules.DefaultChunkSize
                        ? "noise-drainage-camp" : SeededMapGenerator.GeneratorId,
                    map.Width > ChunkRules.DefaultChunkSize || map.Height > ChunkRules.DefaultChunkSize
                        ? "v2" : SeededMapGenerator.GeneratorVersion,
                    map.Resources.Where(resource => resource.Position.X >= left &&
                            resource.Position.X < left + chunkWidth && resource.Position.Y >= top &&
                            resource.Position.Y < top + chunkHeight)
                        .Select(resource => new ChunkResourceMetadata(
                            resource.Id, resource.Kind,
                            new GridPoint(resource.Position.X - left, resource.Position.Y - top),
                            resource.IsRenewable)).ToArray())));
            }
        var state = WorldSystemsRules.CreateGenesis(
            worldSeed,
            config,
            resources,
            factions,
            currency,
            culture,
            chunks);
        return regionalWeather ? RegionalWeatherRules.Initialize(state, map) : state;
    }

    private static WorldSystemsState AdvanceWorldSystemsTo(WorldSystemsState state, long targetTick)
    {
        while (state.WorldTick < targetTick)
        {
            state = WorldSystemsRules.AdvanceOneTick(state);
        }

        return state;
    }

    private void SyncEcologyResourceStates()
    {
        foreach (var resource in worldSystems.Ecology.Resources)
        {
            resources[resource.Id] = resource.State == EcologyResourceState.Available && resource.Quantity > 0
                ? ResourceState.Available
                : ResourceState.Depleted;
        }
    }

    private static SocietyWorldRuntime CreateSociety(
        string worldSeed,
        Func<string, IDecisionProvider>? providerFactory,
        int maxCognitionQueueLength,
        int maxCognitionDispatchPerCycle,
        WorldStartPace startPace)
    {
        var config = WorldStartPaceRules.Society(startPace);
        int Age(int index) => SocietyFixture.FounderArrivalAge(config, worldSeed, index);
        var founders = new[]
        {
            SocietyFixture.CreateFounder("founder-scout", "Scout", "model:scout", config: config, startingAge: Age(0)),
            SocietyFixture.CreateFounder("founder-mira", "Mira", "model:mira", config: config, startingAge: Age(1)),
            SocietyFixture.CreateFounder("founder-rowan", "Rowan", "model:rowan", config: config, startingAge: Age(2)),
            SocietyFixture.CreateFounder("founder-ilya", "Ilya", "model:ilya", config: config, startingAge: Age(3)),
        };
        var initialFounders = startPace == WorldStartPace.FounderSetup ? [] : founders;
        var checkpoint = SocietyFixture.CreateGenesis(
            worldSeed,
            initialFounders,
            [
                new InventoryLot(FoodLotId, "food", HouseholdId, 32, 10_000, 10_000, 0),
                new InventoryLot("wood:camp-alpha", "wood", HouseholdId, 48, 10_000, 10_000, 0),
                new InventoryLot("tools:camp-alpha", "tool", HouseholdId, 4, 10_000, 10_000, 0),
                ..(startPace == WorldStartPace.Legacy ? new InventoryLot[]
                {
                    new("legacy-wooden-axe:camp-alpha", "wooden_axe", HouseholdId, 1, 10_000, 10_000, 0),
                    new("legacy-wooden-pickaxe:camp-alpha", "wooden_pickaxe", HouseholdId, 1, 10_000, 10_000, 0),
                } : []),
                ..(startPace == WorldStartPace.FounderSetup ? new InventoryLot[]
                {
                    new("food:camp-beta", "food", SecondHouseholdId, 16, 10_000, 10_000, 0),
                    new("wood:camp-beta", "wood", SecondHouseholdId, 24, 10_000, 10_000, 0),
                    new("tools:camp-beta", "tool", SecondHouseholdId, 2, 10_000, 10_000, 0),
                    new("seeds:camp-alpha", "grain_seed", HouseholdId, 8, 10_000, 10_000, 0),
                    new("green-seeds:camp-alpha", "cultivated_green_seed", HouseholdId, 4, 10_000, 10_000, 0),
                    new("potatoes:camp-alpha", "potatoes", HouseholdId, 4, 10_000, 10_000, 0),
                    new("clothing:camp-alpha", "clothing", HouseholdId, 2, 10_000, 10_000, 0),
                    new("seeds:camp-beta", "grain_seed", SecondHouseholdId, 8, 10_000, 10_000, 0),
                    new("green-seeds:camp-beta", "cultivated_green_seed", SecondHouseholdId, 4, 10_000, 10_000, 0),
                    new("potatoes:camp-beta", "potatoes", SecondHouseholdId, 4, 10_000, 10_000, 0),
                    new("clothing:camp-beta", "clothing", SecondHouseholdId, 2, 10_000, 10_000, 0),
                } : []),
            ],
            config,
            "model:world-default");
        checkpoint = SocietyFixture.CreateHousehold(
            checkpoint,
            HouseholdId,
            "First household",
            initialFounders.Select(item => item.Id)).Checkpoint;
        if (startPace == WorldStartPace.FounderSetup)
            checkpoint = SocietyFixture.CreateHousehold(checkpoint, SecondHouseholdId, "Second household", []).Checkpoint;
        if (startPace != WorldStartPace.FounderSetup)
        {
            checkpoint = SocietyFixture.AssignRole(checkpoint, "founder-scout", SocietyWorkRole.Trader).Checkpoint;
            checkpoint = SocietyFixture.AssignRole(checkpoint, "founder-mira", SocietyWorkRole.Farmer).Checkpoint;
            checkpoint = SocietyFixture.AssignRole(checkpoint, "founder-rowan", SocietyWorkRole.Builder).Checkpoint;
            checkpoint = SocietyFixture.AssignRole(checkpoint, "founder-ilya", SocietyWorkRole.Teacher).Checkpoint;
        }
        return new SocietyWorldRuntime(
            checkpoint,
            providerFactory,
            maxCognitionQueueLength,
            maxCognitionDispatchPerCycle);
    }

    private void CreatePhysicalState()
    {
        var startingPositions = new[]
        {
            new GridPoint(0, 0),
            new GridPoint(1, 1),
            new GridPoint(2, 1),
            new GridPoint(3, 1),
        };
        var profiles = new (string Id, string Personality, string Aspiration)[]
        {
            ("founder-scout", "curious", "map the nearby world"),
            ("founder-mira", "practical", "make the camp self-sufficient"),
            ("founder-rowan", "patient", "build something lasting"),
            ("founder-ilya", "observant", "teach and preserve memory"),
        };
        for (var index = 0; index < profiles.Length; index++)
        {
            var profile = profiles[index];
            inhabitants.Add(
                profile.Id,
                new PlaytestInhabitantState(
                    profile.Id,
                    startingPositions[index],
                    6_500,
                    0,
                    profile.Personality,
                    profile.Aspiration));
        }
    }

    private void DrainNeeds()
    {
        foreach (var state in inhabitants.Values.ToArray())
        {
            inhabitants[state.InhabitantId] = state with
            {
                HungerBasisPoints = Math.Max(0, state.HungerBasisPoints - 4),
            };
        }
    }

    private void RemoveDeadPhysicalState()
    {
        var activeIds = society.Checkpoint.Inhabitants
            .Where(item => item.Status == SocietyInhabitantStatus.Active)
            .Select(item => item.Id)
            .ToHashSet(StringComparer.Ordinal);
        foreach (var id in inhabitants.Keys.Where(id => !activeIds.Contains(id)).ToArray())
        {
            if (PendingInstructionFor(id) is { Order.Action: "construct_building" or "expand_building" } buildingOrder)
            {
                CancelConstructionForOrder(buildingOrder);
                CancelExpansionForOrder(buildingOrder);
                SetOrderStatus(buildingOrder, "blocked", "The ordered building worker is no longer alive.");
            }
            if (PendingInstructionFor(id) is { Order.Action: "produce_item" } productionOrder)
            {
                CancelProductionForOrder(productionOrder);
                SetOrderStatus(productionOrder, "blocked", "The ordered production worker is no longer alive.");
            }
            if (society.Checkpoint.Inventory.Lots.Any(lot => lot.CarrierId == id))
            {
                var position = inhabitants[id].Position;
                ApplyInventoryTransition(inventory => InventoryFixture.DropCarrierGoods(inventory, id,
                    new InventoryGroundPosition(position.X, position.Y)));
            }
            var deceased = society.Checkpoint.GetInhabitant(id);
            var deathTick = deceased.DeathTick ?? throw new InvalidDataException("A removed inhabitant has no committed death.");
            // Society escrows ownership at death. Goods that had no separate
            // custodian or storage were carried by their owner; keep their
            // physical position at the deceased's tile after that owner is gone.
            var estate = society.Checkpoint.Estates.SingleOrDefault(item => item.DeceasedId == id);
            if (estate is not null)
            {
                var position = inhabitants[id].Position;
                foreach (var lot in society.Checkpoint.Inventory.Lots.Where(item => item.OwnerId == estate.Id &&
                             item.ContainerLotId is null && item.CarrierId is null &&
                             item.StorageBuildingId is null && item.GroundPosition is null).ToArray())
                    ApplyInventoryTransition(inventory => InventoryFixture.Relocate(inventory,
                        $"death:{id}:{lot.Id}", lot.Id, estate.Id, lot.Quantity,
                        groundPosition: new InventoryGroundPosition(position.X, position.Y)));
            }
            foreach (var moment in (inhabitants[id].IdentityMoments ?? [])
                         .Where(item => item.Outcome is "waiting" or "requested").ToArray())
                FinishIdentityMoment(id, moment.Kind, "interrupted");
            deceasedInhabitants.Add(id, new PlaytestDeceasedInhabitantState(
                id, deathTick, society.Checkpoint.AgeAt(deceased, deathTick),
                inhabitants[id] with
                {
                    MedicalTreatment = null,
                    GuardianPlacement = null,
                    Equipment = inhabitants[id].Equipment is { } equipment
                        ? equipment with { OrnamentLotId = null } : null,
                }, TownForResident(id)));
            inhabitants.Remove(id);
            RemoveTownResident(id);
            checkpointSchemaVersion = StateSchemaVersion;
            AppendEvent("inhabitant_removed", id);
        }
    }

}
