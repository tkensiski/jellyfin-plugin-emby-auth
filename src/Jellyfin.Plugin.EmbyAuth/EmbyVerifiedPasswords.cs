using System;
using System.Collections.Generic;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.EmbyAuth;

/// <summary>
/// Records which saved password hashes Emby verified, so that the plugin moves a user to the Default login method only with a password that came from Emby.
/// </summary>
/// <remarks>
/// The file stores a SHA-256 fingerprint of each hash, never the hash itself.
/// </remarks>
public sealed partial class EmbyVerifiedPasswords
{
    private readonly string _filePath;
    private readonly ILogger<EmbyVerifiedPasswords> _logger;
    private readonly Lock _lock = new();
    private Dictionary<Guid, string>? _fingerprints;

    /// <summary>
    /// Initializes a new instance of the <see cref="EmbyVerifiedPasswords"/> class.
    /// </summary>
    /// <param name="filePath">The file that holds the records.</param>
    /// <param name="logger">The logger.</param>
    public EmbyVerifiedPasswords(string filePath, ILogger<EmbyVerifiedPasswords> logger)
    {
        _filePath = filePath;
        _logger = logger;
    }

    /// <summary>
    /// Records that Emby verified the password that has this hash. If the file cannot be read, this call logs an error and loses the record. If the file can be read but not written, the record is kept in memory and this user still moves; only the on-disk copy is behind, and it is lost only if Jellyfin restarts before the next successful write.
    /// </summary>
    /// <param name="userId">The Jellyfin user ID.</param>
    /// <param name="passwordHash">The saved password hash.</param>
    public void Record(Guid userId, string passwordHash)
    {
        ArgumentException.ThrowIfNullOrEmpty(passwordHash);
        var fingerprint = Fingerprint(passwordHash);
        lock (_lock)
        {
            var fingerprints = Load();
            if (fingerprints is null)
            {
                return;
            }

            if (fingerprints.TryGetValue(userId, out var existing) && existing == fingerprint)
            {
                return;
            }

            fingerprints[userId] = fingerprint;
            try
            {
                var temporaryPath = _filePath + ".tmp";
                File.WriteAllText(temporaryPath, JsonSerializer.Serialize(fingerprints));
                File.Move(temporaryPath, _filePath, overwrite: true);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                LogWriteFailed(_logger, ex, _filePath);
            }
        }
    }

    /// <summary>
    /// Checks whether Emby verified the password that has this hash. Returns <c>false</c> for every user while the file cannot be read.
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
        lock (_lock)
        {
            return Load() is { } fingerprints && fingerprints.TryGetValue(userId, out var recorded) && recorded == fingerprint;
        }
    }

    /// <summary>
    /// Checks whether the fingerprint file could be read on this call.
    /// </summary>
    /// <remarks>
    /// A <c>false</c> result means the file could not be read on this call only; the next call reads the file again,
    /// so the result is not sticky. Because <see cref="Load"/> caches a successful read and retries after a failed
    /// one, this method is cheap: call it once per request, never once per user.
    /// </remarks>
    /// <returns><c>true</c> if the file was read successfully, including when it is absent; <c>false</c> if the read failed.</returns>
    public bool RecordsAvailable()
    {
        lock (_lock)
        {
            return Load() is not null;
        }
    }

    private static string Fingerprint(string passwordHash) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(passwordHash)));

    /// <summary>
    /// Reads the fingerprint file, caching the result. Returns <c>null</c>, and does not touch the cache, when the read fails —
    /// so a caller writes nothing and keeps nothing, and the next call reads the file again.
    /// </summary>
    private Dictionary<Guid, string>? Load()
    {
        if (_fingerprints is not null)
        {
            return _fingerprints;
        }

        if (!File.Exists(_filePath))
        {
            _fingerprints = [];
            return _fingerprints;
        }

        try
        {
            _fingerprints = JsonSerializer.Deserialize<Dictionary<Guid, string>>(File.ReadAllText(_filePath)) ?? [];
        }
        catch (Exception ex) when (ex is JsonException or IOException or UnauthorizedAccessException)
        {
            LogReadFailed(_logger, ex, _filePath);
            return null;
        }

        return _fingerprints;
    }

    [LoggerMessage(Level = LogLevel.Error, Message = "Jellyfin cannot read {FilePath}. The plugin records no verified password and moves no user to the Default login method while the read fails. It keeps the records that are in the file and reads the file again on the next login.")]
    private static partial void LogReadFailed(ILogger logger, Exception exception, string filePath);

    [LoggerMessage(Level = LogLevel.Error, Message = "Jellyfin cannot write {FilePath}. The plugin keeps the record in memory and still moves this user; the record on disk is behind until the next successful write, and is lost only if Jellyfin restarts before one happens.")]
    private static partial void LogWriteFailed(ILogger logger, Exception exception, string filePath);
}
