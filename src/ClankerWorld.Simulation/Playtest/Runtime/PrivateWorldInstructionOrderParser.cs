using System.Globalization;
using ClankerWorld.Simulation.Harness;

namespace ClankerWorld.Simulation.Playtest;

/// <summary>
/// Parses supported food, inventory, repair, field, production, custody, movement, guardian, building and shelter orders.
/// Every token must belong to one of these forms; unconsumed text is not guessed.
/// </summary>
internal static class PrivateWorldInstructionOrderParser
{
    internal static bool IsEquipmentKind(string? kind) =>
        kind is "clothing" or "padded_coat" or "rain_cloak" or "basket" or "sack" or "leather_sack";

    internal static bool IsToolKind(string? kind) => kind is not null && ToolProgressionRules.Find(kind) is not null;

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
        Func<MapResource, string> foodKnowledgeKind,
        IReadOnlyList<ProductionOrderRecipe>? productionRecipes = null,
        IReadOnlyList<DeliveryOrderInput>? deliveryInputs = null,
        IReadOnlyList<BuildingOrderDefinition>? buildings = null)
    {
        ArgumentNullException.ThrowIfNull(text);
        ArgumentNullException.ThrowIfNull(resources);
        ArgumentNullException.ThrowIfNull(foodKnowledgeKind);

        return TryTokenize(text, out var tokens)
            ? new OrderParser(tokens, resources, foodKnowledgeKind, productionRecipes ?? [], deliveryInputs ?? [], buildings ?? []).Parse()
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
        Func<MapResource, string> foodKnowledgeKind,
        IReadOnlyList<ProductionOrderRecipe> productionRecipes,
        IReadOnlyList<DeliveryOrderInput> deliveryInputs,
        IReadOnlyList<BuildingOrderDefinition> buildings)
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

            var actionStart = position;
            if (TryReadShelterOrder(end, repeatPrefix) is { } shelterOrder) return shelterOrder;
            position = actionStart;
            if (TryReadBuildingOrder(end, repeatPrefix) is { } buildingOrder) return buildingOrder;
            position = actionStart;
            if (TryReadDeliveryOrder(end, repeatPrefix, keepPrefix) is { } deliveryOrder) return deliveryOrder;
            position = actionStart;
            if (TryReadProductionOrder(end, repeatPrefix, keepPrefix) is { } productionOrder) return productionOrder;
            position = actionStart;
            if (TryReadFieldOrder(end, repeatPrefix, keepPrefix) is { } fieldOrder) return fieldOrder;
            position = actionStart;
            if (TryReadCustodyOrder(end, repeatPrefix, keepPrefix) is { } custodyOrder) return custodyOrder;
            position = actionStart;

            if (!TryReadAction(out var action, out var actionVerb))
                return null;

            if (keepPrefix && action is not ("harvest_food" or "consume_food" or "store_material" or "collect_material" or "repair_equipment"))
                return null;
            if (keepPrefix && actionVerb is not ("gathering" or "harvesting" or "eating" or "storing" or "collecting" or "repairing"))
                return null;

            if (action == "seek_food" && TryReadCoordinate(out var destination))
            {
                if (repeatPrefix) return null;
                if (!ReadWord("now")) _ = ReadWord("please");
                return position == end
                    ? new OwnerInstructionOrder("move_to", "queued", 1, 0, "arrivals", false,
                        TargetPosition: destination)
                    : null;
            }

            _ = action is "collect_material" or "store_material" or "repair_equipment" && ReadWord("my");
            var hasExplicitQuantity = TryReadQuantity(out var requestedUnits);
            if (action == "seek_food" && hasExplicitQuantity)
                return null;

            var equipmentKind = action is "repair_equipment" or "collect_material" or "store_material" ? TryReadEquipmentSubject() : null;
            if (action == "repair_equipment" && equipmentKind is null) return null;
            if (action == "repair_equipment" && IsToolKind(equipmentKind)) action = "repair_tool";
            if (action == "collect_material" && equipmentKind is not null) action = "collect_equipment";
            if (action == "store_material" && equipmentKind is not null) action = "store_equipment";
            var materialKind = action is "harvest_food" or "store_material" or "collect_material" ? TryReadMaterialSubject() : null;
            if (action == "store_material" && materialKind is null) return null;
            if (materialKind is not null && action == "harvest_food") action = "gather_material";
            var subject = materialKind is null && equipmentKind is null
                ? action == "collect_material" ? TryReadCollectionFoodSubject() : TryReadFoodSubject(action == "consume_food")
                : default;
            if (action == "collect_material" && subject.Present) action = "collect_food";
            if (materialKind is null && equipmentKind is null && !subject.Present && (action != "consume_food" || hasExplicitQuantity))
                return null;

            var targetFoodKind = subject.FoodKind;
            var targetResourceId = subject.ResourceId;
            GridPoint? targetPosition = null;
            var hasLocation = action switch
            {
                "collect_material" or "collect_food" or "collect_equipment" => TryReadCollectionLocation(ref targetPosition),
                "repair_equipment" or "repair_tool" => true,
                "store_material" or "store_equipment" => TryReadHomeStorageLocation(ref targetPosition),
                _ => TryReadLocation(action, targetFoodKind, ref targetResourceId, ref targetPosition, materialKind),
            };
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
                    "collect_material" => hasExplicitQuantity ? "material_items" : "collection_loads",
                    "store_material" => hasExplicitQuantity ? "material_items" : "storage_loads",
                    "store_equipment" => hasExplicitQuantity ? "equipment_items" : "storage_loads",
                    "collect_food" => hasExplicitQuantity ? "food_items" : "collection_loads",
                    "collect_equipment" => hasExplicitQuantity ? "equipment_items" : "collection_loads",
                    "repair_equipment" or "repair_tool" => "repairs",
                    _ => "food_items",
                },
                repeat,
                hasExplicitQuantity,
                targetFoodKind,
                targetResourceId,
                targetPosition,
                TargetMaterialKind: materialKind,
                TargetEquipmentKind: equipmentKind);
        }

        private OwnerInstructionOrder? TryReadShelterOrder(int end, bool repeat)
        {
            if (repeat) return null;
            string action;
            var requiresHouse = false;
            if (ReadWord("seek"))
            {
                if (!ReadWord("shelter")) return null;
                action = "seek_shelter";
            }
            else if (ReadWord("take"))
            {
                if (!ReadWord("cover")) return null;
                action = "seek_shelter";
            }
            else if (ReadWord("shelter"))
            {
                action = "seek_shelter";
                requiresHouse = true;
            }
            else if (TryReadAnyWord("light", "tend"))
            {
                _ = TryReadAnyWord("a", "the");
                if (!ReadWord("fire")) return null;
                action = "tend_fire";
            }
            else return null;
            string? buildingKind = null;
            if (ReadWord("in"))
            {
                if (!ReadWord("my") || !ReadWord("house")) return null;
                buildingKind = "house";
            }
            if (requiresHouse && buildingKind is null) return null;
            GridPoint? targetPosition = null;
            if (ReadWord("at"))
            {
                if (!TryReadCoordinate(out var requested)) return null;
                targetPosition = requested;
            }
            if (!ReadWord("now")) _ = ReadWord("please");
            if (position != end) return null;
            return new(action, "queued", 1, 0, action == "seek_shelter" ? "shelters" : "fires", false,
                TargetPosition: targetPosition, TargetBuildingKind: buildingKind);
        }

        private OwnerInstructionOrder? TryReadBuildingOrder(int end, bool repeat)
        {
            if (repeat) return null;
            var action = ReadWord("build") ? "construct_building" : ReadWord("expand") ? "expand_building" : null;
            if (action is null) return null;
            var explicitQuantity = TryReadQuantity(out var quantity);
            if (!explicitQuantity && (ReadWord("a") || ReadWord("an")))
            {
                explicitQuantity = true;
                quantity = 1;
            }
            if (explicitQuantity && quantity != 1 || action == "expand_building" && !ReadWord("my")) return null;
            string? kind;
            if (ReadWord("town")) kind = ReadWord("warehouse") ? "warehouse" : null;
            else if (ReadWord("animal")) kind = ReadWord("yard") ? "animal-yard" : null;
            else if (TryReadAnyWord("house", "farmhouse", "blacksmith", "tailor", "silo", "clinic", "store"))
                kind = tokens[position - 1].Value;
            else kind = null;
            if (kind == "tailor") _ = ReadWord("shop");
            if (!PrivateWorldBuildingOrderCatalog.Supports(action, kind)) return null;
            var definition = buildings.SingleOrDefault(item => item.BuildingKind == kind);
            if (definition is null) return null;
            GridPoint? targetPosition = null;
            if (ReadWord("at"))
            {
                if (!TryReadCoordinate(out var requested)) return null;
                targetPosition = requested;
            }
            if (!ReadWord("now")) _ = ReadWord("please");
            if (position != end) return null;
            return new(action, "queued", 1, 0, action == "construct_building" ? "buildings" : "expansions", false,
                explicitQuantity, TargetPosition: targetPosition, TargetBuildingKind: kind,
                TargetDefinitionId: action == "construct_building" ? definition.Definition.CanonicalId : null);
        }

        private OwnerInstructionOrder? TryReadDeliveryOrder(int end, bool repeat, bool keep)
        {
            if (position >= end || tokens[position].Kind != TokenKind.Word) return null;
            var verb = tokens[position++].Value;
            var purpose = verb switch
            {
                "haul" or "hauls" or "hauling" => "household_stock",
                "supply" or "supplies" or "supplying" => "workstation_input",
                "deliver" or "delivers" or "delivering" => "household_food",
                "donate" or "donates" or "donating" => "town_surplus",
                "stock" or "stocks" or "stocking" => "store_stock",
                _ => null,
            };
            if (purpose is null || keep && !verb.EndsWith("ing", StringComparison.Ordinal)) return null;
            var explicitQuantity = TryReadQuantity(out var quantity);
            if (!explicitQuantity && (ReadWord("a") || ReadWord("an")))
            {
                explicitQuantity = true;
                quantity = 1;
            }
            if (!explicitQuantity) quantity = 1;
            if (!ReadWord("the")) _ = ReadWord("some");
            var subject = PrivateWorldDeliveryOrderCatalog.Subjects.SelectMany(goods => goods.Names
                    .Select(name => (goods.ItemKind, Tokens: name.Split(' '))))
                .Where(item => item.Tokens.Select((word, index) => IsWord(position + index, word)).All(value => value))
                .OrderByDescending(item => item.Tokens.Length).FirstOrDefault();
            if (subject.ItemKind is null) return null;
            position += subject.Tokens.Length;
            if (!(purpose == "store_stock" ? ReadWord("in") : ReadWord("to")) || !ReadWord("my")) return null;
            string? building;
            if (purpose == "town_surplus")
                building = ReadWord("town") && ReadWord("warehouse") ? "warehouse" : null;
            else if (TryReadAnyWord("house", "farmhouse", "silo", "blacksmith", "tailor", "clinic", "store"))
                building = tokens[position - 1].Value;
            else building = null;
            if (building == "tailor") _ = ReadWord("shop");
            if (!PrivateWorldDeliveryOrderCatalog.IsValidTarget(purpose, subject.ItemKind, building, deliveryInputs)) return null;
            GridPoint? targetPosition = null;
            if (ReadWord("at"))
            {
                if (!TryReadCoordinate(out var destination)) return null;
                targetPosition = destination;
            }
            if (ReadWord("until"))
            {
                if (!ReadWord("cancelled") && !ReadWord("canceled")) return null;
                repeat = true;
            }
            if (!ReadWord("now")) _ = ReadWord("please");
            if (position != end) return null;
            return new("deliver_stock", "queued", quantity, 0, explicitQuantity ? "goods_items" : "delivery_loads",
                repeat, explicitQuantity, TargetPosition: targetPosition, TargetItemKind: subject.ItemKind,
                DeliveryPurpose: purpose, TargetBuildingKind: building);
        }

        private OwnerInstructionOrder? TryReadCustodyOrder(int end, bool repeat, bool keep)
        {
            var action = TryReadAnyWord("collect", "collects", "collecting") ? "collect_goods" :
                TryReadAnyWord("store", "stores", "storing") ? "store_goods" :
                TryReadAnyWord("return", "returns", "returning") ? "return_borrowed" : null;
            if (action is null || keep && !tokens[position - 1].Value.EndsWith("ing", StringComparison.Ordinal)) return null;
            if (action != "return_borrowed") _ = ReadWord("my");
            var explicitQuantity = TryReadQuantity(out var quantity);
            if (!explicitQuantity && (ReadWord("a") || ReadWord("an")))
            {
                explicitQuantity = true;
                quantity = 1;
            }
            if (!explicitQuantity) quantity = 1;
            if (action == "return_borrowed" && !ReadWord("borrowed")) return null;
            var itemKind = action == "return_borrowed"
                ? TryReadReturnSubject()
                : TryReadCustodySubject();
            if (itemKind is null) return null;
            GridPoint? targetPosition = null;
            var validLocation = action switch
            {
                "collect_goods" => TryReadCollectionLocation(ref targetPosition),
                "store_goods" => TryReadHomeStorageLocation(ref targetPosition),
                _ => TryReadReturnLocation(ref targetPosition),
            };
            if (!validLocation) return null;
            if (ReadWord("until"))
            {
                if (!ReadWord("cancelled") && !ReadWord("canceled")) return null;
                repeat = true;
            }
            if (!ReadWord("now")) _ = ReadWord("please");
            if (position != end) return null;
            var progress = explicitQuantity ? "goods_items" : action switch
            {
                "collect_goods" => "collection_loads",
                "store_goods" => "storage_loads",
                _ => "return_loads",
            };
            return new(action, "queued", quantity, 0, progress, repeat, explicitQuantity,
                TargetPosition: targetPosition, TargetItemKind: itemKind);
        }

        private string? TryReadReturnSubject()
        {
            var start = position;
            var selected = TryReadCustodySubject();
            var end = position;
            position = start;
            var material = TryReadMaterialSubject();
            if (material is not null && position > end) { selected = material; end = position; }
            position = start;
            var equipment = TryReadEquipmentSubject();
            if (equipment is not null && position > end) { selected = equipment; end = position; }
            // The full noun wins: diamond ornaments, iron ore and iron knives
            // cannot be truncated to a shorter subject from another catalogue.
            position = end;
            return selected;
        }

        private string? TryReadCustodySubject()
        {
            var start = position;
            if (!ReadWord("the")) _ = ReadWord("some");
            var match = PrivateWorldCustodyOrderCatalog.All.SelectMany(goods => goods.Names
                    .Select(name => (goods.ItemKind, Tokens: name.Split(' '))))
                .Where(item => item.Tokens.Select((word, index) => IsWord(position + index, word)).All(value => value))
                .OrderByDescending(item => item.Tokens.Length).FirstOrDefault();
            if (match.ItemKind is null) { position = start; return null; }
            position += match.Tokens.Length;
            return match.ItemKind;
        }

        private bool TryReadReturnLocation(ref GridPoint? targetPosition)
        {
            if (ReadWord("to") && (!ReadWord("its") || !ReadWord("house"))) return false;
            if (!ReadWord("at")) return true;
            if (!TryReadCoordinate(out var destination)) return false;
            targetPosition = destination;
            return true;
        }

        private OwnerInstructionOrder? TryReadProductionOrder(int end, bool repeat, bool keep)
        {
            if (position >= end || tokens[position].Kind != TokenKind.Word) return null;
            var word = tokens[position].Value;
            var verb = word switch
            {
                "make" or "makes" or "making" => "make",
                "craft" or "crafts" or "crafting" => "craft",
                "cook" or "cooks" or "cooking" => "cook",
                "mill" or "mills" or "milling" => "mill",
                "refine" or "refines" or "refining" => "refine",
                "sew" or "sews" or "sewing" => "sew",
                "weave" or "weaves" or "weaving" => "weave",
                "twist" or "twists" or "twisting" => "twist",
                "cut" or "cuts" or "cutting" => "cut",
                "prepare" or "prepares" or "preparing" => "prepare",
                _ => null,
            };
            if (verb is null || keep && !word.EndsWith("ing", StringComparison.Ordinal)) return null;
            position++;
            var explicitQuantity = TryReadQuantity(out var quantity);
            if (!explicitQuantity)
            {
                if (ReadWord("a") || ReadWord("an")) { quantity = 1; explicitQuantity = true; }
                else _ = ReadWord("the");
            }
            var batches = TryReadAnyWord("batch", "batches");
            if (batches && !ReadWord("of")) return null;
            if (!explicitQuantity) quantity = 1;

            var matches = new List<(ProductionOrderRecipe Recipe, int Tokens)>();
            foreach (var recipe in productionRecipes.Where(recipe => recipe.Verbs.Contains(verb, StringComparer.Ordinal)))
            {
                IEnumerable<string> subjects = recipe.Subjects;
                if (verb == "mill" && recipe.OutputKind == "flour")
                    subjects = subjects.Concat(["grain", "grain into flour"]);
                foreach (var subject in subjects)
                    if (TryTokenize(subject, out var alias) && MatchesTokens(position, alias))
                        matches.Add((recipe, alias.Count));
            }
            if (matches.Count == 0) return null;
            var longest = matches.Max(match => match.Tokens);
            var recipes = matches.Where(match => match.Tokens == longest)
                .Select(match => match.Recipe).DistinctBy(recipe => recipe.Recipe.CanonicalId, StringComparer.Ordinal).ToArray();
            // A count must never silently select another recipe with a more convenient yield.
            if (recipes.Length != 1) return null;
            var selected = recipes[0];
            position += longest;
            var progress = batches || !explicitQuantity ? "production_batches" : "output_items";
            if (progress == "output_items" && quantity % selected.OutputQuantity != 0) return null;
            GridPoint? targetPosition = null;
            if (ReadWord("at"))
            {
                if (!TryReadCoordinate(out var point)) return null;
                targetPosition = point;
            }
            if (ReadWord("until"))
            {
                if (!ReadWord("cancelled") && !ReadWord("canceled")) return null;
                repeat = true;
            }
            if (!ReadWord("now")) _ = ReadWord("please");
            if (position != end) return null;
            return new("produce_item", "queued", quantity, 0, progress, repeat, explicitQuantity,
                TargetPosition: targetPosition, TargetRecipeId: selected.Recipe.CanonicalId,
                TargetOutputKind: selected.OutputKind);
        }

        private OwnerInstructionOrder? TryReadFieldOrder(int end, bool repeat, bool keep)
        {
            string action;
            if (TryReadAnyWord("till", "tills", "tilling")) action = "till_field";
            else if (TryReadAnyWord("plant", "plants", "planting")) action = "plant_field";
            else if (TryReadAnyWord("tend", "tends", "tending")) action = "tend_field";
            else if (TryReadAnyWord("harvest", "harvests", "harvesting")) action = "harvest_field";
            else return null;
            if (keep && tokens[position - 1].Value is not ("tilling" or "planting" or "tending" or "harvesting")) return null;
            _ = ReadWord("my");
            var explicitQuantity = TryReadQuantity(out var quantity);
            if (!explicitQuantity && !ReadWord("a")) _ = ReadWord("the");
            var hasField = TryReadAnyWord("field", "fields");
            var crop = (string?)null;
            if (action != "till_field")
            {
                var requiresCrop = hasField && ReadWord("of");
                if (ReadWord("grain")) crop = FarmFieldRules.Grain;
                else if (TryReadAnyWord("potato", "potatoes")) crop = FarmFieldRules.Potatoes;
                else
                {
                    _ = ReadWord("cultivated");
                    if (ReadWord("greens")) crop = FarmFieldRules.Greens;
                    else if (position > 0 && tokens[position - 1].Value == "cultivated") return null;
                }
                if (requiresCrop && crop is null || action == "plant_field" && crop is null) return null;
            }
            if (!hasField && (explicitQuantity || crop is null)) return null;
            GridPoint? targetPosition = null;
            if (ReadWord("at"))
            {
                if (!TryReadCoordinate(out var target)) return null;
                targetPosition = target;
            }
            if (ReadWord("until"))
            {
                if (!ReadWord("cancelled") && !ReadWord("canceled")) return null;
                repeat = true;
            }
            if (!ReadWord("now")) _ = ReadWord("please");
            if (position != end) return null;
            return new(action, "queued", explicitQuantity ? quantity : 1, 0, "fields", repeat,
                explicitQuantity, TargetPosition: targetPosition, TargetCropKind: crop);
        }

        private bool TryReadCollectionLocation(ref GridPoint? targetPosition)
        {
            if (!ReadWord("from") && !ReadWord("at")) return true;
            if (!TryReadCoordinate(out var source)) return false;
            targetPosition = source;
            return true;
        }

        private bool TryReadHomeStorageLocation(ref GridPoint? targetPosition)
        {
            var directCoordinate = ReadWord("at");
            if (!directCoordinate && !ReadWord("in")) return true;
            if (directCoordinate && TryReadCoordinate(out var direct))
            {
                targetPosition = direct;
                return true;
            }
            if (!ReadWord("home") &&
                ((!ReadWord("my") && !ReadWord("your") && !ReadWord("the")) || !ReadWord("house"))) return false;
            if (!ReadWord("at")) return true;
            if (!TryReadCoordinate(out var destination)) return false;
            targetPosition = destination;
            return true;
        }

        private string? TryReadEquipmentSubject()
        {
            var start = position;
            if (!ReadWord("the") && !ReadWord("a")) _ = ReadWord("an");
            var crude = ReadWord("crude");
            var material = ReadWord("wooden") ? "wooden" : ReadWord("stone") ? "stone" : ReadWord("iron") ? "iron" : null;
            if (material is not null)
            {
                var tool = TryReadAnyWord("axe", "axes") ? "axe" :
                    TryReadAnyWord("pickaxe", "pickaxes") ? "pickaxe" :
                    TryReadAnyWord("hoe", "hoes") ? "hoe" :
                    TryReadAnyWord("hammer", "hammers") ? "hammer" :
                    TryReadAnyWord("sickle", "sickles") ? "sickle" :
                    TryReadAnyWord("knife", "knives") ? "knife" : null;
                var kind = (crude ? "crude_" : "") + material + "_" + tool;
                if (IsToolKind(kind)) return kind;
                position = start;
                return null;
            }
            if (crude)
            {
                position = start;
                return null;
            }
            if (TryReadAnyWord("basket", "baskets")) return "basket";
            if (ReadWord("leather")) return TryReadAnyWord("sack", "sacks") ? "leather_sack" : null;
            if (TryReadAnyWord("sack", "sacks")) return "sack";
            if (ReadWord("padded"))
            {
                if (TryReadAnyWord("coat", "coats")) return "padded_coat";
            }
            else if (ReadWord("rain"))
            {
                if (TryReadAnyWord("cloak", "cloaks")) return "rain_cloak";
            }
            else
            {
                _ = ReadWord("basic");
                if (TryReadAnyWord("clothing", "clothes", "garment", "garments")) return "clothing";
            }
            position = start;
            return null;
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
            if (TryReadAnyWord("repair", "repairs", "repairing"))
            {
                action = "repair_equipment";
                verb = tokens[position - 1].Value;
                return true;
            }

            if (TryReadAnyWord("collect", "collects", "collecting"))
            {
                action = "collect_material";
                verb = tokens[position - 1].Value;
                return true;
            }

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

        private FoodSubject TryReadCollectionFoodSubject()
        {
            var start = position;
            _ = TryReadAnyWord("the", "a", "some");
            string? kind;
            if (TryReadAnyWord("berry", "berries")) kind = "berries";
            else if (ReadWord("fruit")) kind = "fruit";
            else if (IsWord(position, "wild", "cultivated") && IsWord(position + 1, "greens"))
            {
                kind = tokens[position].Value == "wild" ? "wild_greens" : "cultivated_greens";
                position += 2;
            }
            else if (ReadWord("food"))
            {
                kind = null;
                _ = TryReadAnyWord("item", "items", "piece", "pieces", "serving", "servings");
            }
            else
            {
                position = start;
                return default;
            }
            return new FoodSubject(true, kind, null, position - start);
        }

        private FoodSubject TryReadFoodSubject(bool includeCookedFood)
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

            var category = ReadFoodCategory(includeCookedFood);
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

        private FoodSubject ReadFoodCategory(bool includeCookedFood)
        {
            if (position >= tokens.Count || tokens[position].Kind != TokenKind.Word)
                return default;

            var word = tokens[position].Value;
            if (includeCookedFood)
            {
                if (position + 1 < tokens.Count)
                {
                    if (word is "berry" or "fruit" && IsWord(position + 1, "porridge"))
                        return new FoodSubject(true, word + "_porridge", null, 2);
                    if (word is "simple" or "restaurant" && IsWord(position + 1, "meal"))
                        return new FoodSubject(true, word + "_meal", null, 2);
                    if (word == "vegetable" && IsWord(position + 1, "stew"))
                        return new FoodSubject(true, "stew", null, 2);
                    if (word == "cultivated" && IsWord(position + 1, "greens"))
                        return new FoodSubject(true, "cultivated_greens", null, 2);
                }
                if (word is "porridge" or "bread" or "stew")
                    return new FoodSubject(true, word, null, 1);
            }
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
