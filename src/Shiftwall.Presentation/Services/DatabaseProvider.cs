using Shiftwall.Core.Data;

namespace Shiftwall.Presentation.Services;

/// <summary>
/// Provides the model database: the copy imported by the user (if valid) or the one embedded in the exe.
/// An imported database is validated with the same rules as the bundled one before being accepted.
/// </summary>
public sealed class DatabaseProvider(string userDirectory)
{
    private static readonly string[] Files = ["models.json", "fortios-releases.json", "upgrade-paths.json"];

    public static string DefaultDirectory => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Shiftwall", "database");

    public bool UsingImported { get; private set; }

    public ModelDatabase Load()
    {
        if (Files.All(f => File.Exists(Path.Combine(userDirectory, f))))
        {
            try
            {
                var db = ModelDatabase.LoadFromDirectory(userDirectory);
                UsingImported = true;
                return db;
            }
            catch (Exception ex) when (ex is DatabaseException or IOException or UnauthorizedAccessException)
            {
                // Invalid import: fall back to the bundled database.
            }
        }

        UsingImported = false;
        return ModelDatabase.LoadEmbedded();
    }

    /// <summary>
    /// Imports a database from the folder containing <paramref name="modelsJsonPath"/> (the three JSON files must be
    /// side by side). Throws <see cref="DatabaseException"/> with every validation error if the files are not valid.
    /// </summary>
    public ModelDatabase Import(string modelsJsonPath)
    {
        var dir = Path.GetDirectoryName(Path.GetFullPath(modelsJsonPath))!;
        var missing = Files.Where(f => !File.Exists(Path.Combine(dir, f))).ToList();
        if (missing.Count > 0)
        {
            throw new DatabaseException("Missing files next to models.json: " + string.Join(", ", missing), []);
        }

        var db = ModelDatabase.LoadFromDirectory(dir); // validates
        Directory.CreateDirectory(userDirectory);
        foreach (var f in Files) File.Copy(Path.Combine(dir, f), Path.Combine(userDirectory, f), overwrite: true);
        UsingImported = true;
        return db;
    }

    /// <summary>Removes the imported database and returns to the bundled one.</summary>
    public ModelDatabase Reset()
    {
        foreach (var f in Files)
        {
            var p = Path.Combine(userDirectory, f);
            if (File.Exists(p)) File.Delete(p);
        }

        UsingImported = false;
        return ModelDatabase.LoadEmbedded();
    }
}
