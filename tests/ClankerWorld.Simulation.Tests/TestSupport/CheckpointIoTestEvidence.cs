using System.Runtime.ExceptionServices;
using System.Runtime.InteropServices;
using System.Security.AccessControl;
using System.Text.Json;

namespace ClankerWorld.Simulation.Tests;

/// <summary>Observes only synchronous checkpoint I/O in a newly generated, disposable test directory.</summary>
internal sealed class CheckpointIoTestEvidence : IDisposable
{
    internal const string ArtifactRootVariable = "CLANKERWORLD_CHECKPOINT_DIAGNOSTICS";
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };
    private readonly Action<string> output;
    private readonly string? artifactRoot;
    private readonly bool expectedFailureControl;
    private bool completed;
    private int attempt;

    public CheckpointIoTestEvidence(string prefix, Action<string> output, string? artifactRoot = null,
        bool expectedFailureControl = false)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(prefix);
        if (prefix.Any(character => !char.IsAsciiLetterOrDigit(character) && character != '-'))
            throw new ArgumentException("A test directory prefix must contain only letters, digits and hyphens.", nameof(prefix));
        DirectoryPath = Path.Combine(Path.GetTempPath(), $"{prefix}{Guid.NewGuid():N}");
        this.output = output;
        this.artifactRoot = artifactRoot ?? Environment.GetEnvironmentVariable(ArtifactRootVariable);
        this.expectedFailureControl = expectedFailureControl;
    }

    public string DirectoryPath { get; }
    public string CheckpointPath => Path.Combine(DirectoryPath, "world.json");
    public string? EvidenceDirectory { get; private set; }
    public Exception? FirstChanceFailure { get; private set; }
    public Exception? EscapingFailure { get; private set; }

    public T Observe<T>(string operation, long? tick, Func<T> action)
    {
        attempt++;
        var captured = false;
        FirstChanceFailure = null;
        EscapingFailure = null;
        EvidenceDirectory = null;
        var thread = Environment.CurrentManagedThreadId;
        void FirstChance(object? sender, FirstChanceExceptionEventArgs args)
        {
            if (captured || Environment.CurrentManagedThreadId != thread ||
                args.Exception is not (IOException or UnauthorizedAccessException)) return;
            // Set this before any diagnostics: their own I/O failures also raise FirstChanceException.
            captured = true;
            FirstChanceFailure = args.Exception;
            BestEffort(() => CaptureFirstChance(operation, tick, args.Exception));
        }

        AppDomain.CurrentDomain.FirstChanceException += FirstChance;
        try { return action(); }
        catch (Exception exception)
        {
            EscapingFailure = exception;
            // A first-chance exception can have been handled inside the operation. Record the
            // escaping exception separately, before rethrowing the very same failure.
            BestEffort(() => WriteMetadata("escaping-exception.json", new
            {
                Operation = operation,
                WorldTick = tick,
                ExpectedFailureControl = expectedFailureControl,
                SameAsFirstChance = ReferenceEquals(exception, FirstChanceFailure),
                Exception = Describe(exception),
            }));
            throw;
        }
        finally { AppDomain.CurrentDomain.FirstChanceException -= FirstChance; }
    }

    private void CaptureFirstChance(string operation, long? tick, Exception exception)
    {
        var files = new List<object>();
        if (!string.IsNullOrWhiteSpace(artifactRoot))
        {
            BestEffort(() =>
            {
                var root = Path.GetFullPath(artifactRoot);
                var destination = Path.Combine(root, Path.GetFileName(DirectoryPath), $"attempt-{attempt:D6}");
                // Never copy a fixture into itself, including an output directory below it.
                var relative = Path.GetRelativePath(DirectoryPath, destination);
                if (relative == "." || !Path.IsPathRooted(relative) && relative != ".." &&
                    !relative.StartsWith(".." + Path.DirectorySeparatorChar, StringComparison.Ordinal))
                    throw new IOException("Checkpoint evidence must be outside its synthetic fixture directory.");
                Directory.CreateDirectory(destination);
                EvidenceDirectory = destination;
            });
        }

        // Probe the exact destination even when it appears absent: File.Exists suppresses
        // errors that could explain a denied overwrite or a pending deletion.
        CaptureFile(DirectoryPath, files);
        CaptureFile(CheckpointPath, files);
        BestEffort(() =>
        {
            foreach (var temporary in Directory.EnumerateFiles(DirectoryPath, ".world.json.*.tmp"))
                CaptureFile(temporary, files);
        });
        var history = CheckpointPath + ".history";
        CaptureFile(history, files);
        BestEffort(() =>
        {
            var attributes = File.GetAttributes(history);
            if ((attributes & FileAttributes.ReparsePoint) != 0) return;
            foreach (var segment in Directory.EnumerateFiles(history))
                CaptureFile(segment, files);
        });
        WriteMetadata("first-chance.json", new
        {
            Operation = operation,
            WorldTick = tick,
            ExpectedFailureControl = expectedFailureControl,
            TimestampUtc = DateTimeOffset.UtcNow,
            ProcessId = Environment.ProcessId,
            ThreadId = Environment.CurrentManagedThreadId,
            Framework = RuntimeInformation.FrameworkDescription,
            OperatingSystem = RuntimeInformation.OSDescription,
            Architecture = RuntimeInformation.ProcessArchitecture.ToString(),
            SourceRevision = Environment.GetEnvironmentVariable("GITHUB_SHA"),
            DirectoryPath,
            CheckpointPath,
            Exception = Describe(exception),
            ThrowContext = Environment.StackTrace,
            Files = files,
        });
    }

    private void CaptureFile(string path, List<object> files)
    {
        string? attributes = null;
        long? length = null;
        DateTime? writtenUtc = null;
        object? metadataFailure = null;
        object? copyFailure = null;
        object? permissionsFailure = null;
        string? permissions = null;
        string? copiedPath = null;
        var isOrdinaryFile = false;
        try
        {
            var flags = File.GetAttributes(path);
            attributes = flags.ToString();
            isOrdinaryFile = (flags & (FileAttributes.ReparsePoint | FileAttributes.Directory)) == 0;
            if (isOrdinaryFile)
            {
                var info = new FileInfo(path);
                length = info.Length;
                writtenUtc = info.LastWriteTimeUtc;
            }
        }
        catch (Exception exception) { metadataFailure = Describe(exception); }
        if (OperatingSystem.IsWindows())
        {
            try
            {
                const AccessControlSections sections = AccessControlSections.Access | AccessControlSections.Owner | AccessControlSections.Group;
                FileSystemSecurity security = attributes?.Contains(nameof(FileAttributes.Directory), StringComparison.Ordinal) == true
                    ? new DirectoryInfo(path).GetAccessControl(sections)
                    : new FileInfo(path).GetAccessControl(sections);
                permissions = security.GetSecurityDescriptorSddlForm(sections);
            }
            catch (Exception exception) { permissionsFailure = Describe(exception); }
        }
        // Failure to read one file must not prevent capturing the closed temporary file.
        if (isOrdinaryFile && EvidenceDirectory is { } evidence)
        {
            try
            {
                copiedPath = Path.Combine(evidence, "files", Path.GetRelativePath(DirectoryPath, path));
                Directory.CreateDirectory(Path.GetDirectoryName(copiedPath)!);
                File.Copy(path, copiedPath);
            }
            catch (Exception exception) { copyFailure = Describe(exception); }
        }
        files.Add(new
        {
            Path = path,
            Attributes = attributes,
            Length = length,
            WrittenUtc = writtenUtc,
            MetadataFailure = metadataFailure,
            Permissions = permissions,
            PermissionsFailure = permissionsFailure,
            CopyFailure = copyFailure,
            CopiedPath = copiedPath
        });
    }

    private static object Describe(Exception exception) => new
    {
        Type = exception.GetType().FullName,
        HResult = $"0x{exception.HResult:X8}",
        exception.Message,
        Detail = exception.ToString(),
    };

    private void WriteMetadata(string name, object value)
    {
        var text = JsonSerializer.Serialize(value, JsonOptions);
        // Test output remains useful when artifact copying was not requested or failed.
        BestEffort(() => output($"Checkpoint I/O {name}: {text}"));
        if (EvidenceDirectory is { } evidence) File.WriteAllText(Path.Combine(evidence, name), text);
    }

    private void BestEffort(Action action)
    {
        try { action(); }
        catch (Exception exception)
        {
            try { output($"Checkpoint diagnostic failed: {exception.GetType().Name} {exception.HResult:X8}: {exception.Message}"); }
            catch (Exception) { /* Evidence must never replace the original checkpoint failure. */ }
        }
    }

    public void Complete() => completed = true;

    public void Dispose()
    {
        if (!completed)
        {
            BestEffort(() => output($"Failed synthetic checkpoint directory retained: {DirectoryPath}"));
            return;
        }
        // A successful test still reports a real cleanup failure. A failed test never
        // enters cleanup, so deletion cannot mask the checkpoint exception.
        if (Directory.Exists(DirectoryPath)) Directory.Delete(DirectoryPath, recursive: true);
    }
}
