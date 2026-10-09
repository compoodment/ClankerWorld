using ClankerWorld.GodotClient.UI;
using Godot;

namespace ClankerWorld.GodotClient;

public partial class Main
{
    private async Task VerifyGlidingMarkersAsync()
    {
        var previous = renderedMapSnapshot!;
        var previousObservation = observationSession.Current;
        var previousSelection = selectedInhabitantId;
        var previousProcessing = IsProcessing();
        var visiblePanels = HudPanels().ToDictionary(panel => panel, panel => panel.Visible);
        SetProcess(false);
        foreach (var panel in visiblePanels.Keys) panel.Hide();
        var person = PanelSmokeAgent("glide-agent", "Rowan", new(120, 60));
        var cow = new OwnerWorldAnimal("glide-cow", "Moss", "cow", "female", 9, "adult", new(120, 62),
            null, null, "wild", null, 0, null, null, null, null, false, [], []);
        var cart = new OwnerWorldHandcart("glide-cart", person.Id, person.DisplayName, new(120, 64), 32, 100,
            null, null, []);
        var boat = new OwnerWorldBoat("glide-boat", "town", "Cedar", new(120, 66), null, null, null, null, "underway", null, []);
        var map = new OwnerWorldSnapshot("gliding-markers-smoke", 1, "gliding-markers-map", [], [], [], null, 0)
        {
            PackedTerrain = new(256, 128, "terrain-kind-v1", Convert.ToBase64String(new byte[256 * 128])),
            Inhabitants = [person],
            Animals = [cow],
            Handcarts = [cart],
            Boats = [boat],
            Authoring = new(false, 0, 0, 0, "gliding-markers-map", "gliding-markers-map", "clear", "spring", []),
        };
        var handshake = new OwnerWorldHandshake(new(1, 1),
            ["owner-observation.read.v1", "inhabitant-inspection.read.v1", "spatial-knowledge.read.v1",
             "owner-control.request.v1", "paused-authoring.request.v1"], []);
        void Show()
        {
            observationSession.ResetAfterLoad();
            if (!observationSession.TryAccept(new(handshake, new(map, new(map.WorldTick, 0, []))), 0, out var failure))
                throw new InvalidOperationException("Glide observation fixture was refused: " + failure);
            Render(map, []);
        }
        void Move(int x)
        {
            person = person with { Position = new(x, 60) };
            cow = cow with { Position = new(x, 62) };
            cart = cart with { Position = new(x, 64) };
            boat = boat with { Position = new(x, 66) };
            map = map with { WorldTick = map.WorldTick + 1, Inhabitants = [person], Animals = [cow], Handcarts = [cart], Boats = [boat] };
            Show();
        }
        try
        {
            Show();
            var agent = inhabitantVisuals[person.Id];
            Control[] markers = [agent, mapObjectVisuals["animal:" + cow.Id], mapObjectVisuals["handcart:" + cart.Id], mapObjectVisuals["boat:" + boat.Id]];
            var origins = markers.Select(marker => marker.Position).ToArray();
            var stride = currentTileSize + TileGap;
            Move(121);
            if (markers.Where((marker, index) => marker.Position != origins[index]).Any())
                throw new InvalidOperationException("A short authoritative move must start at its current drawing, without jumping ahead.");
            AdvanceMapMarkers(0.25);
            for (var index = 0; index < markers.Length; index++)
                if (Math.Abs(markers[index].Position.X - origins[index].X - stride * 0.25f) > 0.01f ||
                    Math.Abs(markers[index].Position.Y - origins[index].Y - (index < 2 ? -1 : 0)) > 0.01f)
                    throw new InvalidOperationException("All four moving markers must glide a quarter step; only agents/animals have the approved one-pixel bob.");
            var animalSprite = markers[1].GetNode<AnimalMapSprite>("AnimalSprite");
            if (agent.Frame != AgentFrame.Walk2 || animalSprite.Step != 2)
                throw new InvalidOperationException("Native walking sprites must change after one quarter second.");
            var paused = markers.Select(marker => marker.Position).ToArray();
            map = map with { Authoring = map.Authoring! with { IsPaused = true } };
            Show(); AdvanceMapMarkers(2);
            if (!paused.SequenceEqual(markers.Select(marker => marker.Position)) || agent.Frame != AgentFrame.Walk2 || animalSprite.Step != 2)
                throw new InvalidOperationException("Pause must freeze the drawing and walk phase exactly.");
            map = map with { Authoring = map.Authoring! with { IsPaused = false } };
            Show(); AdvanceMapMarkers(0.25);
            if (agent.Frame != AgentFrame.Walk1 || animalSprite.Step != 1 ||
                Math.Abs(agent.Position.X - origins[0].X - stride * 0.5f) > 0.01f || agent.Position.Y != origins[0].Y)
                throw new InvalidOperationException("Resume must continue the same half-step and quarter-second pose without jumping.");
            var midway = agent.Position;
            Move(122);
            if (agent.Position != midway)
                throw new InvalidOperationException("A newer report must retarget from the current drawing, without an instant correction.");
            AdvanceMapMarkers(0.5);
            if (Math.Abs(agent.Position.X - origins[0].X - stride * 1.25f) > 0.01f)
                throw new InvalidOperationException("A retargeted glide must interpolate from its actual intermediate position.");
            var hovered = false;
            agent.MouseEntered += () => hovered = true;
            var point = agent.GetGlobalRect().GetCenter();
            GetViewport().PushInput(new InputEventMouseMotion { Position = point, GlobalPosition = point }, true);
            await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
            if (!hovered)
                throw new InvalidOperationException("The actual intermediate drawing must remain hoverable.");
            GetViewport().PushInput(new InputEventMouseButton
            {
                Position = point,
                GlobalPosition = point,
                ButtonIndex = MouseButton.Left,
                Pressed = true
            }, true);
            GetViewport().PushInput(new InputEventMouseButton
            {
                Position = point,
                GlobalPosition = point,
                ButtonIndex = MouseButton.Left,
                Pressed = false
            }, true);
            await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
            if (selectedInhabitantId != person.Id || !agent.Selected || !agent.NameShown || !selectedInhabitantCard.Visible)
                throw new InvalidOperationException("Clicking a gliding figure must select it, show its name and open its card.");
            var cardBefore = selectedInhabitantCard.Position;
            AdvanceMapMarkers(0.25);
            if (selectedInhabitantCard.Position == cardBefore)
                throw new InvalidOperationException("The selected agent's quick card must follow the drawn position.");
            AdvanceMapMarkers(1);
            if (agent.Frame != AgentFrame.Still || animalSprite.Step != 0 ||
                Math.Abs(agent.Position.X - origins[0].X - stride * 2) > 0.01f || agent.Position.Y != origins[0].Y)
                throw new InvalidOperationException("Arrival must finish at the authoritative tile, without predicting another step or retaining a bob.");
            Move(126);
            if (agent.Frame != AgentFrame.Still || animalSprite.Step != 0 ||
                Math.Abs(agent.Position.X - origins[0].X - stride * 6) > 0.01f)
                throw new InvalidOperationException("A jump longer than three tiles must snap and clear walking.");

            map = map with { WorldId = "gliding-wrap-smoke", WrapsEastWest = true };
            Move(255);
            CenterCameraAt(new(0.5f, 61));
            var seamStart = agent.Position;
            Move(0); AdvanceMapMarkers(0.5);
            if (Math.Abs(agent.Position.X - seamStart.X - stride * 0.5f) > 0.01f || agent.Facing != AgentSprites.FacingToward(1, 0))
                throw new InvalidOperationException("A wrapped move must glide one tile across the seam, never across the whole world.");
            var beforePan = agent.Position;
            CenterCameraAt(new(256.5f, 61));
            if (Math.Abs(agent.Position.X - beforePan.X) > 0.01f)
                throw new InvalidOperationException("Equivalent camera copies must preserve the intermediate wrapped drawing.");
            var normalized = inhabitantCanonicalXs[person.Id] / stride;
            cameraZoom = Math.Max(minimumCameraZoom, cameraZoom / 2);
            RenderMap(map);
            if (Math.Abs(inhabitantCanonicalXs[person.Id] / (currentTileSize + TileGap) - normalized) > 0.6f)
                throw new InvalidOperationException("Zoom must retain the in-progress tile, rather than jumping to its reported destination.");
            map = map with { WorldTick = 0 };
            Show();
            if (agent.Frame != AgentFrame.Still || animalSprite.Step != 0)
                throw new InvalidOperationException("A checkpoint rewind must clear local motion.");
            map = map with { Authoring = map.Authoring! with { IsPaused = true } };
            Show();
            Move(1);
            var relocated = agent.Position;
            AdvanceMapMarkers(1);
            if (agent.Frame != AgentFrame.Still || animalSprite.Step != 0 || agent.Position != relocated)
                throw new InvalidOperationException("A paused authoring relocation must display its reported tile at once, without starting a frozen glide.");
            map = map with { Inhabitants = [], Animals = [], Handcarts = [], Boats = [] };
            Show();
            if (markerMotions.Count != 0)
                throw new InvalidOperationException("Removed markers must release their local motion state.");
        }
        finally
        {
            observationSession.ResetAfterLoad();
            if (previousObservation is not null) observationSession.TryAccept(previousObservation, 0, out _);
            selectedInhabitantId = previousSelection;
            Render(previous, []);
            foreach (var (panel, visible) in visiblePanels) panel.Visible = visible;
            SetProcess(previousProcessing);
        }
    }
}
