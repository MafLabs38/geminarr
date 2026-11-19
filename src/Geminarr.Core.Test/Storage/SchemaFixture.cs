using Geminarr.Core.Storage;
using Geminarr.Core.Test.Access;
using Geminarr.Core.Test.Support;
using Microsoft.Data.Sqlite;

namespace Geminarr.Core.Test.Storage;

public sealed class SchemaFixture : IDisposable
{
    private readonly TestDatabase _workspace = new();

    public void Dispose() => _workspace.Dispose();

    [Fact]
    public void should_create_every_table_at_the_first_open_and_remember_the_version()
    {
        using var connection = _workspace.Database.Open();

        Schema.CurrentVersion(connection).Should().Be(Schema.Version);
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT name FROM sqlite_master WHERE type = 'table' ORDER BY name;";
        using var reader = command.ExecuteReader();
        var tables = new List<string>();
        while (reader.Read())
        {
            tables.Add(reader.GetString(0));
        }

        tables.Should().Contain(["connection", "conversations", "destination", "outbox", "runs"]);
    }

    [Fact]
    public void should_keep_the_row_of_the_connection_when_a_database_of_the_first_step_is_upgraded()
    {
        using (var connection = _workspace.Database.Open())
        {
            // A database as the first step left it: the columns of the next ones are not there yet.
            foreach (var column in new[] { "sign_out_pending", "paused_until", "before_state", "before_reason", "before_failures" })
            {
                Run(connection, $"ALTER TABLE connection DROP COLUMN {column};");
            }

            Run(connection, "INSERT INTO connection (id, state, since, consecutive_failures, last_reason) VALUES (1, 'NeedsLogin', '2026-11-13T09:00:00.0000000+00:00', 2, 'needs-login');");
            Run(connection, "PRAGMA user_version = 1;");
        }

        using var upgraded = new Database(Options.Of(_workspace.Settings)).Open();

        Schema.CurrentVersion(upgraded).Should().Be(Schema.Version);
        using var command = upgraded.CreateCommand();
        command.CommandText = "SELECT state, consecutive_failures, paused_until, before_state, sign_out_pending FROM connection WHERE id = 1;";
        using var reader = command.ExecuteReader();
        reader.Read().Should().BeTrue();
        (reader.GetString(0), reader.GetInt32(1), reader.IsDBNull(2), reader.IsDBNull(3), reader.GetInt32(4)).Should().Be(("NeedsLogin", 2, true, true, 0));
    }

    private static void Run(SqliteConnection connection, string sql)
    {
        using var command = connection.CreateCommand();
        command.CommandText = sql;
        command.ExecuteNonQuery();
    }

    [Fact]
    public void should_apply_a_step_once_only()
    {
        using (var first = _workspace.Database.Open())
        {
            Schema.Apply(first);
            Schema.Apply(first);
        }

        using var second = new Database(Options.Of(_workspace.Settings)).Open();
        Schema.CurrentVersion(second).Should().Be(Schema.Version);
    }

    [Fact]
    public void should_refuse_a_database_written_by_a_newer_version()
    {
        using (var connection = _workspace.Database.Open())
        {
            using var command = connection.CreateCommand();
            command.CommandText = $"PRAGMA user_version = {Schema.Version + 1};";
            command.ExecuteNonQuery();
        }

        var open = () => new Database(Options.Of(_workspace.Settings)).Open();

        open.Should().Throw<UnsupportedSchemaException>().Which.Message.Should().Contain("schema version");
        StorageErrors.IsUnavailable(new UnsupportedSchemaException(2, 1)).Should().BeTrue();
    }

    [Fact]
    public void should_not_create_the_data_folder_of_a_volume_that_was_not_mounted()
    {
        var missing = Path.Combine(_workspace.Folder, "not-mounted");

        var open = () => new Database(Options.Of(new Geminarr.Core.Configuration.GeminarrOptions { DataFolder = missing })).Open();

        open.Should().Throw<DirectoryNotFoundException>();
        Directory.Exists(missing).Should().BeFalse();
        StorageErrors.IsUnavailable(new DirectoryNotFoundException()).Should().BeTrue();
        StorageErrors.IsUnavailable(new SqliteException("x", 1)).Should().BeTrue();
        StorageErrors.IsUnavailable(new InvalidOperationException()).Should().BeFalse();
    }

    [Fact]
    public void should_make_the_file_readable_by_its_owner_only()
    {
        if (OperatingSystem.IsWindows())
        {
            return;
        }

        using var connection = _workspace.Database.Open();

        File.GetUnixFileMode(Path.Combine(_workspace.Folder, Database.FileName)).Should().Be(UnixFileMode.UserRead | UnixFileMode.UserWrite);
    }

    [Fact]
    public void should_overwrite_what_is_deleted_and_not_only_forget_it()
    {
        using var connection = _workspace.Database.Open();
        using var command = connection.CreateCommand();
        command.CommandText = "PRAGMA secure_delete;";

        Convert.ToInt32(command.ExecuteScalar(), System.Globalization.CultureInfo.InvariantCulture).Should().Be(1);
    }
}
