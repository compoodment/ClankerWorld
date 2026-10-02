using ClankerWorld.GodotClient.ClientState;
using ClankerWorld.GodotClient.Pairing;
using ClankerWorld.GodotClient.UI;
using Godot;
using System.Net;
using System.Text.Json;

namespace ClankerWorld.GodotClient;

public partial class Main
{
    private async Task VerifyNewcomerOfferAsync()
    {
        var previousRegistration = registration;
        var previousKey = deviceKey;
        var previousUrl = worldUrlInput.Text;
        var previousConfiguration = providerConfiguration;
        var previousRefreshing = isRefreshing;
        var previousCi = System.Environment.GetEnvironmentVariable("CI");
        System.Environment.SetEnvironmentVariable("CI", "true");
        using var signer = OwnerDeviceKey.CreateEphemeralForContinuousIntegration();
        using var portProbe = new System.Net.Sockets.TcpListener(IPAddress.Loopback, 0);
        portProbe.Start();
        var port = ((IPEndPoint)portProbe.LocalEndpoint).Port;
        portProbe.Stop();
        var origin = $"http://127.0.0.1:{port}/";
        using var listener = new HttpListener();
        listener.Prefixes.Add(origin);
        listener.Start();
        var snapshot = new OwnerWorldSnapshot("newcomer-smoke", 0, "newcomer-map",
            [new(0, 0, "meadow")], [], [], null, 0)
        {
            ContinuityRuleActive = true,
            FounderSetup = new(4, 4, true),
        };
        var handshake = new OwnerWorldHandshake(new(1, 1),
            ["owner-observation.read.v1", "inhabitant-inspection.read.v1", "spatial-knowledge.read.v1",
             "owner-control.request.v1", "paused-authoring.request.v1"], []);
        void Accept(OwnerWorldSnapshot current)
        {
            if (!observationSession.TryAccept(new(handshake, new(current, new(0, 0, []))), 0, out var failure))
                throw new InvalidOperationException("Newcomer fixture was rejected: " + failure);
            RenderEventLog();
        }
        try
        {
            var authority = new OwnerAuthorityIdentity("newcomer-smoke", snapshot.WorldId);
            registration = new(authority, "newcomer-device", signer.PublicKeyFingerprint, origin);
            deviceKey = signer;
            worldUrlInput.Text = origin;
            // Hold observation refreshes so this check observes only the requests
            // made by opening the existing Add Agent controls.
            isRefreshing = true;
            Accept(snapshot);
            if (!eventLog.GetParsedText().Contains(WorldEventText.ContinuityRisk, StringComparison.Ordinal) ||
                !eventLog.GetParsedText().Contains("Add a newcomer", StringComparison.Ordinal))
                throw new InvalidOperationException("An active rule must offer a newcomer even without retained transition events.");
            var requests = new List<string>();
            var responses = Task.Run(async () =>
            {
                for (var index = 0; index < 4; index++)
                {
                    var context = await listener.GetContextAsync().WaitAsync(TimeSpan.FromSeconds(5));
                    var path = context.Request.Url!.AbsolutePath;
                    requests.Add(path);
                    object response = path switch
                    {
                        OwnerPairingEndpoints.ChallengeIssue => new OwnerChallenge(authority, registration.DeviceId,
                            "newcomer-challenge-" + index, "newcomer-nonce-" + index, DateTimeOffset.UtcNow.AddMinutes(1)),
                        OwnerPairingEndpoints.OwnerProviderStatus => new OwnerProviderConfigurationStatus(
                            "openai", "openai", 0, [new("openai", "gpt-5-mini", false)]),
                        OwnerPairingEndpoints.OwnerProviderModels => new OwnerProviderModelList(
                            "openai", [new("gpt-5-mini", true)], "gpt-5-mini", null),
                        _ => throw new InvalidOperationException("Opening a newcomer offer must only read provider setup and model choices: " + path),
                    };
                    context.Response.ContentType = "application/json";
                    await JsonSerializer.SerializeAsync(context.Response.OutputStream, response, response.GetType(),
                        CompatibilitySmokeJsonOptions);
                    context.Response.Close();
                }
            });
            eventLog.EmitSignal(RichTextLabel.SignalName.MetaClicked, "add-newcomer");
            await responses;
            for (var frame = 0; frame < 10 && isOwnerAction; frame++)
                await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
            if (!founderSetupPanel.Visible || !placingAddedAgent || requests.Count != 4 ||
                requests.Count(path => path == OwnerPairingEndpoints.OwnerProviderStatus) != 1 ||
                requests.Count(path => path == OwnerPairingEndpoints.OwnerProviderModels) != 1)
                throw new InvalidOperationException("The newcomer link must open Add Agent using only setup reads, without placement or a paid call.");
            founderModelPicker.SetModel("player-choice");
            await HandleEventLogActionAsync("add-newcomer");
            if (!founderSetupPanel.Visible || founderModelPicker.Model != "player-choice")
                throw new InvalidOperationException("Opening the offer again must preserve the player's open Add Agent choices.");
            Accept(snapshot with { ContinuityRuleActive = false });
            if (eventLog.GetParsedText().Contains("Add a newcomer", StringComparison.Ordinal) ||
                eventLog.GetParsedText().Contains(WorldEventText.ContinuityRisk, StringComparison.Ordinal))
                throw new InvalidOperationException("The offer must disappear when the rule turns off, even without an event-list change.");
            founderSetupPanel.Hide();
            placingAddedAgent = false;
            await HandleEventLogActionAsync("add-newcomer");
            if (founderSetupPanel.Visible)
                throw new InvalidOperationException("An old newcomer link must stay inactive after the rule turns off.");
        }
        finally
        {
            founderApiKeyInput.Text = string.Empty;
            founderSetupPanel.Hide();
            placingAddedAgent = false;
            registration = previousRegistration;
            deviceKey = previousKey;
            worldUrlInput.Text = previousUrl;
            providerConfiguration = previousConfiguration;
            isRefreshing = previousRefreshing;
            System.Environment.SetEnvironmentVariable("CI", previousCi);
            listener.Stop();
            observationSession.ResetAfterLoad();
            renderedEventLog = string.Empty;
            RenderEventLog();
        }
    }
}
