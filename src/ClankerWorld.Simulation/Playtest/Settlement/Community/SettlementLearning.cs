using ClankerWorld.Simulation.Cognition;
using ClankerWorld.Simulation.Society;

namespace ClankerWorld.Simulation.Playtest;

public sealed record SettlementLesson(string TeacherId, SocietyWorkRole Role, string Stage,
    int Progress, long RequestedTick, long LastTransitionTick);

public sealed partial class PrivateWorldRuntime
{
    private const int LessonWorkRequired = 20;

    private static bool ActiveLesson(SettlementLesson? lesson) => lesson?.Stage is "requested" or "accepted" or "training";

    private bool ReadyForLesson(string actor) => inhabitants.TryGetValue(actor, out var person) &&
        !NeedsUrgentFood(person) && !NeedsUrgentWarmth(person);

    private bool CanMentor(string teacher, SocietyWorkRole role) =>
        inhabitants.ContainsKey(teacher) && !ActiveLesson(inhabitants[teacher].Lesson) &&
        society.Checkpoint.GetInhabitant(teacher) is { AgeBand: SocietyAgeBand.Adult or SocietyAgeBand.Elder } person &&
        (person.CurrentRole == role || person.CurrentRole == SocietyWorkRole.Teacher);

    private PlaytestInhabitantState? ActiveStudent(string teacher) => inhabitants.Values.FirstOrDefault(person =>
        person.Lesson is { Stage: "accepted" or "training" } lesson && lesson.TeacherId == teacher);

    private bool HasLearningDecision(string actor) => ReadyForLesson(actor) && inhabitants.Values.Any(person =>
        person.Lesson is { Stage: "requested" } lesson && lesson.TeacherId == actor);

    private bool CanContinueLesson(string actor)
    {
        if (!AdultResident(actor) || !ReadyForLesson(actor) || HasCouncilDecision(actor) || HasTradeResponse(actor) || HasFamilyDecision(actor) || HasParenthoodDecision(actor) || HasDependentCareDecision(actor))
        {
            return false;
        }
        if (inhabitants[actor].Lesson is { Stage: "accepted" or "training" } lesson)
        {
            return ReadyForLesson(lesson.TeacherId) && AdultResident(lesson.TeacherId);
        }
        return ActiveStudent(actor) is { } student && AdultResident(student.InhabitantId) && ReadyForLesson(student.InhabitantId);
    }

    private void MaintainLessons()
    {
        foreach (var person in inhabitants.Values.ToArray())
        {
            if (person.Lesson is not { } lesson || !ActiveLesson(lesson))
            {
                continue;
            }
            if (!AdultResident(person.InhabitantId) || !CanMentor(lesson.TeacherId, lesson.Role) ||
                WorldTick - lesson.RequestedTick > (lesson.Stage == "requested" ? 120 : 600))
            {
                SetLesson(person.InhabitantId, lesson with { Stage = "cancelled" });
            }
        }
    }

    private void AddLearningCandidates(List<CognitionCandidate> candidates, string actor)
    {
        if (!ReadyForLesson(actor) || survivalState is null)
        {
            return;
        }
        var person = inhabitants[actor];
        if (ActiveLesson(person.Lesson))
        {
            if (person.Lesson!.Stage is "accepted" or "training" && ReadyForLesson(person.Lesson.TeacherId))
            {
                candidates.Add(new("lesson_attend", "Attend the agreed practical lesson at camp.", 18));
            }
            candidates.Add(new("lesson_cancel", "Withdraw from the requested or ongoing lesson.", 110));
        }
        else if (ActiveStudent(actor) is { } student)
        {
            if (ReadyForLesson(student.InhabitantId))
            {
                candidates.Add(new("lesson_teach:" + student.InhabitantId, "Continue the agreed practical lesson.", 17));
            }
            candidates.Add(new("lesson_decline:" + student.InhabitantId, "Stop the lesson without changing the learner's role.", 110));
        }
        else
        {
            foreach (var request in inhabitants.Values.Where(item => item.Lesson is { Stage: "requested" } lesson && lesson.TeacherId == actor))
            {
                candidates.Add(new("lesson_accept:" + request.InhabitantId,
                    $"Teach {society.Checkpoint.GetInhabitant(request.InhabitantId).Name} the requested {request.Lesson!.Role} role.", 17));
                candidates.Add(new("lesson_decline:" + request.InhabitantId, "Decline this teaching request.", 70));
            }
            var identity = society.Checkpoint.GetInhabitant(actor);
            if (identity.AgeBand is SocietyAgeBand.Adult or SocietyAgeBand.Elder &&
                identity.CurrentRole is SocietyWorkRole.Unassigned or SocietyWorkRole.Trader &&
                (person.Lesson is null || WorldTick - person.Lesson.LastTransitionTick >= 300))
            {
                foreach (var role in new[] { SocietyWorkRole.Builder, SocietyWorkRole.Farmer })
                {
                    var mentor = inhabitants.Keys.Order(StringComparer.Ordinal).FirstOrDefault(id => id != actor && CanMentor(id, role) &&
                        !inhabitants.Values.Any(item => ActiveLesson(item.Lesson) && item.Lesson!.TeacherId == id));
                    if (mentor is not null)
                    {
                        candidates.Add(new($"learn:{role.ToString().ToLowerInvariant()}:{mentor}",
                            $"Ask {society.Checkpoint.GetInhabitant(mentor).Name} to teach the {role} role; they may refuse.", 35));
                    }
                }
            }
        }
    }

    private void ApplyLearningCandidate(string actor, string candidate)
    {
        if (!AdultResident(actor)) return;
        var person = inhabitants[actor];
        if (candidate.StartsWith("learn:", StringComparison.Ordinal))
        {
            var parts = candidate.Split(':', 3);
            if (parts.Length != 3 || !Enum.TryParse<SocietyWorkRole>(parts[1], true, out var role) ||
                role is not (SocietyWorkRole.Builder or SocietyWorkRole.Farmer) || parts[2] == actor ||
                ActiveLesson(person.Lesson) || !CanMentor(parts[2], role) ||
                inhabitants.Values.Any(item => ActiveLesson(item.Lesson) && item.Lesson!.TeacherId == parts[2]))
            {
                return;
            }
            SetLesson(actor, new(parts[2], role, "requested", 0, WorldTick, WorldTick));
            checkpointSchemaVersion = StateSchemaVersion;
            return;
        }
        if (candidate == "lesson_cancel")
        {
            if (person.Lesson is { } withdrawn && ActiveLesson(withdrawn))
                SetLesson(actor, withdrawn with { Stage = "cancelled" });
            return;
        }
        if (candidate == "lesson_attend" || candidate.StartsWith("lesson_teach:", StringComparison.Ordinal))
        {
            ContinueLesson(actor);
            return;
        }
        var accept = candidate.StartsWith("lesson_accept:", StringComparison.Ordinal);
        if (!accept && !candidate.StartsWith("lesson_decline:", StringComparison.Ordinal)) return;
        var studentId = candidate[(accept ? 14 : 15)..];
        if (!inhabitants.TryGetValue(studentId, out var student) || student.Lesson is not { } lesson ||
            lesson.TeacherId != actor || !ActiveLesson(lesson))
        {
            return;
        }
        if (!accept)
        {
            SetLesson(studentId, lesson with { Stage = "declined" });
        }
        else if (lesson.Stage == "requested" && CanMentor(actor, lesson.Role) && ActiveStudent(actor) is null)
        {
            SetLesson(studentId, lesson with { Stage = "accepted" });
            foreach (var participant in new[] { actor, studentId })
            {
                if (inhabitants[participant].Project is { Stage: not ("completed" or "cancelled") } project)
                {
                    SetProject(participant, project with { Stage = "paused", Blocker = "Taking part in an agreed lesson" });
                }
            }
        }
    }

    private void ContinueLesson(string actor)
    {
        if (!CanContinueLesson(actor))
        {
            return;
        }
        var person = inhabitants[actor];
        var camp = SettlementStoragePosition;
        if (!IsWithinInteractionRange(person.Position, camp, ResourceInteractionRange))
        {
            MoveToward(actor, person, camp, "lesson", ResourceInteractionRange);
            return;
        }
        if (ActiveStudent(actor) is not { } student || !IsWithinInteractionRange(student.Position, camp, ResourceInteractionRange))
        {
            return;
        }
        var previousLesson = student.Lesson!;
        var lesson = previousLesson with { Progress = previousLesson.Progress + 1, Stage = "training" };
        if (lesson.Progress >= LessonWorkRequired)
        {
            lesson = lesson with { Stage = "completed" };
            society.Apply(checkpoint => SocietyFixture.AssignRole(checkpoint, student.InhabitantId, lesson.Role));
            IncreaseTrust(student.InhabitantId, actor, 2, "teaching_completed");
            var memoryId = $"lesson-gratitude:{student.InhabitantId}:{actor}:{lesson.Role}";
            if (!society.Checkpoint.Memories.Any(memory => memory.Id == memoryId))
            {
                society.Apply(checkpoint => SocietyFixture.RecordSocialMemory(checkpoint, new(memoryId, student.InhabitantId, actor,
                    $"Learned the {lesson.Role} role with {checkpoint.GetInhabitant(actor).Name}.", "public", WorldTick)));
            }
        }
        SetLesson(student.InhabitantId, lesson);
    }

    private void SetLesson(string student, SettlementLesson lesson)
    {
        var person = inhabitants[student];
        if (person.Lesson?.Stage != lesson.Stage)
        {
            lesson = lesson with { LastTransitionTick = WorldTick };
            AppendEvent("lesson_" + lesson.Stage, student);
        }
        inhabitants[student] = person with { Lesson = lesson };
    }

    private static void ValidateLessons(PrivateWorldRuntimeState state)
    {
        var known = state.Society.Society.Inhabitants.Select(person => person.Id).ToHashSet(StringComparer.Ordinal);
        var active = state.Inhabitants.Where(person => ActiveLesson(person.Lesson)).Select(person => person.Lesson!.TeacherId).ToArray();
        if (active.Distinct(StringComparer.Ordinal).Count() != active.Length)
        {
            throw new InvalidDataException("A mentor cannot hold more than one active apprenticeship.");
        }
        foreach (var person in state.Inhabitants)
        {
            if (person.Lesson is { } lesson && (state.SchemaVersion < 8 || !known.Contains(lesson.TeacherId) ||
                lesson.TeacherId == person.InhabitantId || lesson.Role is not (SocietyWorkRole.Builder or SocietyWorkRole.Farmer) ||
                lesson.Stage is not ("requested" or "accepted" or "training" or "completed" or "cancelled" or "declined") ||
                lesson.Progress is < 0 or > LessonWorkRequired || lesson.RequestedTick < 0 ||
                lesson.Stage is "requested" or "accepted" && lesson.Progress != 0 ||
                lesson.Stage == "training" && lesson.Progress is <= 0 or >= LessonWorkRequired ||
                lesson.Stage == "completed" && lesson.Progress != LessonWorkRequired ||
                lesson.LastTransitionTick < lesson.RequestedTick || lesson.LastTransitionTick > state.Society.Society.WorldTick))
            {
                throw new InvalidDataException("The saved apprenticeship is invalid.");
            }
        }
    }
}
