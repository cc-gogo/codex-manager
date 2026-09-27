using Microsoft.Data.Sqlite;

namespace CodexConversationManager.Core.LocalData;

public static class SqliteSnapshot
{
    public static bool IsDatabase(string path) =>
        path.EndsWith(".sqlite", StringComparison.OrdinalIgnoreCase) ||
        path.EndsWith(".db", StringComparison.OrdinalIgnoreCase);

    public static void Copy(string sourcePath, string destinationPath)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(destinationPath))!);
        if (File.Exists(destinationPath)) File.Delete(destinationPath);
        using var source = new SqliteConnection(new SqliteConnectionStringBuilder
        {
            DataSource = sourcePath, Mode = SqliteOpenMode.ReadOnly, Pooling = false
        }.ToString());
        using var destination = new SqliteConnection(new SqliteConnectionStringBuilder
        {
            DataSource = destinationPath, Mode = SqliteOpenMode.ReadWriteCreate, Pooling = false
        }.ToString());
        source.Open();
        destination.Open();
        source.BackupDatabase(destination);
    }

    public static void Restore(string backupPath, string originalPath)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(originalPath))!);
        using var source = new SqliteConnection(new SqliteConnectionStringBuilder
        {
            DataSource = backupPath, Mode = SqliteOpenMode.ReadOnly, Pooling = false
        }.ToString());
        using var destination = new SqliteConnection(new SqliteConnectionStringBuilder
        {
            DataSource = originalPath, Mode = SqliteOpenMode.ReadWriteCreate, Pooling = false
        }.ToString());
        source.Open();
        destination.Open();
        source.BackupDatabase(destination);
    }
}
