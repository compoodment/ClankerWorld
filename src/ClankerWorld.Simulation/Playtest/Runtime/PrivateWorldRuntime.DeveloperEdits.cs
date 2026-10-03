using System.Text.Json;
using ClankerWorld.Simulation.Kernel;
using ClankerWorld.Simulation.Society;

namespace ClankerWorld.Simulation.Playtest;

public sealed partial class PrivateWorldRuntime
{
    public static IReadOnlyList<string> DeveloperGoods { get; } = Array.AsReadOnly(new[]
    {
        "berries", "fruit", "wild_greens", "cultivated_greens", "grain", "flour", "wood", "stone",
        "fiber", "rope", "cloth", "clothing", "padded_coat", "rain_cloak", "basket", "sack",
        "wooden_axe", "wooden_pickaxe", "iron_ore", "iron", "storage_pot", "water_jug", "medicine", "tree_seed",
    });

    public PrivateWorldDeveloperEditResult ApplyDeveloperEdit(PrivateWorldDeveloperEdit edit,
        Action<PrivateWorldRuntimeState>? persist = null)
    {
        ArgumentNullException.ThrowIfNull(edit);
        gate.Wait();
        try
        {
            if (edit.WorldId != society.Checkpoint.WorldId)
                return new(false, false, "The active world changed. Refresh before editing.");
            if (!society.Checkpoint.IsPaused)
                return new(false, false, "Pause the world before making a developer edit.");
            var detail = JsonSerializer.Serialize(edit);
            // A lost response can be retried without applying a second grant.
            // Once this event leaves hot history the old expected ID still
            // refuses the retry, rather than allowing it to run again.
            if (events.Any(item => item.Kind == "developer_edit" && item.Detail == detail))
                return new(false, true, null);
            if (edit.ExpectedEventId != nextEventId - 1)
                return new(false, false, "The world changed. Refresh before editing.");
            using var proposed = RestoreCore(CaptureState(), providerFactory,
                maxCognitionDispatchPerCycle, trustedPreparedState: true);
            try
            {
                proposed.ApplyDeveloperEditCore(edit);
                proposed.AppendEvent("developer_edit", detail);
                proposed.Validate();
                ValidateStateForCodec(proposed.CaptureState());
            }
            catch (Exception exception) when (exception is ArgumentException or InvalidOperationException or InvalidDataException)
            {
                return new(false, false, exception.Message);
            }
            // Persist the validated proposal before exposing it. An I/O failure
            // leaves live state and the expected-event precondition unchanged.
            persist?.Invoke(proposed.CaptureState());
            CommitPreparedTick(proposed);
            plannedRoutes = [];
            return new(true, false, null);
        }
        finally { gate.Release(); }
    }

    private void ApplyDeveloperEditCore(PrivateWorldDeveloperEdit edit)
    {
        if (new[] { edit.AgentId, edit.Operation, edit.Value, edit.OtherAgentId ?? "" }
            .Any(value => value is null || value.Length > 512 || value.Any(char.IsControl)))
            throw new ArgumentException("Developer edit values must be short and contain no control characters.");
        if (!inhabitants.TryGetValue(edit.AgentId, out var person) ||
            society.Checkpoint.GetInhabitant(edit.AgentId).Status != SocietyInhabitantStatus.Active)
            throw new InvalidOperationException("Select a living agent.");
        var actor = edit.AgentId;
        var id = $"developer-edit-{edit.ExpectedEventId + 1}";
        switch (edit.Operation)
        {
            case "set_need":
                if (edit.Amount is < 0 or > 100)
                    throw new ArgumentOutOfRangeException(nameof(edit), "Needs must be between 0 and 100 percent.");
                var survival = person.Survival ?? new SurvivalCondition();
                if (edit.Value != "fullness") survivalState ??= new SettlementSurvivalState(WorldTick, []);
                inhabitants[actor] = edit.Value switch
                {
                    "fullness" => person with { HungerBasisPoints = edit.Amount * 100 },
                    "warmth" => person with { Survival = survival with { WarmthBasisPoints = edit.Amount * 100 } },
                    "illness" => person with { Survival = survival with { IllnessBasisPoints = edit.Amount * 100 } },
                    "nutrition" => person with { Survival = survival with { NutritionBasisPoints = edit.Amount * 100 } },
                    _ => throw new ArgumentException("Choose fullness, warmth, illness or nutrition."),
                };
                break;
            case "give_goods":
                if (!DeveloperGoods.Contains(edit.Value, StringComparer.Ordinal) || edit.Amount is < 1 or > 100)
                    throw new ArgumentException("Choose a supported good and a quantity from 1 to 100.");
                if (edit.Amount > FreeCarryCapacity(actor))
                    throw new InvalidOperationException("The agent cannot carry that many goods.");
                var individualVessels = InventoryContainerRules.IsContainer(edit.Value);
                var lotsToAdd = individualVessels ? edit.Amount : 1;
                for (var index = 0; index < lotsToAdd; index++)
                {
                    var lotId = lotsToAdd == 1 ? id : id + ":" + index;
                    ApplyInventoryTransition(inventory => InventoryFixture.AddLot(inventory, lotId, edit.Value, actor,
                        individualVessels ? 1 : edit.Amount));
                }
                break;
            case "remove_goods":
                if (edit.Amount is < 1 or > 100)
                    throw new ArgumentException("Choose a quantity from 1 to 100.");
                var removable = society.Checkpoint.Inventory.Lots.Where(lot => lot.OwnerId == actor &&
                        lot.ItemKind == edit.Value && PersonalEquipmentRules.IsCarried(lot, actor) &&
                        lot.DeliveryBuildingId is null && lot.ContainerLotId is null &&
                        !PersonalEquipmentRules.IsSelected(person.Equipment, lot.Id) &&
                        !knowledge.Artifacts.Any(artifact => artifact.LotId == lot.Id) &&
                        !society.Checkpoint.Inventory.Lots.Any(content => content.ContainerLotId == lot.Id))
                    .OrderBy(lot => lot.Id, StringComparer.Ordinal).ToArray();
                if (removable.Sum(PhysicalUnreservedQuantity) < edit.Amount)
                    throw new InvalidOperationException("Not enough unreserved personal goods are carried. Equipped items, filled vessels and knowledge records are protected.");
                var remaining = edit.Amount;
                foreach (var lot in removable)
                {
                    var amount = Math.Min(remaining, PhysicalUnreservedQuantity(lot));
                    if (amount == 0) continue;
                    ApplyInventoryTransition(inventory => InventoryFixture.Discard(inventory, actor, lot.Id, amount));
                    remaining -= amount;
                    if (remaining == 0) break;
                }
                break;
            case "add_skill":
            case "remove_skill":
                if (!Enum.TryParse<SettlementSkillKind>(edit.Value, true, out var skill) || !Enum.IsDefined(skill))
                    throw new ArgumentException("Choose building, farming, crafting or smithing.");
                var hasSkill = person.Skills?.Any(item => item.Kind == skill) == true;
                if (hasSkill == (edit.Operation == "add_skill"))
                    throw new InvalidOperationException(hasSkill ? "The agent already has that skill." : "The agent does not have that skill.");
                var skills = (person.Skills ?? []).Where(item => item.Kind != skill).ToList();
                if (edit.Operation == "add_skill")
                    skills.Add(person.Skills?.FirstOrDefault(item => item.Kind == skill) ?? new SettlementSkill(skill, WorldTick));
                inhabitants[actor] = person with { Skills = skills.OrderBy(item => item.Kind).ToArray() };
                break;
            case "start_partnership":
            case "end_partnership":
                if (edit.Value != "partnership") throw new ArgumentException("Choose partnership as the relationship type.");
                var other = edit.OtherAgentId;
                if (other is null || other == actor || !AdultResident(actor) || !AdultResident(other))
                    throw new InvalidOperationException("Choose two different living adults.");
                if (edit.Operation == "start_partnership")
                {
                    if (!AvailablePartner(actor) || !AvailablePartner(other) || CloseKin(actor, other))
                        throw new InvalidOperationException("Both adults must be unpartnered and not close relatives.");
                    society.Apply(checkpoint => SocietyFixture.ProposeRelationship(checkpoint,
                        new(id, 1, SocietyRelationshipType.Partnership, actor, other, WorldTick, PrivacyClass: "public")));
                    society.Apply(checkpoint => SocietyFixture.AcceptRelationship(checkpoint, id, 1, other));
                    if (society.Checkpoint.GetRelationship(id).State != SocietyRelationshipState.Accepted)
                        throw new InvalidOperationException("The partnership could not be started.");
                }
                else
                {
                    var partnership = Partnerships(actor).FirstOrDefault(item =>
                        item.State is SocietyRelationshipState.Accepted or SocietyRelationshipState.Proposed &&
                        (item.ProposerId == other || item.TargetId == other));
                    if (partnership is null) throw new InvalidOperationException("These agents have no current partnership.");
                    society.Apply(checkpoint => SocietyFixture.RevokeRelationship(checkpoint, partnership.Id, actor));
                }
                break;
            default:
                throw new ArgumentException("Choose a supported developer edit.");
        }
        inhabitants[actor] = inhabitants[actor] with { LastDecisionContext = null };
        checkpointSchemaVersion = StateSchemaVersion;
    }
}
