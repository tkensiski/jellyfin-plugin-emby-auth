using System;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.EmbyAuth;

/// <summary>
/// Records which saved password hashes Emby verified, so that the plugin moves a user to the Default login method only with a password that came from Emby.
/// </summary>
/// <remarks>
/// Records live in a plugin-owned SQLite database, storing a SHA-256 fingerprint of each hash, never the hash
/// itself. Each <see cref="Record"/> call commits its own row durably before returning. <see cref="Matches"/>
/// and <see cref="RecordsAvailable"/> take no lock of the plugin's own, so a read never waits on a write.
/// </remarks>
public sealed partial class EmbyVerifiedPasswords
{
    private const int SchemaVersion = 1;

    private readonly string _databasePath;
    private readonly string _connectionString;
    private readonly ILogger<EmbyVerifiedPasswords> _logger;
    private volatile bool _initialized;

    /// <summary>
    /// Initializes a new instance of the <see cref="EmbyVerifiedPasswords"/> class.
    /// </summary>
    /// <param name="databasePath">The path to the SQLite database that holds the records.</param>
    /// <param name="logger">The logger.</param>
    public EmbyVerifiedPasswords(string databasePath, ILogger<EmbyVerifiedPasswords> logger)
    {
        _databasePath = databasePath;
        _logger = logger;
        _connectionString = new SqliteConnectionStringBuilder
        {
            DataSource = databasePath,
            Mode = SqliteOpenMode.ReadWriteCreate,
            Pooling = true,
            DefaultTimeout = 5,
        }.ToString();
        EnsureInitialized();
    }

    /// <summary>
    /// Records that Emby verified the password that has this hash. The row commits durably before this call
    /// returns. A write failure is logged and never escapes as an exception.
    /// </summary>
    /// <param name="userId">The Jellyfin user ID.</param>
    /// <param name="passwordHash">The saved password hash.</param>
    public void Record(Guid userId, string passwordHash)
    {
        ArgumentException.ThrowIfNullOrEmpty(passwordHash);
        var fingerprint = Fingerprint(passwordHash);
        if (!EnsureInitialized())
        {
            return;
        }

        try
        {
            using var connection = Open();
            using var command = connection.CreateCommand();
            command.CommandText = "INSERT INTO VerifiedPasswords (UserId, Fingerprint) VALUES ($userId, $fingerprint) ON CONFLICT(UserId) DO UPDATE SET Fingerprint = excluded.Fingerprint;";
            command.Parameters.AddWithValue("$userId", userId.ToString());
            command.Parameters.AddWithValue("$fingerprint", fingerprint);
            command.ExecuteNonQuery();
        }
        catch (SqliteException ex)
        {
            LogWriteFailed(_logger, ex, _databasePath);
        }
    }

    /// <summary>
    /// Checks whether Emby verified the password that has this hash. Returns <c>false</c> for every user while
    /// the database cannot be read.
    /// </summary>
    /// <param name="userId">The Jellyfin user ID.</param>
    /// <param name="passwordHash">The saved password hash.</param>
    /// <returns><c>true</c> if a record exists for this user and hash.</returns>
    public bool Matches(Guid userId, string? passwordHash)
    {
        if (string.IsNullOrEmpty(passwordHash))
        {
            return false;
        }

        var fingerprint = Fingerprint(passwordHash);
        try
        {
            using var connection = Open();
            using var command = connection.CreateCommand();
            command.CommandText = "SELECT EXISTS(SELECT 1 FROM VerifiedPasswords WHERE UserId = $userId AND Fingerprint = $fingerprint);";
            command.Parameters.AddWithValue("$userId", userId.ToString());
            command.Parameters.AddWithValue("$fingerprint", fingerprint);
            return command.ExecuteScalar() is long matchCount && matchCount == 1;
        }
        catch (SqliteException ex)
        {
            LogReadFailed(_logger, ex, _databasePath);
            return false;
        }
    }

    /// <summary>
    /// Checks whether the record database can be read on this call.
    /// </summary>
    /// <returns><c>true</c> if the database was read successfully; <c>false</c> if the read failed.</returns>
    public bool RecordsAvailable()
    {
        try
        {
            using var connection = Open();
            using var command = connection.CreateCommand();
            command.CommandText = "SELECT COUNT(*) FROM VerifiedPasswords;";
            _ = command.ExecuteScalar();
            return true;
        }
        catch (SqliteException ex)
        {
            LogReadFailed(_logger, ex, _databasePath);
            return false;
        }
    }

    private static string Fingerprint(string passwordHash) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(passwordHash)));

    /// <summary>
    /// Opens a new connection to the record database.
    /// </summary>
    /// <returns>The open connection. The caller disposes it.</returns>
    private SqliteConnection Open()
    {
        var connection = new SqliteConnection(_connectionString);
        connection.Open();
        return connection;
    }

    /// <summary>
    /// Creates the database and its schema on first use. Safe to call concurrently: creating the directory,
    /// setting WAL mode, and creating the table are all idempotent, so a second concurrent call is harmless and
    /// this method takes no lock.
    /// </summary>
    /// <returns><c>true</c> if the database is ready; <c>false</c> if it could not be opened or created.</returns>
    private bool EnsureInitialized()
    {
        if (_initialized)
        {
            return true;
        }

        try
        {
            var directory = Path.GetDirectoryName(_databasePath);
            if (!string.IsNullOrEmpty(directory))
            {
                Directory.CreateDirectory(directory);
            }

            using var connection = Open();
            using (var walCommand = connection.CreateCommand())
            {
                walCommand.CommandText = "PRAGMA journal_mode=WAL;";
                _ = walCommand.ExecuteScalar();
            }

            using (var createTableCommand = connection.CreateCommand())
            {
                createTableCommand.CommandText = "CREATE TABLE IF NOT EXISTS VerifiedPasswords (UserId TEXT NOT NULL PRIMARY KEY, Fingerprint TEXT NOT NULL);";
                createTableCommand.ExecuteNonQuery();
            }

            using (var schemaVersionCommand = connection.CreateCommand())
            {
                // PRAGMA does not accept a bound parameter. Justification for CA2100: SchemaVersion is a
                // compile-time int constant, never user input.
#pragma warning disable CA2100
                schemaVersionCommand.CommandText = FormattableString.Invariant($"PRAGMA user_version = {SchemaVersion};");
#pragma warning restore CA2100
                schemaVersionCommand.ExecuteNonQuery();
            }

            _initialized = true;
            return true;
        }
        catch (Exception ex) when (ex is SqliteException or IOException or UnauthorizedAccessException)
        {
            LogStoreUnavailable(_logger, ex, _databasePath);
            return false;
        }
    }

    [LoggerMessage(Level = LogLevel.Error, Message = "Jellyfin cannot read the verified-password records in {DatabasePath}. It reports every user as needing an Emby login until the read succeeds.")]
    private static partial void LogReadFailed(ILogger logger, Exception exception, string databasePath);

    [LoggerMessage(Level = LogLevel.Error, Message = "Jellyfin cannot write the verified-password record to {DatabasePath}. The record was not saved; the user must log in through Emby again.")]
    private static partial void LogWriteFailed(ILogger logger, Exception exception, string databasePath);

    [LoggerMessage(Level = LogLevel.Error, Message = "The plugin cannot open or create its record database at {DatabasePath}.")]
    private static partial void LogStoreUnavailable(ILogger logger, Exception exception, string databasePath);
}
