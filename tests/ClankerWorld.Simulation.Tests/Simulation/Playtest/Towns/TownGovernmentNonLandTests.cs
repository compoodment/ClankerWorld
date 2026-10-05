using System.Text.Json;
using ClankerWorld.Simulation.Playtest;
using ClankerWorld.Simulation.Society;

namespace ClankerWorld.Simulation.Tests;

public sealed class TownGovernmentNonLandTests
{
    private const int Day = 10;

    [Theory]
    [InlineData("land")]
    [InlineData("ordinary")]
    public void AddedDutiesNeedAResidentMajorityAndKeepTheConsentingHoldersOriginalTerm(string baseMandate)
    {
        var town = Town.Elect(baseMandate);
        var original = Assert.Single(town.Government.Offices);
        var councilForm = town.Council.Form;
        var councilMembers = town.Council.Members.ToArray();
        Assert.Null(TownGovernmentRules.CurrentNonLandAuthority(town.Government, town.Tick));

        town.Advance(20);
        var change = town.ProposeExtension(baseMandate);
        town.Read(change, "a");
        town.Accept(change, "a");
        town.Yes(change, "a", "b");
        Assert.Equal("voting", town.Change(change).Status);
        Assert.Null(TownGovernmentRules.CurrentNonLandAuthority(town.Government, town.Tick));
        Assert.Empty(town.Government.NonLandGrants);

        town.Advance(21);
        town.Yes(change, "c");
        var authority = Assert.IsType<TownNonLandAuthority>(TownGovernmentRules.CurrentNonLandAuthority(town.Government, town.Tick));
        var grant = Assert.Single(town.Government.NonLandGrants);
        Assert.Equal("completed", town.Change(change).Status);
        Assert.Equal("a", authority.HolderId);
        Assert.Equal(grant.Id, authority.AuthorityId);
        Assert.Equal(21, authority.EffectiveTick);
        Assert.Equal(10, authority.TermStartTick);
        Assert.Equal(210, authority.TermEndTick);
        Assert.Equal(20, grant.ConsentTick);
        Assert.Equal(original.ElectionId, grant.BaseElectionId);
        Assert.Equal(original, town.Government.Offices.Single(office => office.Mandates == baseMandate));
        Assert.Equal(councilForm, town.Council.Form);
        Assert.Equal(councilMembers, town.Council.Members);
        Assert.False(TownGovernmentRules.NonLandAuthorityAt(town.Government, "a", grant.Id, 20));
        Assert.True(TownGovernmentRules.NonLandAuthorityAt(town.Government, "a", grant.Id, 21));
        Assert.False(TownGovernmentRules.NonLandAuthorityAt(town.Government, "b", grant.Id, 21));
        Assert.False(TownGovernmentRules.NonLandAuthorityAt(town.Government, "a", original.ElectionId!, 21));
    }

    [Fact]
    public void GeneralCandidacyCannotReplaceConsentToTheExactAddedDuties()
    {
        var town = Town.Elect("land");
        var original = Assert.Single(town.Government.Offices);
        town.Advance(20);
        town.Register("a", "non_land");
        var change = town.ProposeExtension("land");
        town.Read(change, "a");
        town.Yes(change, "a", "b", "c");

        Assert.Equal("handover", town.Change(change).Status);
        Assert.Null(TownGovernmentRules.CurrentNonLandAuthority(town.Government, town.Tick));
        town.Advance(50);
        Assert.Equal("cancelled", town.Change(change).Status);
        Assert.Equal(TownArrangementRules.NoOffice, town.Government.Arrangement.NonLand);
        Assert.Empty(town.Government.NonLandGrants);
        Assert.Equal(original, Assert.Single(town.Government.Offices));
        Assert.Throws<InvalidOperationException>(() => town.Accept(change, "a"));
    }

    [Fact]
    public void OnlyTheNamedCurrentHolderWhoLearnedThisProposalCanAcceptItsDuties()
    {
        var town = Town.Elect("land");
        town.Read(town.Government.Changes[0].Id, "a");
        town.Advance(20);
        var change = town.ProposeExtension("land");
        town.Read(change, "b");

        Assert.Throws<InvalidOperationException>(() => town.Accept(change, "a"));
        Assert.Throws<InvalidOperationException>(() => town.Accept(change, "b"));
        Assert.Null(town.Change(change).NonLandExtension!.ConsentTick);
        town.Read(change, "a");
        town.Accept(change, "a");
        Assert.Null(TownGovernmentRules.CurrentNonLandAuthority(town.Government, town.Tick));
        town.Yes(change, "a", "b", "c");
        Assert.NotNull(TownGovernmentRules.CurrentNonLandAuthority(town.Government, town.Tick));
        Assert.Throws<InvalidOperationException>(() => town.Accept(change, "a"));
    }

    [Fact]
    public void EndingTheCapturedBaseTermPreventsAStaleConsentFromCompletingTheHandover()
    {
        var town = Town.Elect("land");
        town.Advance(20);
        var change = town.ProposeExtension("land");
        town.Read(change, "a");
        town.Yes(change, "a", "b", "c");
        (town.Council, town.Government) = TownGovernmentRules.Resign(town.Council, town.Government, "a", "land", town.Tick);

        Assert.Throws<InvalidOperationException>(() => town.Accept(change, "a"));
        Assert.Null(TownGovernmentRules.CurrentNonLandAuthority(town.Government, town.Tick));
        Assert.Empty(town.Government.NonLandGrants);
        Assert.Equal(TownArrangementRules.NoOffice, town.Government.Arrangement.NonLand);
        town.Validate();
    }

    [Fact]
    public void WithdrawingCandidacyDoesNotResignTheAddedMandate()
    {
        var town = Town.WithExtension();
        var original = town.Government.Offices.Single(office => office.Mandates == "land");
        var authority = TownGovernmentRules.CurrentNonLandAuthority(town.Government, town.Tick);
        town.Advance(21);
        (town.Council, town.Government) = TownGovernmentRules.WithdrawMayor(town.Council, town.Government, "a", "land", town.Tick);
        town.Advance(town.Tick);
        Assert.Equal(authority, TownGovernmentRules.CurrentNonLandAuthority(town.Government, town.Tick));

        (town.Council, town.Government) = TownGovernmentRules.Resign(town.Council, town.Government, "a", "non_land", town.Tick);
        town.Advance(town.Tick);
        Assert.Null(TownGovernmentRules.CurrentNonLandAuthority(town.Government, town.Tick));
        Assert.Equal(TownArrangementRules.Mayor, town.Government.Arrangement.NonLand);
        Assert.Equal(original, town.Government.Offices.Single(office => office.Mandates == "land"));
        Assert.True(TownGovernmentRules.NonLandAuthorityAt(town.Government, "a", authority!.AuthorityId, 20));
        // History stores whole ticks, so it preserves findings accepted before resignation in the same tick.
        Assert.True(TownGovernmentRules.NonLandAuthorityAt(town.Government, "a", authority.AuthorityId, 21));
        Assert.False(TownGovernmentRules.NonLandAuthorityAt(town.Government, "a", authority.AuthorityId, 22));
    }

    [Theory]
    [InlineData("resignation", 31)]
    [InlineData("expiry", 210)]
    public void EndingTheBaseTermEndsItsExtensionButPreservesTheApprovedTownScope(string ending, int endedTick)
    {
        var town = Town.WithExtension();
        var authority = TownGovernmentRules.CurrentNonLandAuthority(town.Government, town.Tick)!;
        (town.Council, town.Government) = TownGovernmentRules.WithdrawMayor(town.Council, town.Government, "a", "land", town.Tick);
        town.Advance(endedTick);
        if (ending == "resignation")
        {
            (town.Council, town.Government) = TownGovernmentRules.Resign(town.Council, town.Government, "a", "land", town.Tick);
            town.Advance(town.Tick);
        }

        Assert.Null(TownGovernmentRules.CurrentNonLandAuthority(town.Government, endedTick));
        Assert.Equal(TownArrangementRules.Mayor, town.Government.Arrangement.NonLand);
        Assert.True(TownGovernmentRules.NonLandScopeAt(town.Government, endedTick));
        Assert.All(town.Government.Offices, office => Assert.Null(office.HolderId));
        Assert.True(TownGovernmentRules.NonLandAuthorityAt(town.Government, "a", authority.AuthorityId, 20));
        Assert.True(TownGovernmentRules.NonLandAuthorityAt(town.Government, "a", authority.AuthorityId, endedTick));
        Assert.False(TownGovernmentRules.NonLandAuthorityAt(town.Government, "a", authority.AuthorityId, endedTick + 1));
        var ended = Assert.Single(town.Government.OfficeHistory, term => term.Mandates == "non_land");
        Assert.Equal(endedTick, ended.EndTick);
        Assert.Single(town.Government.NonLandGrants);
        town.Reload().Validate();
    }

    [Fact]
    public void ProtectedRepealRevokesCurrentAuthorityWithoutErasingItsHistoricalProof()
    {
        var town = Town.WithExtension();
        var authority = TownGovernmentRules.CurrentNonLandAuthority(town.Government, town.Tick)!;
        var original = town.Government.Offices.Single(office => office.Mandates == "land");
        var replay = town.Reload();
        foreach (var current in new[] { town, replay })
        {
            current.Advance(25);
            var repeal = current.Propose(current.Government.Arrangement with { NonLand = TownArrangementRules.NoOffice });
            current.Yes(repeal, "a", "b");
            Assert.NotNull(TownGovernmentRules.CurrentNonLandAuthority(current.Government, 25));
            current.Yes(repeal, "c");
            Assert.Equal("completed", current.Change(repeal).Status);
            Assert.Null(TownGovernmentRules.CurrentNonLandAuthority(current.Government, 25));
            Assert.True(TownGovernmentRules.NonLandScopeAt(current.Government, 24));
            Assert.False(TownGovernmentRules.NonLandScopeAt(current.Government, 25));
            Assert.True(TownGovernmentRules.NonLandAuthorityAt(current.Government, "a", authority.AuthorityId, 24));
            // This historical allowance preserves earlier findings; current authority above is already revoked.
            Assert.True(TownGovernmentRules.NonLandAuthorityAt(current.Government, "a", authority.AuthorityId, 25));
            Assert.False(TownGovernmentRules.NonLandAuthorityAt(current.Government, "a", authority.AuthorityId, 26));
            Assert.Equal(original, Assert.Single(current.Government.Offices));
            current.Reload().Validate();
        }
        Assert.Equal(JsonSerializer.Serialize(town.Government), JsonSerializer.Serialize(replay.Government));
        Assert.Equal(JsonSerializer.Serialize(town.Council), JsonSerializer.Serialize(replay.Council));
    }

    [Fact]
    public void RestoringScopeNeedsFreshConsentAndDoesNotReviveTheEarlierAuthority()
    {
        var town = Town.WithExtension();
        var first = TownGovernmentRules.CurrentNonLandAuthority(town.Government, town.Tick)!;
        town.Advance(25);
        town.Yes(town.Propose(town.Government.Arrangement with { NonLand = TownArrangementRules.NoOffice }), "a", "b", "c");
        town.Advance(35);
        var second = town.ProposeExtension("land");
        town.Yes(second, "a", "b", "c");

        Assert.Null(TownGovernmentRules.CurrentNonLandAuthority(town.Government, 35));
        Assert.Throws<InvalidOperationException>(() => town.Accept(second, "a"));
        town.Read(second, "a");
        town.Accept(second, "a");
        town.Advance(35);
        var current = Assert.IsType<TownNonLandAuthority>(TownGovernmentRules.CurrentNonLandAuthority(town.Government, 35));
        Assert.NotEqual(first.AuthorityId, current.AuthorityId);
        Assert.Equal(10, current.TermStartTick);
        Assert.Equal(210, current.TermEndTick);
        Assert.Equal(35, current.EffectiveTick);
        Assert.True(TownGovernmentRules.NonLandAuthorityAt(town.Government, "a", first.AuthorityId, 24));
        Assert.False(TownGovernmentRules.NonLandAuthorityAt(town.Government, "a", first.AuthorityId, 35));
        Assert.False(TownGovernmentRules.NonLandAuthorityAt(town.Government, "a", current.AuthorityId, 24));
        Assert.True(TownGovernmentRules.NonLandAuthorityAt(town.Government, "a", current.AuthorityId, 35));
        Assert.Equal(2, town.Government.NonLandGrants.Count);
        town.Reload().Validate();
    }

    [Fact]
    public void ASeparateResidentElectionCanCreateANonLandMandateOnlyForItsWillingWinner()
    {
        var town = new Town();
        town.Register("a", "land");
        town.Register("b", "non_land");
        var target = new TownArrangement(TownArrangementRules.Council, TownArrangementRules.NoOffice, TownArrangementRules.Mayor);
        town.Yes(town.Propose(target), "a", "b", "c");
        Assert.Equal(["b"], town.Government.Contest!.Candidates);
        Assert.Null(TownGovernmentRules.CurrentNonLandAuthority(town.Government, 0));
        Assert.Throws<InvalidOperationException>(() => town.Ballot("a", "a"));
        town.Ballot("a", "b");
        town.Advance(10);

        var authority = Assert.IsType<TownNonLandAuthority>(TownGovernmentRules.CurrentNonLandAuthority(town.Government, 10));
        Assert.Equal("b", authority.HolderId);
        Assert.Equal(10, authority.EffectiveTick);
        Assert.Equal(10, authority.TermStartTick);
        Assert.Equal(210, authority.TermEndTick);
        Assert.Equal("non_land", Assert.Single(town.Government.Offices).Mandates);
        Assert.Empty(town.Government.NonLandGrants);
        Assert.False(TownGovernmentRules.NonLandAuthorityAt(town.Government, "b", authority.AuthorityId, 9));
        Assert.True(TownGovernmentRules.NonLandAuthorityAt(town.Government, "b", authority.AuthorityId, 10));
        town.Reload().Validate();
    }

    [Fact]
    public void AQueuedExtensionCannotReplaceTheNonLandOfficeAnEarlierChangeCreates()
    {
        var town = Town.Elect("land");
        var land = Assert.Single(town.Government.Offices);
        town.Advance(20);
        town.Register("a", "non_land");
        town.Register("b", "non_land");
        var firstTarget = town.Government.Arrangement with { NonLand = TownArrangementRules.Mayor };
        var first = town.Propose(firstTarget);
        town.Yes(first, "a", "b", "c");
        // An incumbent takes added duties through the named extension, rather than
        // using a fresh non-land election to restart the existing term.
        Assert.Equal(["b"], town.Government.Contest!.Candidates);
        var queued = town.Propose(firstTarget with { Ordinary = TownArrangementRules.AllAdultCouncil }, "land");
        Assert.Equal("queued", town.Change(queued).Status);
        Assert.Null(town.Change(queued).NonLandExtension!.ConsentTick);
        town.Ballot("b", "b");
        town.Advance(30);

        Assert.Equal("completed", town.Change(first).Status);
        Assert.Equal("cancelled", town.Change(queued).Status);
        Assert.Equal(firstTarget, town.Government.Arrangement);
        Assert.Throws<InvalidOperationException>(() => town.Accept(queued, "a"));
        var authority = Assert.IsType<TownNonLandAuthority>(TownGovernmentRules.CurrentNonLandAuthority(town.Government, 30));
        Assert.Equal("b", authority.HolderId);
        Assert.Equal(30, authority.TermStartTick);
        Assert.Equal(230, authority.TermEndTick);
        Assert.Equal(land, town.Government.Offices.Single(office => office.Mandates == "land"));
        Assert.Empty(town.Government.NonLandGrants);
        town.Advance(31);
        Assert.Equal(authority, TownGovernmentRules.CurrentNonLandAuthority(town.Government, 31));
        town.Reload().Validate();
    }

    [Theory]
    [InlineData("missing-consent")]
    [InlineData("missing-receipt")]
    [InlineData("other-holder")]
    [InlineData("wrong-base-election")]
    [InlineData("early-effective-time")]
    [InlineData("restarted-term")]
    [InlineData("orphaned-office")]
    public void SavedExtensionsRejectForgedAuthorityEvenWhenTheirResidentVoteIsGenuine(string damage)
    {
        var town = Town.WithExtension();
        var grant = Assert.Single(town.Government.NonLandGrants);
        var change = town.Change(grant.ChangeId);
        var extension = change.NonLandExtension!;
        var office = town.Government.Offices.Single(item => item.Mandates == "non_land");
        town = town.Reload();

        switch (damage)
        {
            case "missing-consent":
                town.ReplaceChange(change with { NonLandExtension = extension with { ConsentTick = null } });
                break;
            case "missing-receipt":
                town.Council = town.Council with { Knowledge = [] };
                break;
            case "other-holder":
                town.Read(change.Id, "b");
                town.ReplaceChange(change with { SuccessorId = "b", NonLandExtension = extension with { HolderId = "b" } });
                town.Government = town.Government with { NonLandGrants = [grant with { HolderId = "b" }] };
                town.ReplaceOffice(office with { HolderId = "b" });
                break;
            case "wrong-base-election":
                town.ReplaceChange(change with
                {
                    RequestKey = change.RequestKey.Replace(extension.BaseElectionId, grant.Id, StringComparison.Ordinal),
                    NonLandExtension = extension with { BaseElectionId = grant.Id }
                });
                town.Government = town.Government with { NonLandGrants = [grant with { BaseElectionId = grant.Id }] };
                break;
            case "early-effective-time":
                town.Government = town.Government with { NonLandGrants = [grant with { EffectiveTick = 19 }] };
                break;
            case "restarted-term":
                town.ReplaceChange(change with { NonLandExtension = extension with { TermStartTick = 20, TermEndTick = 220 } });
                town.Government = town.Government with { NonLandGrants = [grant with { TermStartTick = 20, TermEndTick = 220 }] };
                town.ReplaceOffice(office with { TermStartTick = 20, TermEndTick = 220 });
                break;
            case "orphaned-office":
                town.Government = town.Government with { NonLandGrants = [] };
                break;
        }

        Assert.Throws<InvalidDataException>(() => town.Validate());
    }

    private sealed class Town
    {
        private const string Id = "town:non-land-test";
        private static readonly string[] Adults = ["a", "b", "c", "d"];
        public TownGovernanceState Council { get; set; } = TownGovernanceState.Create(Adults);
        public TownGovernmentState Government { get; set; } = TownGovernmentState.Create();
        public long Tick { get; private set; }

        public static Town Elect(string mandate)
        {
            var town = new Town();
            town.Register("a", mandate);
            var target = mandate == "land"
                ? new TownArrangement(TownArrangementRules.Council, TownArrangementRules.Mayor)
                : new TownArrangement(TownArrangementRules.Mayor, TownArrangementRules.NoOffice);
            town.Yes(town.Propose(target), "a", "b", "c");
            town.Ballot("a", "a");
            town.Advance(10);
            return town;
        }

        public static Town WithExtension()
        {
            var town = Elect("land");
            town.Advance(20);
            var change = town.ProposeExtension("land");
            town.Read(change, "a");
            town.Accept(change, "a");
            town.Yes(change, "a", "b", "c");
            return town;
        }

        public void Advance(long tick)
        {
            Tick = tick;
            (Council, Government) = TownGovernmentRules.Advance(Council, Government, Id, "Test Town", "seed", Adults, tick, Day);
            Validate();
        }

        public void Validate()
        {
            var society = SocietyFixture.CreateGenesis("non-land-government", Adults.Select(id => SocietyFixture.CreateFounder(id, id))) with { WorldTick = Tick };
            var town = new TownRuntimeState(Id, "Test Town", "founded", 0, Adults, [], [], Governance: Council, Government: Government);
            TownGovernanceValidation.Validate(town, society, Day);
            TownGovernmentValidation.Validate(town, society, [], Day);
        }

        public string Propose(TownArrangement target, string? baseMandate = null)
        {
            (Council, Government) = TownGovernmentRules.Propose(Council, Government, Id, "a", target, false, Adults, Tick, Day, baseMandate);
            return Government.Changes[^1].Id;
        }

        public string ProposeExtension(string baseMandate) =>
            Propose(Government.Arrangement with { NonLand = TownArrangementRules.Mayor }, baseMandate);

        public TownGovernmentChange Change(string id) => Government.Changes.Single(change => change.Id == id);

        public void Read(string change, string actor)
        {
            var notice = Council.Notices.Last(item => item.Kind == "government" && item.SubjectId == change);
            Council = TownGovernanceRules.LearnNotice(Council, actor, notice.Id, Tick);
        }

        public void Accept(string change, string actor) =>
            (Council, Government) = TownGovernmentRules.AcceptNonLandDuties(Council, Government, change, actor, Adults, Tick);

        public void Yes(string change, params string[] voters)
        {
            foreach (var actor in voters) Government = TownGovernmentRules.Vote(Government, change, actor, true, Tick);
            Advance(Tick);
        }

        public void Register(string actor, string mandates) =>
            (Council, Government) = TownGovernmentRules.RegisterMayor(Council, Government, actor, mandates, Adults, Tick);

        public void Ballot(string actor, string candidate) =>
            Government = TownGovernmentRules.VoteMayor(Government, TownGovernmentRules.RoundToken(Government.Contest!), actor, candidate, Tick);

        public void ReplaceChange(TownGovernmentChange replacement) =>
            Government = Government with { Changes = Government.Changes.Select(change => change.Id == replacement.Id ? replacement : change).ToArray() };

        public void ReplaceOffice(TownOffice replacement) =>
            Government = Government with { Offices = Government.Offices.Select(office => office.Mandates == replacement.Mandates ? replacement : office).ToArray() };

        public Town Reload()
        {
            var replay = new Town
            {
                Tick = Tick,
                Council = JsonSerializer.Deserialize<TownGovernanceState>(JsonSerializer.Serialize(Council))!,
                Government = JsonSerializer.Deserialize<TownGovernmentState>(JsonSerializer.Serialize(Government))!
            };
            replay.Validate();
            return replay;
        }
    }
}
