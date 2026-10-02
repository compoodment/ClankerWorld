using System.Text.Json;
using ClankerWorld.GodotClient.UI;
using Godot;

namespace ClankerWorld.GodotClient;

public partial class Main
{
    private void VerifyToolMakingPresentation()
    {
        const string customerId = "world:inhabitant:birth:founder:customer:agent:parent:1";
        const string smithId = "world:inhabitant:birth:founder:smith:agent:parent:2";
        const string requestId = "tool-making-request:ui-proof";
        const string buildingId = "blacksmith:household:river:1";
        const string recipeId = "clankerworld-tools-v1/stone-pickaxe";
        const string missing = "The Blacksmith needs stone and wood at its workplace.";
        const string full = "The Blacksmith has no room for the finished tool.";
        var position = new OwnerWorldPosition(0, 0);
        OwnerWorldInhabitant Person(string id, string name) => new(id, name, "active", position, 8_000, [], [],
            new("idle", null, null, [], ""), new(position, [], []), false);
        var request = new OwnerWorldToolMakingRequest(requestId, "Mira", recipeId,
            "Stone pickaxe", "stone_pickaxe", "accepted", missing);
        var shop = new OwnerWorldPlacedBuilding(buildingId, "tool-ui/blacksmith", position, 0,
            "River Blacksmith", ["blacksmith"], HouseholdId: "household:river")
        {
            ToolMakingRequests = [request],
        };
        var fixture = new OwnerWorldSnapshot("tool-making-ui-smoke", 1, "tool-making-ui-map",
            [new(0, 0, "meadow")], [], [], null, 3)
        {
            Inhabitants = [Person(customerId, "Mira") with
                { ToolMakingRequestNote = "River Blacksmith accepted your stone pickaxe request. " + missing },
                Person(smithId, "Orin")],
            PlacedBuildings = [shop],
        };
        var options = new JsonSerializerOptions(JsonSerializerDefaults.Web);
        var snapshot = JsonSerializer.Deserialize<OwnerWorldSnapshot>(JsonSerializer.Serialize(fixture, options), options)
            ?? throw new InvalidOperationException("The tool-making observation must parse through the owner wire.");
        var parsed = snapshot.PlacedBuildings.Single().ToolMakingRequests.Single();
        if (parsed.RequesterName != "Mira" || parsed.RecipeId != recipeId || parsed.Status != "accepted" ||
            parsed.Blocker != missing || snapshot.Inhabitants.Single(person => person.Id == customerId).ToolMakingRequestNote is null)
            throw new InvalidOperationException("The owner wire must retain the actual request, requester, recipe, status and blocker.");
        var oldBuildingSelection = selectedBuildingId;
        var oldBuildingDetails = buildingDetailsRequested;
        var oldBuildingSnapshot = buildingCardSnapshot;
        var oldAgentSelection = selectedInhabitantId;
        var oldProfileRequested = agentProfileRequested;
        var oldAgentSnapshot = agentCardSnapshot;
        try
        {
            selectedBuildingId = buildingId;
            buildingDetailsRequested = true;
            RenderBuildingCard(snapshot);
            var facts = string.Join('\n', buildingFacts.GetChildren().OfType<Label>().Select(label => label.Text));
            if (buildingDetailsHeader.NameLabel.Text != "River Blacksmith" ||
                !facts.Contains("Mira · Stone pickaxe", StringComparison.Ordinal) ||
                !facts.Contains("Household accepted", StringComparison.Ordinal) || !facts.Contains(missing, StringComparison.Ordinal) ||
                facts.Contains(requestId, StringComparison.Ordinal) || facts.Contains(recipeId, StringComparison.Ordinal))
                throw new InvalidOperationException("The Blacksmith card must show its named source, requester, recipe and missing-input reason without raw identities.");
            selectedInhabitantId = customerId;
            agentProfileRequested = true;
            RenderSelectedInhabitantCard(snapshot);
            if (!inhabitantDetails.GetParsedText().Contains("River Blacksmith accepted your stone pickaxe request.", StringComparison.Ordinal))
                throw new InvalidOperationException("The agent card must show its own request note from the wire.");

            RenderBuildingCard(snapshot with { PlacedBuildings = [shop with
                { ToolMakingRequests = [request with { Blocker = full }] }] });
            facts = string.Join('\n', buildingFacts.GetChildren().OfType<Label>().Select(label => label.Text));
            if (!facts.Contains(full, StringComparison.Ordinal) || facts.Contains(missing, StringComparison.Ordinal))
                throw new InvalidOperationException("Refreshing a tool request must replace its missing-input reason with the actual full-storage reason.");
            RenderBuildingCard(snapshot with { PlacedBuildings = [shop with
                { ToolMakingRequests = [request with { Status = "ready", Blocker = null }] }] });
            facts = string.Join('\n', buildingFacts.GetChildren().OfType<Label>().Select(label => label.Text));
            if (!facts.Contains("Tool ready · payment still to be agreed", StringComparison.Ordinal) || facts.Contains(full, StringComparison.Ordinal))
                throw new InvalidOperationException("A finished tool must clear the blocker while preserving that payment still needs agreement.");

            OwnerWorldEvent[] events = [
                new(1, 1, "tool_request_placed", customerId + ":" + requestId),
                new(2, 1, "tool_request_accepted", smithId + ":" + requestId),
                new(3, 1, "tool_request_ready", smithId + ":" + requestId),
                new(4, 1, "tool_request_completed", customerId + ":" + requestId),
            ];
            var handshake = new OwnerWorldHandshake(new(1, 1),
                ["owner-observation.read.v1", "inhabitant-inspection.read.v1", "spatial-knowledge.read.v1",
                 "owner-control.request.v1", "paused-authoring.request.v1"], []);
            if (!observationSession.TryAccept(new(handshake, new(snapshot, new(1, 0, events))), 0, out var failure))
                throw new InvalidOperationException("The tool-making Event Log observation was rejected: " + failure);
            foreach (var worldEvent in events) knownEvents[worldEvent.EventId] = worldEvent;
            RenderEventLog();
            var log = eventLog.GetParsedText();
            if (!log.Contains("Mira asked a Blacksmith household", StringComparison.Ordinal) ||
                !log.Contains("Orin agreed to make the requested tool", StringComparison.Ordinal) ||
                !log.Contains("price still needs an agreed exchange", StringComparison.Ordinal) ||
                !log.Contains("fulfilled through the completed shop exchange", StringComparison.Ordinal) ||
                log.Contains("world:inhabitant", StringComparison.Ordinal) || log.Contains(requestId, StringComparison.Ordinal))
                throw new InvalidOperationException("Tool-request history must reach the player log with complete agent names and separate production and purchase outcomes.");
        }
        finally
        {
            observationSession.ResetAfterLoad();
            knownEvents.Clear();
            UpdateUnreadEvents(null);
            renderedEventLog = string.Empty;
            RenderEventLog();
            ClearBuildingSelection();
            selectedBuildingId = oldBuildingSelection;
            buildingDetailsRequested = oldBuildingDetails;
            buildingCardSnapshot = oldBuildingSnapshot;
            if (oldBuildingSnapshot is not null && oldBuildingSelection is not null) RenderBuildingCard(oldBuildingSnapshot);
            RenderSelectedInhabitantCard(fixture with { Inhabitants = [] });
            selectedInhabitantId = oldAgentSelection;
            agentProfileRequested = oldProfileRequested;
            agentCardSnapshot = oldAgentSnapshot;
            if (oldAgentSnapshot is not null) RenderSelectedInhabitantCard(oldAgentSnapshot);
        }
    }
}
