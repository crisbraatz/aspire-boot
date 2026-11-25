namespace AspireBoot.Tests.Unit.Architecture;

[Trait("Category", "Unit")]
public sealed class NoPasswordApisTests
{
    private static readonly string[] ForbiddenMembers =
    [
        "PasswordSignInAsync",
        "CheckPasswordSignInAsync",
        "AddPasswordAsync",
        "PasswordHash",
    ];

    private static readonly string[] ScannedFolders = ["src/AspireBoot.Api", "src/AspireBoot.Core"];

    [Fact]
    public void ApiAndCoreSources_DoNotUsePasswordApis()
    {
        var root = FindRepositoryRoot();

        var offenders = ScannedFolders
            .SelectMany(folder => Directory.EnumerateFiles(Path.Combine(root, folder), "*.cs", SearchOption.AllDirectories))
            .Where(file => !IsExcluded(file))
            .Where(file =>
            {
                var text = File.ReadAllText(file);
                return ForbiddenMembers.Any(member => text.Contains(member, StringComparison.Ordinal));
            })
            .Select(file => Path.GetRelativePath(root, file))
            .ToArray();

        Assert.Empty(offenders);
    }

    private static bool IsExcluded(string file)
    {
        var segments = file.Split(Path.DirectorySeparatorChar);
        return segments.Any(segment => segment is "bin" or "obj" or "Migrations");
    }

    private static string FindRepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "AspireBoot.slnx")))
        {
            directory = directory.Parent;
        }

        return directory?.FullName ?? throw new InvalidOperationException("Repository root (AspireBoot.slnx) not found.");
    }
}