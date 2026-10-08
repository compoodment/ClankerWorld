using System.Reflection;
using System.Text.Json;
using ClankerWorld.Simulation.Cognition;
using ClankerWorld.Simulation.Playtest;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace ClankerWorld.Simulation.Tests;

public sealed class RuntimeStateBoundaryTests
{
    private static readonly string[] LifetimeNames = ["live", "saved", "scratch"];
    private static string Root => RuntimeWriterScan.RepositoryRoot;
    private static Dictionary<string, string[]> Classification => JsonSerializer.Deserialize<Dictionary<string, string[]>>(
        File.ReadAllText(Path.Combine(Root, "tests/ClankerWorld.Simulation.Tests/Architecture/runtime-fields.json")))!;

    [Fact]
    public void EveryInstanceFieldHasExactlyOneStateLifetime()
    {
        var categories = Classification;
        Assert.Equal(LifetimeNames, categories.Keys.Order(StringComparer.Ordinal));
        var classified = categories.Values.SelectMany(names => names).Order(StringComparer.Ordinal).ToArray();
        Assert.Equal(classified.Distinct(StringComparer.Ordinal).Count(), classified.Length);
        var fields = typeof(PrivateWorldRuntime).GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
            .Select(field => field.Name).Order(StringComparer.Ordinal).ToArray();
        Assert.Equal(fields, classified);
    }

    [Fact]
    public void CommitTransfersEverySavedFieldAndNoLiveOrScratchField()
    {
        var source = File.ReadAllText(Path.Combine(Root,
            "src/ClankerWorld.Simulation/Playtest/Runtime/PrivateWorldRuntime.Tick.cs"));
        var method = CSharpSyntaxTree.ParseText(source).GetRoot().DescendantNodes().OfType<MethodDeclarationSyntax>()
            .Single(item => item.Identifier.ValueText == "CommitPreparedTick");
        var transfers = new List<string>();
        foreach (var assignment in method.DescendantNodes().OfType<AssignmentExpressionSyntax>())
        {
            if (assignment.Left is IdentifierNameSyntax left && assignment.Right is MemberAccessExpressionSyntax right &&
                right.Expression is IdentifierNameSyntax { Identifier.ValueText: "proposed" } &&
                left.Identifier.ValueText == right.Name.Identifier.ValueText)
                transfers.Add(left.Identifier.ValueText);
            else if (assignment.Left is TupleExpressionSyntax swapLeft && assignment.Right is TupleExpressionSyntax swapRight &&
                swapLeft.ToString() == "(society, proposed.society)" && swapRight.ToString() == "(proposed.society, society)")
                transfers.Add("society");
            else
                Assert.Fail("Unrecognized commit transfer: " + assignment);
        }
        Assert.Equal(Classification["saved"].Order(StringComparer.Ordinal), transfers.Order(StringComparer.Ordinal));
    }

    [Fact]
    public async Task CommittedRuntimeEncodesExactlyThePreparedRuntime()
    {
        using var committed = NormalPathWorld.CreateGenerated("runtime-commit-old", _ => new DeterministicDecisionProvider());
        using var prepared = NormalPathWorld.CreateGenerated("runtime-commit-prepared", _ => new DeterministicDecisionProvider());
        Assert.True((await prepared.AdvanceOneTickAsync()).Advanced);
        Assert.True((await prepared.AdvanceOneTickAsync()).Advanced);
        var expected = PrivateWorldRuntimeCodec.Encode(prepared.ExportState());
        var expectedFields = Classification["saved"].ToDictionary(name => name,
            name => typeof(PrivateWorldRuntime).GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(prepared));
        var commit = typeof(PrivateWorldRuntime).GetMethod("CommitPreparedTick", BindingFlags.Instance | BindingFlags.NonPublic)!;
        commit.Invoke(committed, [prepared]);
        Assert.Equal(expected, PrivateWorldRuntimeCodec.Encode(committed.ExportState()));
        foreach (var (name, value) in expectedFields)
            Assert.Equal(value, typeof(PrivateWorldRuntime).GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(committed));
        committed.Validate();
    }
}
