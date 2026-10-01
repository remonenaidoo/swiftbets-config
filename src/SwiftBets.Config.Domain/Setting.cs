using SwiftBets.Contracts.Config;
using SwiftBets.Contracts.Results;

namespace SwiftBets.Config.Domain;

/// <summary>One setting as stored: its value in force, a version that only increases, and who last changed it and why.</summary>
public sealed record Setting(string Key, string Value, long Version, string ChangedBy, string Reason, DateTimeOffset ChangedAt)
{
    public const int MaxReasonLength = 400;

    /// <summary>
    /// The next version of a setting, or why it cannot change. Setting the value it already has is not a change, so it
    /// is answered with the current setting and nothing is published.
    /// </summary>
    public static Result<SettingChange> Change(Setting? current, string key, string value, string actor, string? reason, DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(key);
        ArgumentNullException.ThrowIfNull(value);
        if (string.IsNullOrWhiteSpace(reason) || reason.Trim().Length > MaxReasonLength)
        {
            return Result.Failure<SettingChange>(ConfigErrors.ReasonRequired);
        }

        if (ConfigKeys.Validate(key, value) is { } problem)
        {
            return Result.Failure<SettingChange>(ConfigErrors.InvalidValue(problem));
        }

        if (current is not null && current.Value == value)
        {
            return Result.Success(new SettingChange(current, Changed: false));
        }

        return Result.Success(new SettingChange(new Setting(key, value, (current?.Version ?? 0) + 1, actor, reason.Trim(), now), Changed: true));
    }
}

public sealed record SettingChange(Setting Setting, bool Changed);
