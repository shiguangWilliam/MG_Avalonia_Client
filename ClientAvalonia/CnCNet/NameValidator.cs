using System;
using System.Linq;
using ClientCore;
using ClientCore.Extensions;
using ClientAvalonia.GlobalState;

namespace ClientAvalonia.CnCNet;

public enum NameValidationError
{
    None = 0,
    EmptyName,
    OffensiveName,
    FirstCharacterIsNumber,
    FirstCharacterIsHyphen,
    InvalidCharacters,
    TooLong,
}

public static class NameValidator
{
    public static string? GetLocalizedPlayerNameErrorMessage(NameValidationError error)
    {
        return error switch
        {
            NameValidationError.None => null,
            NameValidationError.EmptyName => "Please enter a name.".L10N("Client:ClientCore:EnterAName"),
            NameValidationError.OffensiveName => "Please enter a name that is less offensive.".L10N("Client:ClientCore:NameOffensive"),
            NameValidationError.FirstCharacterIsNumber => "The first character in the player name cannot be a number.".L10N("Client:ClientCore:NameFirstIsNumber"),
            NameValidationError.FirstCharacterIsHyphen => "The first character in the player name cannot be a hyphen ( - ).".L10N("Client:ClientCore:NameFirstIsHyphen"),
            NameValidationError.InvalidCharacters => "Your player name has invalid characters in it.".L10N("Client:ClientCore:NameInvalidChar1") + Environment.NewLine +
                                                     "Allowed characters are anything from A to Z and numbers.".L10N("Client:ClientCore:NameInvalidChar2"),
            NameValidationError.TooLong => "Your nickname is too long.".L10N("Client:ClientCore:NameTooLong"),
            _ => null,
        };
    }

    public static string? GetLocalizedGameNameErrorMessage(NameValidationError error)
    {
        return error switch
        {
            NameValidationError.None => null,
            NameValidationError.EmptyName => "Please enter a game name.".L10N("Client:Main:PleaseEnterGameName"),
            NameValidationError.OffensiveName => "Please enter a less offensive game name.".L10N("Client:Main:GameNameOffensiveText"),
            _ => null,
        };
    }

    public static NameValidationError IsNameValid(string name, out string? localizedErrorMessage)
    {
        var profanityFilter = new ProfanityFilter();

        if (string.IsNullOrEmpty(name))
        {
            localizedErrorMessage = GetLocalizedPlayerNameErrorMessage(NameValidationError.EmptyName);
            return NameValidationError.EmptyName;
        }

        if (profanityFilter.IsOffensive(name))
        {
            localizedErrorMessage = GetLocalizedPlayerNameErrorMessage(NameValidationError.OffensiveName);
            return NameValidationError.OffensiveName;
        }

        if (int.TryParse(name[..1], out _))
        {
            localizedErrorMessage = GetLocalizedPlayerNameErrorMessage(NameValidationError.FirstCharacterIsNumber);
            return NameValidationError.FirstCharacterIsNumber;
        }

        if (name[0] == '-')
        {
            localizedErrorMessage = GetLocalizedPlayerNameErrorMessage(NameValidationError.FirstCharacterIsHyphen);
            return NameValidationError.FirstCharacterIsHyphen;
        }

        char[] allowedCharacters = "abcdefghijklmnopqrstuvwxyzABCDEFGHIJKLMNOPQRSTUVWXYZ0123456789-_[]|\\{}^`".ToCharArray();
        foreach (char nickChar in name)
        {
            if (!allowedCharacters.Contains(nickChar))
            {
                localizedErrorMessage = GetLocalizedPlayerNameErrorMessage(NameValidationError.InvalidCharacters);
                return NameValidationError.InvalidCharacters;
            }
        }

        if (name.Length > AppState.Configuration.Legacy.MaxNameLength)
        {
            localizedErrorMessage = GetLocalizedPlayerNameErrorMessage(NameValidationError.TooLong);
            return NameValidationError.TooLong;
        }

        localizedErrorMessage = null;
        return NameValidationError.None;
    }

    public static string GetValidOfflineName(string name)
    {
        char[] disallowedCharacters = [',', ';'];
        string validName = new(name.Trim().Where(c => !disallowedCharacters.Contains(c)).ToArray());
        int maxLength = AppState.Configuration.Legacy.MaxNameLength;

        if (maxLength > 0 && validName.Length > maxLength)
            return validName[..maxLength];

        return validName;
    }

    public static NameValidationError IsGameNameValid(string name, out string? localizedErrorMessage)
    {
        var profanityFilter = new ProfanityFilter();

        if (string.IsNullOrEmpty(name))
        {
            localizedErrorMessage = GetLocalizedGameNameErrorMessage(NameValidationError.EmptyName);
            return NameValidationError.EmptyName;
        }

        if (profanityFilter.IsOffensive(name))
        {
            localizedErrorMessage = GetLocalizedGameNameErrorMessage(NameValidationError.OffensiveName);
            return NameValidationError.OffensiveName;
        }

        localizedErrorMessage = null;
        return NameValidationError.None;
    }

    public static string GetSanitizedGameName(string name)
        => name.Replace(";", string.Empty).Trim();
}
