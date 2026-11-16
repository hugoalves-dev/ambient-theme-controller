using AmbientTheme.Core.Theme;

namespace AmbientTheme.Core.Tests.Theme;

public sealed class FileThemeJournalTests : IDisposable
{
    private readonly string _path = Path.Combine(Path.GetTempPath(), $"atc-journal-{Guid.NewGuid():N}", "journal.json");

    public void Dispose()
    {
        var directory = Path.GetDirectoryName(_path)!;
        if (Directory.Exists(directory))
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public void Pending_mutation_survives_a_process_restart()
    {
        var mutation = new ThemeMutation("op-1", new ThemeSnapshot(1, 1), new ThemeSnapshot(0, 0));
        new FileThemeJournal(_path).Begin(mutation);

        Assert.Equal(mutation, new FileThemeJournal(_path).Pending);
    }

    [Fact]
    public void Completed_mutation_leaves_nothing_pending()
    {
        var journal = new FileThemeJournal(_path);
        journal.Begin(new ThemeMutation("op-1", new ThemeSnapshot(1, 1), new ThemeSnapshot(0, 0)));

        journal.Complete();

        Assert.Null(new FileThemeJournal(_path).Pending);
    }

    [Fact]
    public void Unreadable_journal_is_treated_as_nothing_pending()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
        File.WriteAllText(_path, "{ broken");

        Assert.Null(new FileThemeJournal(_path).Pending);
    }
}
