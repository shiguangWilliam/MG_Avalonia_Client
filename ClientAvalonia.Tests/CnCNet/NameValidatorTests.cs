using System;
using ClientAvalonia.CnCNet;
using ClientAvalonia.Tests.Fixture;
using FluentAssertions;
using Xunit;

namespace ClientAvalonia.Tests.CnCNet;

/// <summary>
/// DX-aligned: ClientCore NameValidator is shared by DX and Avalonia — character set, length,
/// first-character rules must match exactly. <see cref="TempGameRoot"/> sets MaxNameLength=16
/// (DX default) so we don't have to touch the singleton.
///
/// Serial because we mutate <c>ProgramConstants._hostedGamePathOverride</c> (process-wide static).
/// </summary>
[Collection("ProgramConstantsSerial")]
public sealed class NameValidatorTests : IDisposable
{
    private readonly TempGameRoot _root = new();

    public NameValidatorTests()
    {
        _root.BindToProgramConstants();
    }

    public void Dispose() => _root.Dispose();

    [Fact]
    [Trait("DXContract", "DX-NAME-VALIDATOR")]
    public void EmptyName_IsRejected_AsEmpty()
    {
        NameValidationError error = NameValidator.IsNameValid("", out _);
        error.Should().Be(NameValidationError.EmptyName);
    }

    [Fact]
    [Trait("DXContract", "DX-NAME-VALIDATOR")]
    public void NameStartingWithDigit_IsRejected_AsFirstCharacterIsNumber()
    {
        NameValidationError error = NameValidator.IsNameValid("1Alpha", out _);
        error.Should().Be(NameValidationError.FirstCharacterIsNumber);
    }

    [Fact]
    [Trait("DXContract", "DX-NAME-VALIDATOR")]
    public void NameStartingWithHyphen_IsRejected_AsFirstCharacterIsHyphen()
    {
        NameValidationError error = NameValidator.IsNameValid("-Hyphen", out _);
        error.Should().Be(NameValidationError.FirstCharacterIsHyphen);
    }

    [Fact]
    [Trait("DXContract", "DX-NAME-VALIDATOR")]
    public void NameWithDisallowedChars_IsRejected_AsInvalidCharacters()
    {
        // Space, @, ! are NOT in the allowed set per DX.
        NameValidator.IsNameValid("Has Space", out _).Should().Be(NameValidationError.InvalidCharacters);
        NameValidator.IsNameValid("Bad@Name", out _).Should().Be(NameValidationError.InvalidCharacters);
        NameValidator.IsNameValid("Bad!Name", out _).Should().Be(NameValidationError.InvalidCharacters);
    }

    [Fact]
    [Trait("DXContract", "DX-NAME-VALIDATOR")]
    public void NameWithinAllowedSet_IsAccepted()
    {
        // Per NameValidator: A-Z a-z 0-9 and -_[]|\\{}^`
        // Keep names ≤ MaxNameLength=16 (DX default).
        NameValidator.IsNameValid("ValidName", out _).Should().Be(NameValidationError.None);
        NameValidator.IsNameValid("A[1]B", out _).Should().Be(NameValidationError.None);
        NameValidator.IsNameValid("Name`X`", out _).Should().Be(NameValidationError.None);
    }

    [Fact]
    [Trait("DXContract", "DX-NAME-VALIDATOR")]
    public void NameExceedingMaxNameLength_IsRejected_AsTooLong()
    {
        // MaxNameLength = 16 in our fixture ClientDefinitions.ini (DX default).
        string tooLong = new string('a', 17);
        NameValidator.IsNameValid(tooLong, out _).Should().Be(NameValidationError.TooLong);
    }

    [Fact]
    [Trait("DXContract", "DX-NAME-VALIDATOR")]
    public void NameAtExactlyMaxNameLength_IsAccepted()
    {
        string atLimit = new string('a', 16);
        NameValidator.IsNameValid(atLimit, out _).Should().Be(NameValidationError.None);
    }

    [Fact]
    public void GetValidOfflineName_StripsCommaAndSemicolon_AndTruncatesToMax()
    {
        string sanitized = NameValidator.GetValidOfflineName("a,b;c");
        sanitized.Should().Be("abc");

        string truncated = NameValidator.GetValidOfflineName(new string('a', 20));
        truncated.Should().HaveLength(16);
    }

    [Fact]
    public void GetSanitizedGameName_RemovesSemicolons_AndTrims()
    {
        // "My ; Game ;" → strip ; → "My  Game " → .Trim() → "My  Game"
        NameValidator.GetSanitizedGameName("My ; Game ;").Should().Be("My  Game");
    }

    [Fact]
    public void IsGameNameValid_RejectsEmpty()
    {
        NameValidator.IsGameNameValid("", out _).Should().Be(NameValidationError.EmptyName);
    }

    [Fact]
    public void IsGameNameValid_AcceptsNormal()
    {
        NameValidator.IsGameNameValid("My Game", out _).Should().Be(NameValidationError.None);
    }

    [Fact]
    public void GetLocalizedPlayerNameErrorMessage_ReturnsText_ForKnownErrors()
    {
        NameValidator.GetLocalizedPlayerNameErrorMessage(NameValidationError.None).Should().BeNull();
        NameValidator.GetLocalizedPlayerNameErrorMessage(NameValidationError.EmptyName).Should().NotBeNullOrEmpty();
        NameValidator.GetLocalizedPlayerNameErrorMessage(NameValidationError.TooLong).Should().NotBeNullOrEmpty();
    }
}
