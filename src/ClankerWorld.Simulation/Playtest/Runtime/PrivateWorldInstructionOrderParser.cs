using System.Globalization;
using ClankerWorld.Simulation.Harness;

namespace ClankerWorld.Simulation.Playtest;

/// <summary>
/// Parses only the direct food, material-gathering and personal-storage orders that the runtime can execute.
/// Every token must belong to one of these forms; unconsumed text is not guessed.
/// </summary>
internal static class PrivateWorldInstructionOrderParser
{
    internal static bool IsMaterialKind(string? kind) =>
        kind is "wood" or "stone" or "fiber" or "clay" or "iron_ore" or "gold_ore" or "diamond";

    internal static bool MatchesMaterial(MapResource resource, string kind) =>
        resource.Kind == kind || kind == "wood" && resource.Kind == "construction";

    private static readonly IReadOnlyDictionary<string, int> NumberWords =
        new Dictionary<string, int>(StringComparer.Ordinal)
        {
            ["one"] = 1,
            ["two"] = 2,
            ["three"] = 3,
            ["four"] = 4,
            ["five"] = 5,
            ["six"] = 6,
            ["seven"] = 7,
            ["eight"] = 8,
            ["nine"] = 9,
            ["ten"] = 10,
        };

    public static OwnerInstructionOrder? Parse(
        string text,
        IReadOnlyList<MapResource> resources,
        Func<MapResource, string> foodKnowledgeKind)
    {
        ArgumentNullException.ThrowIfNull(text);
        ArgumentNullException.ThrowIfNull(resources);
        ArgumentNullException.ThrowIfNull(foodKnowledgeKind);

        return TryTokenize(text, out var tokens)
            ? new OrderParser(tokens, resources, foodKnowledgeKind).Parse()
            : null;
    }

    private static bool TryTokenize(string text, out IReadOnlyList<Token> tokens)
    {
        var result = new List<Token>();
        for (var index = 0; index < text.Length;)
        {
            if (char.IsWhiteSpace(text[index]))
            {
                index++;
                continue;
            }

            var start = index;
            if (char.IsLetter(text[index]) || text[index] == '_')
            {
                index++;
                while (index < text.Length && (char.IsLetter(text[index]) || text[index] == '_'))
                    index++;

                while (index + 1 < text.Length && text[index] == '-' &&
                       (char.IsLetter(text[index + 1]) || text[index + 1] == '_'))
                {
                    index += 2;
                    while (index < text.Length && (char.IsLetter(text[index]) || text[index] == '_'))
                        index++;
                }

                result.Add(new Token(text[start..index].ToLowerInvariant(), start, index - start, TokenKind.Word));
                continue;
            }

            if (char.IsDigit(text[index]))
            {
                index++;
                while (index < text.Length && char.IsDigit(text[index]))
                    index++;
                result.Add(new Token(text[start..index], start, index - start, TokenKind.Number));
                continue;
            }

            var value = text[index].ToString();
            if (value is not ("," or "/" or "(" or ")" or "." or "!" or "?" or "+" or "-"))
            {
                tokens = [];
                return false;
            }

            index++;
            result.Add(new Token(value, start, 1, TokenKind.Punctuation));
        }

        tokens = result;
        return true;
    }

    private enum TokenKind
    {
        Word,
        Number,
        Punctuation,
    }

    private readonly record struct Token(string Value, int Start, int Length, TokenKind Kind);

    private readonly record struct FoodSubject(
        bool Present,
        string? FoodKind,
        string? ResourceId,
        int TokensConsumed);

    private sealed class OrderParser(
        IReadOnlyList<Token> tokens,
        IReadOnlyList<MapResource> resources,
        Func<MapResource, string> foodKnowledgeKind)
    {
        private int position;

        public OwnerInstructionOrder? Parse()
        {
            var end = tokens.Count;
            if (end > 0 && tokens[end - 1].Value is "." or "!" or "?")
                end--;
            if (end == 0)
                return null;

            if (ReadWord("please") && position == end)
                return null;

            var keepPrefix = ReadWord("keep");
            var repeatPrefix = keepPrefix || ReadWord("repeat") || ReadWord("repeatedly");

            if (!TryReadAction(out var action, out var actionVerb))
                return null;

            if (keepPrefix && action is not ("harvest_food" or "consume_food" or "store_material"))
                return null;
            if (keepPrefix && actionVerb is not ("gathering" or "harvesting" or "eating" or "storing"))
                return null;

            var hasExplicitQuantity = TryReadQuantity(out var requestedUnits);
            if (action == "seek_food" && hasExplicitQuantity)
                return null;

            var materialKind = action is "harvest_food" or "store_material" ? TryReadMaterialSubject() : null;
            if (action == "store_material" && materialKind is null) return null;
            if (materialKind is not null && action == "harvest_food") action = "gather_material";
            var subject = materialKind is null ? TryReadFoodSubject() : default;
            if (materialKind is null && !subject.Present && (action != "consume_food" || hasExplicitQuantity))
                return null;

            var targetFoodKind = subject.FoodKind;
            var targetResourceId = subject.ResourceId;
            GridPoint? targetPosition = null;
            var hasLocation = action == "store_material" ? TryReadHomeStorageLocation() :
                TryReadLocation(action, targetFoodKind, ref targetResourceId, ref targetPosition, materialKind);
            if (!hasLocation)
                return null;
            if (action == "consume_food" && (targetResourceId is not null || targetPosition is not null))
                return null;

            var repeat = repeatPrefix;
            if (ReadWord("until"))
            {
                if (!ReadWord("cancelled") && !ReadWord("canceled"))
                    return null;
                repeat = true;
            }

            if (ReadWord("now"))
                _ = true;
            else if (ReadWord("please"))
                _ = true;

            if (position != end)
                return null;
            if (targetResourceId is not null && targetPosition is not null)
                return null;

            return new OwnerInstructionOrder(
                action,
                "queued",
                hasExplicitQuantity ? requestedUnits : 1,
                0,
                action switch
                {
                    "seek_food" => "arrivals",
                    "harvest_food" when !hasExplicitQuantity => "harvests",
                    "gather_material" => hasExplicitQuantity ? "material_items" : "harvests",
                    "store_material" => hasExplicitQuantity ? "material_items" : "storage_loads",
                    _ => "food_items",
                },
                repeat,
                hasExplicitQuantity,
                targetFoodKind,
                targetResourceId,
                targetPosition,
                TargetMaterialKind: materialKind);
        }

        private bool TryReadHomeStorageLocation()
        {
            if (!ReadWord("in") && !ReadWord("at")) return true;
            if (ReadWord("home")) return true;
            if (!ReadWord("my") && !ReadWord("your") && !ReadWord("the")) return false;
            return ReadWord("house");
        }

        private string? TryReadMaterialSubject()
        {
            var start = position;
            if (!ReadWord("the")) _ = ReadWord("some");
            if (ReadWord("wood")) return "wood";
            if (TryReadAnyWord("stone", "stones")) return "stone";
            if (ReadWord("clay")) return "clay";
            if (TryReadAnyWord("diamond", "diamonds")) return "diamond";
            if (ReadWord("iron"))
            {
                if (ReadWord("ore")) return "iron_ore";
            }
            else if (ReadWord("gold"))
            {
                if (ReadWord("ore")) return "gold_ore";
            }
            else
            {
                _ = ReadWord("plant");
                if (TryReadAnyWord("fiber", "fibre")) return "fiber";
            }
            position = start;
            return null;
        }

        private bool TryReadAction(out string action, out string verb)
        {
            action = "";
            verb = "";
            if (TryReadAnyWord("store", "stores", "storing"))
            {
                action = "store_material";
                verb = tokens[position - 1].Value;
                return true;
            }

            if (TryReadAnyWord("eat", "eats", "eating"))
            {
                action = "consume_food";
                verb = tokens[position - 1].Value;
                return true;
            }

            if (TryReadAnyWord("gather", "gathers", "gathering", "harvest", "harvests", "harvesting"))
            {
                action = "harvest_food";
                verb = tokens[position - 1].Value;
                return true;
            }

            if (!TryReadAnyWord("go", "goes", "going", "travel", "travels",
                    "traveling", "travelling", "move", "moves", "moving"))
                return false;
            if (!ReadWord("to"))
                return false;

            action = "seek_food";
            verb = tokens[position - 2].Value;
            return true;
        }

        private bool TryReadQuantity(out int quantity)
        {
            quantity = 0;
            if (position >= tokens.Count || position == 0 || GapBefore(position) == 0)
                return false;

            if (tokens[position].Kind == TokenKind.Word &&
                NumberWords.TryGetValue(tokens[position].Value, out quantity))
            {
                position++;
                return true;
            }

            if (tokens[position].Kind != TokenKind.Number)
                return false;

            var start = position;
            var raw = tokens[position].Value;
            var after = position + 1;
            if (after + 1 < tokens.Count && tokens[after].Value == "," &&
                tokens[after + 1].Kind == TokenKind.Number &&
                tokens[after + 1].Value.Length == 3 &&
                GapBetween(after - 1, after) == 0 && GapBetween(after, after + 1) == 0)
            {
                raw += tokens[after + 1].Value;
                position = after + 2;
            }
            else
            {
                position++;
            }

            if (!int.TryParse(raw, NumberStyles.None, CultureInfo.InvariantCulture, out quantity) ||
                quantity is < 1 or > 1000)
            {
                position = start;
                quantity = 0;
                return false;
            }

            return true;
        }

        private FoodSubject TryReadFoodSubject()
        {
            var subjectStart = position;
            if (ReadWord("the") || ReadWord("a") || ReadWord("an") || ReadWord("some"))
            {
                if (position >= tokens.Count)
                {
                    position = subjectStart;
                    return default;
                }
            }

            var category = ReadFoodCategory();
            var resource = MatchResourceAlias(position);
            if (resource.Present && (category.TokensConsumed == 0 ||
                    resource.TokensConsumed > category.TokensConsumed))
            {
                position += resource.TokensConsumed;
                return new FoodSubject(true, foodKnowledgeKind(resource.Resource!), resource.Resource!.Id,
                    position - subjectStart);
            }

            if (category.TokensConsumed > 0)
            {
                position += category.TokensConsumed;
                return new FoodSubject(true, category.FoodKind, null, position - subjectStart);
            }

            if (resource.Present)
            {
                position += resource.TokensConsumed;
                return new FoodSubject(true, foodKnowledgeKind(resource.Resource!), resource.Resource!.Id,
                    position - subjectStart);
            }

            position = subjectStart;
            return default;
        }

        private FoodSubject ReadFoodCategory()
        {
            if (position >= tokens.Count || tokens[position].Kind != TokenKind.Word)
                return default;

            var word = tokens[position].Value;
            if (word is "berry" or "berries")
                return new FoodSubject(true, "berries", null, 1);
            if (word == "fruit")
                return new FoodSubject(true, "fruit", null, 1);
            if (word == "wild" && position + 1 < tokens.Count && IsWord(position + 1, "greens"))
                return new FoodSubject(true, "wild_greens", null, 2);
            if (word == "food")
            {
                var count = position + 1 < tokens.Count &&
                    IsWord(position + 1, "source", "site", "item", "items", "piece", "pieces", "serving", "servings")
                    ? 2 : 1;
                return new FoodSubject(true, null, null, count);
            }

            if (word is "item" or "items" or "piece" or "pieces" or "serving" or "servings")
                return new FoodSubject(true, null, null, 1);

            return default;
        }

        private (bool Present, MapResource? Resource, int TokensConsumed) MatchResourceAlias(int start, string? materialKind = null)
        {
            var candidates = resources
                .Where(resource => materialKind is null ? resource.Kind is "food" or "fruit" : MatchesMaterial(resource, materialKind))
                .SelectMany(resource => ResourceAliasTokens(resource.Id)
                    .Select(alias => (Resource: resource, Alias: alias)))
                .Where(candidate => MatchesTokens(start, candidate.Alias))
                .OrderByDescending(candidate => candidate.Alias.Length)
                .ThenByDescending(candidate => candidate.Resource.Id.Length)
                .ToArray();
            if (candidates.Length == 0)
                return default;

            var longest = candidates[0].Alias.Length;
            var matches = candidates.Where(candidate => candidate.Alias.Length == longest)
                .Select(candidate => candidate.Resource)
                .DistinctBy(resource => resource.Id, StringComparer.Ordinal)
                .ToArray();
            if (matches.Length != 1)
                return default;

            return (true, matches[0], longest);
        }

        private static IEnumerable<Token[]> ResourceAliasTokens(string resourceId)
        {
            var canonical = resourceId.ToLowerInvariant();
            var spaced = canonical.Replace('-', ' ').Replace('_', ' ');
            foreach (var alias in new[] { canonical, spaced }.Distinct(StringComparer.Ordinal))
            {
                if (TryTokenize(alias, out var tokens) && tokens.Count > 0)
                    yield return tokens.ToArray();
            }
        }

        private bool MatchesTokens(int start, IReadOnlyList<Token> alias)
        {
            if (start + alias.Count > tokens.Count)
                return false;
            for (var offset = 0; offset < alias.Count; offset++)
            {
                var token = tokens[start + offset];
                if (token.Kind != alias[offset].Kind || token.Value != alias[offset].Value)
                    return false;
            }
            return true;
        }

        private bool TryReadLocation(
            string action,
            string? targetFoodKind,
            ref string? targetResourceId,
            ref GridPoint? targetPosition,
            string? materialKind = null)
        {
            if (position >= tokens.Count || tokens[position].Kind != TokenKind.Word ||
                !IsWord(position, "from", "at", "near", "by", "in"))
                return true;
            if (action == "consume_food")
                return false;

            var preposition = tokens[position++].Value;
            if (preposition != "from" && TryReadCoordinate(out var coordinate))
            {
                targetPosition = coordinate;
                return true;
            }

            var resource = MatchResourceAlias(position, materialKind);
            if (!resource.Present)
                return false;
            if (targetResourceId is not null || targetPosition is not null)
                return false;
            var resourceKind = foodKnowledgeKind(resource.Resource!);
            if (targetFoodKind is not null && targetFoodKind != resourceKind)
                return false;

            targetResourceId = resource.Resource!.Id;
            position += resource.TokensConsumed;
            return true;
        }

        private bool TryReadCoordinate(out GridPoint coordinate)
        {
            coordinate = default;
            var start = position;
            var tilePrefix = ReadWord("tile");
            var parenthesized = ReadToken("(");
            if (!tilePrefix) _ = ReadWord("tile");

            if (!TryReadSignedInteger(out var x))
            {
                position = start;
                return false;
            }

            if (ReadToken(",") || ReadToken("/"))
            {
                // Delimited coordinates do not need whitespace around the separator.
            }
            else if (position < tokens.Count && GapBefore(position) > 0)
            {
                // "at 12 4" is an accepted compact coordinate form.
            }
            else
            {
                position = start;
                return false;
            }

            if (!TryReadSignedInteger(out var y))
            {
                position = start;
                return false;
            }

            if (parenthesized && !ReadToken(")"))
            {
                position = start;
                return false;
            }
            if (x is < -10_000_000 or > 10_000_000 || y is < -10_000_000 or > 10_000_000)
            {
                position = start;
                return false;
            }

            coordinate = new GridPoint(x, y);
            return true;
        }

        private bool TryReadSignedInteger(out int value)
        {
            value = 0;
            var sign = "";
            if (position < tokens.Count && tokens[position].Value is "+" or "-")
            {
                sign = tokens[position].Value;
                var signIndex = position++;
                if (position >= tokens.Count || GapBetween(signIndex, position) != 0)
                    return false;
            }

            if (position >= tokens.Count || tokens[position].Kind != TokenKind.Number)
                return false;

            var raw = sign + tokens[position].Value;
            position++;
            return int.TryParse(raw, NumberStyles.AllowLeadingSign,
                CultureInfo.InvariantCulture, out value);
        }

        private bool TryReadAnyWord(params string[] values)
        {
            if (position >= tokens.Count || tokens[position].Kind != TokenKind.Word ||
                !values.Contains(tokens[position].Value, StringComparer.Ordinal))
                return false;
            position++;
            return true;
        }

        private bool ReadWord(string value)
        {
            if (position >= tokens.Count || tokens[position].Kind != TokenKind.Word ||
                tokens[position].Value != value)
                return false;
            position++;
            return true;
        }

        private bool ReadToken(string value)
        {
            if (position >= tokens.Count || tokens[position].Value != value)
                return false;
            position++;
            return true;
        }

        private bool IsWord(int index, params string[] values) =>
            index < tokens.Count && tokens[index].Kind == TokenKind.Word &&
            values.Contains(tokens[index].Value, StringComparer.Ordinal);

        private int GapBefore(int index) =>
            index <= 0 ? 0 : tokens[index].Start - (tokens[index - 1].Start + tokens[index - 1].Length);

        private int GapBetween(int left, int right) =>
            tokens[right].Start - (tokens[left].Start + tokens[left].Length);
    }
}
