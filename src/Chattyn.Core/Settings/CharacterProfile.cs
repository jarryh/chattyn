using System.Text.Json.Serialization;

namespace Chattyn.Core.Settings;

public enum CharacterGender
{
    Unspecified,
    She,
    He,
    They,
}

/// <summary>
/// One roleplay character. Every field is optional and may be written in the writer's own
/// language; the description shapes the tone of the natural version.
/// </summary>
public sealed class CharacterProfile
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string Name { get; set; } = "";
    public CharacterGender Gender { get; set; }
    public string RaceAndClass { get; set; } = "";

    /// <summary>How the character talks: formality, vocabulary, dialect, typical manner.</summary>
    public string SpeechStyle { get; set; } = "";

    /// <summary>Anything else: background, temperament, relationships.</summary>
    public string Notes { get; set; } = "";

    [JsonIgnore]
    public string DisplayName => string.IsNullOrWhiteSpace(Name) ? "Unnamed character" : Name.Trim();

    public CharacterProfile Clone() => new()
    {
        Id = Id,
        Name = Name,
        Gender = Gender,
        RaceAndClass = RaceAndClass,
        SpeechStyle = SpeechStyle,
        Notes = Notes,
    };
}
