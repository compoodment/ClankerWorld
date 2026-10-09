using System.Reflection;
using ClankerWorld.Simulation.Playtest;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace ClankerWorld.Simulation.Tests;

internal static class RuntimeWriterScan
{
    public static string RepositoryRoot
    {
        get
        {
            for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory is not null; directory = directory.Parent)
                if (File.Exists(Path.Combine(directory.FullName, "ClankerWorld.sln"))) return directory.FullName;
            throw new DirectoryNotFoundException("Could not locate the ClankerWorld repository root.");
        }
    }

    public static SortedDictionary<string, string[]> FindWriters(string root)
    {
        var properties = typeof(PlaytestInhabitantState).GetProperties(BindingFlags.Instance | BindingFlags.Public)
            .Select(property => property.Name).ToArray();
        var writers = properties.Append("$collection").ToDictionary(name => name,
            _ => new SortedSet<string>(StringComparer.Ordinal), StringComparer.Ordinal);
        var source = Path.Combine(root, "src/ClankerWorld.Simulation");
        var paths = Directory.EnumerateFiles(source, "*.cs", SearchOption.AllDirectories)
            .Where(path => !Path.GetRelativePath(source, path).Split(Path.DirectorySeparatorChar).Any(part => part is "obj" or "bin"))
            .Append(Path.Combine(root, "src/ClankerWorld.Shared/AgentPlacementRules.cs"));
        var trees = paths.Select(path => CSharpSyntaxTree.ParseText(File.ReadAllText(path), path: path)).ToList();
        trees.Add(CSharpSyntaxTree.ParseText("global using System; global using System.Collections.Generic; " +
            "global using System.IO; global using System.Linq; global using System.Net.Http; " +
            "global using System.Threading; global using System.Threading.Tasks;"));
        var references = ((string)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES")!).Split(Path.PathSeparator)
            .Select(path => MetadataReference.CreateFromFile(path));
        var compilation = CSharpCompilation.Create("RuntimeWriterGuard", trees, references,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
        // GeneratedRegex implementations are emitted by the real build, outside this
        // source scan. All other binding failures must fail closed rather than hide a writer.
        var errors = compilation.GetDiagnostics().Where(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error &&
            !IsGeneratedRegexDeclaration(diagnostic)).Take(10).ToArray();
        if (errors.Length > 0) throw new InvalidDataException(string.Join("\n", errors.Select(error => error.ToString())));
        foreach (var tree in trees.Where(tree => tree.FilePath.Length > 0))
        {
            var model = compilation.GetSemanticModel(tree);
            var filename = Path.GetRelativePath(root, tree.FilePath).Replace(Path.DirectorySeparatorChar, '/');
            foreach (var node in tree.GetRoot().DescendantNodes())
            {
                if (node is WithExpressionSyntax with && IsInhabitant(model.GetTypeInfo(with.Expression).Type))
                    foreach (var assignment in with.Initializer.Expressions.OfType<AssignmentExpressionSyntax>())
                        if (model.GetSymbolInfo(assignment.Left).Symbol is IPropertySymbol property && writers.TryGetValue(property.Name, out var files))
                            files.Add(filename);
                // Construction writes every component, including defaults; moving construction
                // into a new file must be reviewed just like adding a new component update.
                if (node is BaseObjectCreationExpressionSyntax creation && IsInhabitant(model.GetTypeInfo(creation).Type))
                    foreach (var property in properties) writers[property].Add(filename);
                if (node is AssignmentExpressionSyntax write)
                {
                    if (model.GetSymbolInfo(write.Left).Symbol is IPropertySymbol property && IsInhabitant(property.ContainingType))
                        writers[property.Name].Add(filename);
                    if (write.Left is ElementAccessExpressionSyntax element && IsInhabitantDictionary(model.GetTypeInfo(element.Expression).Type) ||
                        model.GetSymbolInfo(write.Left).Symbol is IFieldSymbol { Name: "inhabitants", ContainingType.Name: "PrivateWorldRuntime" })
                        writers["$collection"].Add(filename);
                }
                if (node is InvocationExpressionSyntax call && call.Expression is MemberAccessExpressionSyntax access &&
                    access.Name.Identifier.ValueText is "Add" or "Remove" or "Clear" or "TryAdd" &&
                    IsInhabitantDictionary(model.GetTypeInfo(access.Expression).Type))
                    writers["$collection"].Add(filename);
            }
        }
        return new SortedDictionary<string, string[]>(writers.ToDictionary(pair => pair.Key, pair => pair.Value.ToArray()), StringComparer.Ordinal);
    }

    private static bool IsInhabitant(ITypeSymbol? type) => type?.ToDisplayString() == "ClankerWorld.Simulation.Playtest.PlaytestInhabitantState";
    private static bool IsGeneratedRegexDeclaration(Diagnostic diagnostic) => diagnostic.Id == "CS8795" &&
        diagnostic.Location.SourceTree?.GetRoot().FindNode(diagnostic.Location.SourceSpan)
            .AncestorsAndSelf().OfType<MethodDeclarationSyntax>().FirstOrDefault()?.AttributeLists
            .SelectMany(list => list.Attributes).Any(attribute => attribute.Name.ToString() == "GeneratedRegex") == true;
    private static bool IsInhabitantDictionary(ITypeSymbol? type) => type is INamedTypeSymbol { Name: "Dictionary", TypeArguments.Length: 2 } dictionary &&
        IsInhabitant(dictionary.TypeArguments[1]);
}
