using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;

namespace ClankerWorld.Simulation.Cognition;

/// <summary>Adapts the Decisions API's named, typed answers to authoritative cognition admission.</summary>
public sealed class OpenAiDecisionsProvider : IDecisionProvider
{
    private const string ChoiceName = "selected_candidate";
    private readonly HttpClient httpClient;
    private readonly Func<string?> apiKeyAccessor;
    private readonly Uri endpoint;
    private readonly string model;
    private readonly TimeSpan requestTimeout;
    private readonly ModelNeedFormat needFormat;

    public OpenAiDecisionsProvider(HttpClient httpClient, Func<string?> apiKeyAccessor,
        Uri? endpoint = null, string model = "gpt-6-luna", TimeSpan? requestTimeout = null,
        long providerEpoch = 1, ModelNeedFormat needFormat = ModelNeedWords.DefaultFormat)
    {
        this.httpClient = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
        this.apiKeyAccessor = apiKeyAccessor ?? throw new ArgumentNullException(nameof(apiKeyAccessor));
        this.endpoint = endpoint ?? new Uri("https://api.openai.com/v1/decisions");
        if (!this.endpoint.IsAbsoluteUri || this.endpoint.Scheme != Uri.UriSchemeHttps)
            throw new ArgumentException("The Decisions endpoint must be an absolute HTTPS URI.", nameof(endpoint));
        ArgumentException.ThrowIfNullOrWhiteSpace(model);
        this.model = model.Trim();
        this.requestTimeout = requestTimeout ?? TimeSpan.FromSeconds(30);
        if (this.requestTimeout <= TimeSpan.Zero || this.requestTimeout > TimeSpan.FromMinutes(5))
            throw new ArgumentOutOfRangeException(nameof(requestTimeout));
        ArgumentOutOfRangeException.ThrowIfNegative(providerEpoch);
        ProviderEpoch = providerEpoch;
        this.needFormat = Enum.IsDefined(needFormat) ? needFormat : throw new ArgumentOutOfRangeException(nameof(needFormat));
    }

    public DecisionProviderKind Kind => DecisionProviderKind.OpenAiDecisions;
    public long ProviderEpoch { get; }

    public async ValueTask<CognitionDecisionResponse> DecideAsync(CognitionDecisionRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        request.Validate();
        cancellationToken.ThrowIfCancellationRequested();
        var key = apiKeyAccessor()?.Trim();
        if (string.IsNullOrWhiteSpace(key))
            throw new CognitionProviderUnavailableException("missing_key", "Add an OpenAI key to use OpenAI Decisions.");

        List<object> questions =
        [
            new
            {
                type = "choice", name = ChoiceName,
                instructions = "Choose exactly one legal candidate for the agent's next small action. " +
                    (needFormat == ModelNeedFormat.Words
                        ? "fullness, warmth and illness give the current level in words and the scale from worst to best."
                        : "hunger_basis_points is 0 starving to 10000 full; warmth_basis_points is 0 dangerously cold to 10000 warm; illness_basis_points is 0 well to 10000 severely ill.") +
                    " A null need is unknown.",
                choices = request.Observation.Candidates.Select(candidate => new { value = candidate.Id, description = candidate.Description }).ToArray(),
            },
        ];
        for (var index = 0; index < (request.Observation.MemoryCompactionCandidates?.Count ?? 0); index++)
            questions.Add(new
            {
                type = "score",
                name = MemoryName(index),
                instructions = $"Rate only memory_compaction_candidates source_index {index} for usefulness in this agent's future decisions and identity. " +
                    "Preserve lasting relationships, learned skills, major personal events and unresolved commitments. " +
                    "A belief is uncertain; consider provenance and confidence without treating it as verified world fact.",
                levels = new[]
                {
                    new { label = "Minor", description = "Redundant or unlikely to help the agent again." },
                    new { label = "Useful", description = "Possibly useful context for future choices." },
                    new { label = "Important", description = "Important long-term context to keep easy to retrieve." },
                },
            });
        if (request.Observation.MemorySummaryOptions is { Count: > 0 } summaryOptions)
            questions.Add(new
            {
                type = "choice",
                name = "memory_summary",
                instructions = "Choose a shorter extract of this agent's own old experiences, preserving source attribution and uncertainty, or keep_records.",
                choices = summaryOptions.Select(option => new { value = option.Choice, description = option.Text })
                    .Append(new { value = "keep_records", description = "Keep the original records without a new summary." }).ToArray(),
            });
        var payload = new
        {
            model,
            input = JsonSerializer.Serialize(JevDecisionProvider.BuildState(request.Observation, needFormat)),
            questions,
        };
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(requestTimeout);
        using var message = new HttpRequestMessage(HttpMethod.Post, endpoint)
        {
            Content = new StringContent(JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json"),
        };
        message.Headers.Authorization = new AuthenticationHeaderValue("Bearer", key);
        using var response = await httpClient.SendAsync(message, HttpCompletionOption.ResponseHeadersRead, timeout.Token).ConfigureAwait(false);
        if (!response.IsSuccessStatusCode)
            throw new HttpRequestException($"OpenAI Decisions returned HTTP {(int)response.StatusCode} ({response.StatusCode}).", null, response.StatusCode);
        var body = await ProviderResponseBody.ReadAsync(response.Content, timeout.Token).ConfigureAwait(false);
        return ParseResponse(request, body);
    }

    private static CognitionDecisionResponse ParseResponse(CognitionDecisionRequest request, string body)
    {
        try
        {
            using var document = JsonDocument.Parse(body);
            var root = document.RootElement;
            var answers = new Dictionary<string, JsonElement>(StringComparer.Ordinal);
            var candidates = request.Observation.MemoryCompactionCandidates ?? [];
            var allowed = Enumerable.Range(0, candidates.Count).Select(MemoryName).Append(ChoiceName).ToHashSet(StringComparer.Ordinal);
            if (request.Observation.MemorySummaryOptions is { Count: > 0 }) allowed.Add("memory_summary");
            foreach (var answer in root.GetProperty("answers").EnumerateArray())
            {
                var name = answer.GetProperty("name").GetString();
                if (name is null || !allowed.Contains(name) || !answers.TryAdd(name, answer))
                    throw new InvalidDataException("Decisions returned an unknown or duplicate answer name.");
            }
            var choice = answers[ChoiceName];
            if (choice.GetProperty("type").GetString() != "choice")
                throw new InvalidDataException("Decisions did not return a usable candidate choice.");
            var selected = choice.GetProperty("choice").GetString();
            ArgumentException.ThrowIfNullOrWhiteSpace(selected);
            var probabilities = choice.GetProperty("probabilities").EnumerateArray().ToDictionary(
                item => item.GetProperty("value").GetString()!, item => item.GetProperty("probability").GetDouble(), StringComparer.Ordinal);
            List<CognitionMemoryCompactionScore> scores = [];
            for (var index = 0; index < candidates.Count; index++)
            {
                if (!answers.TryGetValue(MemoryName(index), out var answer) || answer.GetProperty("type").GetString() == "refusal") continue;
                if (answer.GetProperty("type").GetString() != "score")
                    throw new InvalidDataException("Decisions returned the wrong memory answer type.");
                var score = answer.GetProperty("score").GetDouble();
                var confidence = answer.GetProperty("confidence").GetDouble();
                if (!double.IsFinite(score) || score is < 0 or > 2 || !double.IsFinite(confidence) || confidence is < 0 or > 1)
                    throw new InvalidDataException("Decisions returned an invalid memory score.");
                var candidate = candidates[index];
                scores.Add(new(candidate.Id, candidate.OwnerId, candidate.Kind, candidate.SourceTick,
                    (int)Math.Round(score * 5_000, MidpointRounding.AwayFromZero),
                    (int)Math.Round(confidence * 10_000, MidpointRounding.AwayFromZero)));
            }
            string? summaryChoice = null;
            if (answers.TryGetValue("memory_summary", out var summaryAnswer) && summaryAnswer.GetProperty("type").GetString() != "refusal")
            {
                if (summaryAnswer.GetProperty("type").GetString() != "choice") throw new InvalidDataException("Invalid private summary answer type.");
                summaryChoice = summaryAnswer.GetProperty("choice").GetString();
                if (summaryChoice == "keep_records") summaryChoice = null;
                else if (!(request.Observation.MemorySummaryOptions ?? []).Any(option => option.Choice == summaryChoice))
                    throw new InvalidDataException("Unknown private summary choice.");
            }
            var modelId = root.TryGetProperty("model", out var reportedModel) ? reportedModel.GetString() : null;
            var usage = root.TryGetProperty("usage", out var reportedUsage)
                ? new CognitionUsage(modelId, reportedUsage.GetProperty("input_tokens").GetInt32(), reportedUsage.GetProperty("output_tokens").GetInt32())
                : null;
            return new(request.RequestId, request.Observation.InhabitantId, DecisionProviderKind.OpenAiDecisions,
                request.ProviderEpoch, request.Observation.RunEpoch, request.Observation.DecisionGeneration,
                request.Observation.ObservationDigest, selected, choice.GetProperty("confidence").GetDouble(), probabilities,
                usage, MemoryCompactionScores: scores.Count == 0 ? null : scores, MemorySummaryChoice: summaryChoice);
        }
        catch (Exception exception) when (exception is JsonException or KeyNotFoundException or InvalidOperationException or ArgumentException or FormatException or OverflowException)
        {
            throw new InvalidDataException("Decisions returned a malformed or incomplete answer.", exception);
        }
    }

    private static string MemoryName(int index) => $"memory_salience_{index:D2}";
}
