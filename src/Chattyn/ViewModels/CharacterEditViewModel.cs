using Chattyn.Core.Settings;
using CommunityToolkit.Mvvm.ComponentModel;

namespace Chattyn.ViewModels;

/// <summary>One character being edited in Settings; becomes a <see cref="CharacterProfile"/> on Save.</summary>
public sealed partial class CharacterEditViewModel : ObservableObject
{
    public CharacterEditViewModel(CharacterProfile profile)
    {
        Id = profile.Id;
        _name = profile.Name;
        _gender = profile.Gender;
        _raceAndClass = profile.RaceAndClass;
        _speechStyle = profile.SpeechStyle;
        _notes = profile.Notes;
    }

    public string Id { get; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(DisplayName))]
    private string _name;

    [ObservableProperty] private CharacterGender _gender;
    [ObservableProperty] private string _raceAndClass;
    [ObservableProperty] private string _speechStyle;
    [ObservableProperty] private string _notes;

    /// <summary>Shown in the character list; follows the name while it is typed.</summary>
    public string DisplayName => ToProfile().DisplayName;

    public CharacterProfile ToProfile() => new()
    {
        Id = Id,
        Name = Name.Trim(),
        Gender = Gender,
        RaceAndClass = RaceAndClass.Trim(),
        SpeechStyle = SpeechStyle.Trim(),
        Notes = Notes.Trim(),
    };
}
