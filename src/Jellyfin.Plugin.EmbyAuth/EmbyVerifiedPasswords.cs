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
    /// Records that Emby verified the password that has this hash. If the file cannot be written, the method logs an error and the record is lost.
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
    /// Checks whether Emby verified the password that has this hash.
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
            return Load().TryGetValue(userId, out var recorded) && recorded == fingerprint;
        }
    }

    private static string Fingerprint(string passwordHash) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(passwordHash)));

    private Dictionary<Guid, string> Load()
    {
        if (_fingerprints is not null)
        {
            return _fingerprints;
        }

        _fingerprints = [];
        if (!File.Exists(_filePath))
        {
            return _fingerprints;
        }

        try
        {
            _fingerprints = JsonSerializer.Deserialize<Dictionary<Guid, string>>(File.ReadAllText(_filePath)) ?? [];
        }
        catch (Exception ex) when (ex is JsonException or IOException or UnauthorizedAccessException)
        {
            LogReadFailed(_logger, ex, _filePath);
        }

        return _fingerprints;
    }

    [LoggerMessage(Level = LogLevel.Error, Message = "Jellyfin cannot read {FilePath}. The plugin moves no user to the Default login method until users log in again through Emby. The next record replaces the file.")]
    private static partial void LogReadFailed(ILogger logger, Exception exception, string filePath);

    [LoggerMessage(Level = LogLevel.Error, Message = "Jellyfin cannot write {FilePath}. The plugin does not move this user to the Default login method until the user logs in again through Emby.")]
    private static partial void LogWriteFailed(ILogger logger, Exception exception, string filePath);
}
