namespace ClankerWorld.Simulation.Playtest;

/// <summary>A Council vote authorizes one specific report; it does not decide its truth.</summary>
public static class TownNonviolentGovernmentFilingRules
{
    public static string RequestKey(TownViolationFilingRequest request) => "law_case:" +
        TownHearingProcedure.Digest(new { request.Allegation.IncidentId, request.Allegation.LawId, request.Allegation.LawVersion });

    public static bool IsValid(TownViolationFilingRequest? request, long tick) =>
        request is not null && TownNonviolentRules.ValidAllegation(request.Allegation) &&
        request.Allegation.ConductTick <= tick && TownHearingProcedure.Text(request.Statement) &&
        request.Allegation.SourceEvidenceIds.Distinct(StringComparer.Ordinal).Count() == request.Allegation.SourceEvidenceIds.Count;

    public static string Describe(TownViolationFilingRequest request) =>
        $"Report {request.Allegation.ConductKind} by {request.Allegation.SubjectId} at " +
        $"({request.Allegation.Position.X}, {request.Allegation.Position.Y}), tick {request.Allegation.ConductTick}, " +
        $"under law {request.Allegation.LawId} version {request.Allegation.LawVersion}. " + request.Statement;
}
