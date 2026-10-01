using SwiftBets.Contracts.Errors;

namespace SwiftBets.Config.Domain;

public static class ConfigErrors
{
    public static readonly Error ReasonRequired = Error.Validation("reason_required", "Say why the setting changes (up to 400 characters).");

    public static readonly Error NotFound = Error.NotFound("setting_not_found", "No such setting.");

    public static Error InvalidValue(string problem) => Error.Validation("invalid_value", problem);
}
