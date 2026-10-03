using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using ClankerWorld.Simulation.Cognition;
using ClankerWorld.Simulation.Harness;

namespace ClankerWorld.Simulation.Playtest;

public static class TownLandClaimRules
{
    public static string DescribeTiles(IEnumerable<GridPoint> tiles) => string.Join("; ",
        TownLandRightsRules.OrderTiles(tiles).Select(tile => FormattableString.Invariant($"({tile.X}, {tile.Y})")));

    public static string RequestKey(IEnumerable<GridPoint> tiles) => "land_claim:" +
        Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(DescribeTiles(tiles))));

    public static string TitleId(string proposalId) => "town-title:claim:" +
        Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(proposalId)));

    public static bool CanClaim(SeededMap map, TownRuntimeState town, IReadOnlyList<GridPoint>? tiles,
        IReadOnlyList<TownLandTitleRecord> titles)
    {
        if (town.OriginSite is null || tiles is not { Count: > 0 and <= CognitionDecisionResponse.MaximumCivicLandTiles } ||
            tiles.Any(tile => !map.IsLand(tile)) || tiles.Distinct().Count() != tiles.Count ||
            TownLandRightsRules.ConnectedPlots(map, tiles).Count != 1)
            return false;
        var claimed = titles.SelectMany(title => title.Tiles).ToHashSet();
        if (tiles.Any(claimed.Contains)) return false;
        var own = titles.Where(title => title.TownId == town.Id).SelectMany(title => title.Tiles).ToHashSet();
        return tiles.Any(tile => new[] { new GridPoint(tile.X - 1, tile.Y), new GridPoint(tile.X + 1, tile.Y),
            new GridPoint(tile.X, tile.Y - 1), new GridPoint(tile.X, tile.Y + 1) }
            .Any(neighbor => own.Contains(map.WrapColumn(neighbor))));
    }

    /// <summary>Unclaimed land tiles beside this Town's title, nearest first; each one alone is a claimable plot.</summary>
    public static GridPoint[] ClaimableNear(SeededMap map, TownRuntimeState town, IReadOnlyList<TownLandTitleRecord> titles,
        GridPoint from, int count)
    {
        if (town.OriginSite is null) return [];
        var claimed = titles.SelectMany(title => title.Tiles).ToHashSet();
        return titles.Where(title => title.TownId == town.Id).SelectMany(title => title.Tiles)
            .SelectMany(tile => new[] { new GridPoint(tile.X - 1, tile.Y), new GridPoint(tile.X + 1, tile.Y),
                new GridPoint(tile.X, tile.Y - 1), new GridPoint(tile.X, tile.Y + 1) })
            .Select(map.WrapColumn).Where(tile => map.IsLand(tile) && !claimed.Contains(tile)).Distinct()
            .OrderBy(tile => map.FootDistance(from, tile)).ThenBy(tile => tile.Y).ThenBy(tile => tile.X)
            .Take(count).ToArray();
    }

    public static void Validate(SeededMap map, long tick, IReadOnlyList<TownRuntimeState> towns,
        IReadOnlyList<TownLandTitleRecord> titles)
    {
        var approvals = new HashSet<string>(StringComparer.Ordinal);
        foreach (var town in towns)
            foreach (var proposal in town.Governance?.Proposals.Where(p => p.Kind == "land_claim") ?? [])
            {
                if (proposal.LandClaimTiles is not { Count: <= CognitionDecisionResponse.MaximumCivicLandTiles } tiles ||
                    !TownLandRightsRules.IsValidPlot(map, tiles, tick, proposal.OpenedTick) ||
                    proposal.RequestKey != RequestKey(tiles))
                    throw new InvalidDataException("A saved Council land claim must name one exact connected land plot.");
                var id = TitleId(proposal.Id);
                var title = titles.SingleOrDefault(title => title.Id == id);
                if (proposal.Status == "passed")
                {
                    if (title is null || title.TownId != town.Id || title.RecordedTick != proposal.SettledTick ||
                        !title.Tiles.SequenceEqual(tiles))
                        throw new InvalidDataException("A passed Council land claim must match its recorded Town title.");
                    approvals.Add(id);
                }
                else if (title is not null)
                    throw new InvalidDataException("An unfinished or refused Council land claim cannot hold title.");
            }
        if (titles.Any(title => title.Id.StartsWith("town-title:claim:", StringComparison.Ordinal) && !approvals.Contains(title.Id)))
            throw new InvalidDataException("A Council land title is missing its approval.");
    }
}

public sealed partial class PrivateWorldRuntime
{
    private TownGovernanceState SubmitTownLandClaim(TownRuntimeState town, TownGovernanceState state,
        string actor, IReadOnlyList<CognitionLandTile>? requested)
    {
        var tiles = requested?.Select(tile => new GridPoint(tile.X, tile.Y)).ToArray();
        if (!TownLandClaimRules.CanClaim(map, town, tiles, townLandTitles))
            throw new InvalidOperationException("A land claim needs connected unclaimed land adjoining this Town's title.");
        var first = TownLandRightsRules.OrderTiles(tiles!)[0];
        var text = string.Create(CultureInfo.InvariantCulture,
            $"Claim {tiles!.Length} land tiles adjoining {town.Name}, starting at ({first.X}, {first.Y}).");
        return TownGovernanceRules.SubmitProposal(state, town.Id, actor, "land_claim", null, text,
            "council:" + state.Revision, TownAdults(town), WorldTick, CivicDay, landClaimTiles: tiles);
    }

    private (TownRuntimeState Town, TownGovernanceState Governance) ApplyApprovedTownLandClaims(
        TownRuntimeState town, TownGovernanceState updated)
    {
        foreach (var proposal in updated.Proposals.Where(p => p.Kind == "land_claim" &&
                     (p.Status == "pending" || p.Status == "passed" &&
                         !town.Governance!.Proposals.Any(old => old.Id == p.Id && old.Status == "passed"))).ToArray())
        {
            if (!TownLandClaimRules.CanClaim(map, town, proposal.LandClaimTiles, townLandTitles))
            {
                updated = TownGovernanceRules.CancelLandClaim(updated, proposal, WorldTick);
                continue;
            }
            if (proposal.Status != "passed") continue;
            var tiles = proposal.LandClaimTiles!;
            townLandTitles.Add(new(TownLandClaimRules.TitleId(proposal.Id), town.Id, tiles.ToArray(), WorldTick));
            townLandTitles = townLandTitles.OrderBy(title => title.Id, StringComparer.Ordinal).ToList();
            town = town with { BorderTiles = TownLandRightsRules.OrderTiles(town.BorderTiles.Concat(tiles).Distinct()) };
            AppendEvent("town_land_claimed", $"{town.Id}|{proposal.Id}|{TownLandClaimRules.DescribeTiles(tiles)}", tiles[0]);
        }
        return (town, updated);
    }
}
