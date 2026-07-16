using ClientAvalonia.Rendering;
using ClientCore;
using Rampastring.Tools;

namespace ClientAvalonia.IniUi.Binding;

/// <summary>Load/save audio tab (DX <c>AudioOptionsPanel</c> Load/Save).</summary>
public static class AudioOptionsApplier
{
    private const int VolumeScale = 10;

    public static void Apply(UiNodeViewModel? optionsRoot)
    {
        if (optionsRoot == null)
            return;

        UserINISettings ini = UserINISettings.Instance;
        SetTrackbar(optionsRoot, "trbScoreVolume", "lblScoreVolumeValue", ini.ScoreVolume);
        SetTrackbar(optionsRoot, "trbSoundVolume", "lblSoundVolumeValue", ini.SoundVolume);
        SetTrackbar(optionsRoot, "trbVoiceVolume", "lblVoiceVolumeValue", ini.VoiceVolume);
        SetTrackbar(optionsRoot, "trbClientVolume", "lblClientVolumeValue", ini.ClientVolume);

        SetCheck(optionsRoot, "chkScoreShuffle", ini.IsScoreShuffle);
        SetCheck(optionsRoot, "chkMainMenuMusic", ini.PlayMainMenuMusic);
        SetCheck(optionsRoot, "chkStopMusicOnMenu", ini.StopMusicOnMenu);
        SetCheck(optionsRoot, "chkStopGameLobbyMessageAudio", ini.StopGameLobbyMessageAudio);

        WireVolumeLabels(optionsRoot);
    }

    public static void Save(UiNodeViewModel? optionsRoot)
    {
        if (optionsRoot == null)
            return;

        UserINISettings ini = UserINISettings.Instance;
        ini.ScoreVolume.Value = ReadTrackbar(optionsRoot, "trbScoreVolume") / (double)VolumeScale;
        ini.SoundVolume.Value = ReadTrackbar(optionsRoot, "trbSoundVolume") / (double)VolumeScale;
        ini.VoiceVolume.Value = ReadTrackbar(optionsRoot, "trbVoiceVolume") / (double)VolumeScale;
        ini.ClientVolume.Value = ReadTrackbar(optionsRoot, "trbClientVolume") / (double)VolumeScale;

        ini.IsScoreShuffle.Value = ReadCheck(optionsRoot, "chkScoreShuffle", ini.IsScoreShuffle);
        ini.PlayMainMenuMusic.Value = ReadCheck(optionsRoot, "chkMainMenuMusic", ini.PlayMainMenuMusic);
        ini.StopMusicOnMenu.Value = ReadCheck(optionsRoot, "chkStopMusicOnMenu", ini.StopMusicOnMenu);
        ini.StopGameLobbyMessageAudio.Value = ReadCheck(
            optionsRoot,
            "chkStopGameLobbyMessageAudio",
            ini.StopGameLobbyMessageAudio);

        Logger.Log(
            $"AudioOptionsApplier: saved music={ini.ScoreVolume.Value:F2}, sound={ini.SoundVolume.Value:F2}, " +
            $"voice={ini.VoiceVolume.Value:F2}, client={ini.ClientVolume.Value:F2}");
    }

    private static void WireVolumeLabels(UiNodeViewModel root)
    {
        WirePair(root, "trbScoreVolume", "lblScoreVolumeValue");
        WirePair(root, "trbSoundVolume", "lblSoundVolumeValue");
        WirePair(root, "trbVoiceVolume", "lblVoiceVolumeValue");
        WirePair(root, "trbClientVolume", "lblClientVolumeValue");
    }

    private static void WirePair(UiNodeViewModel root, string trackId, string labelId)
    {
        UiNodeViewModel? track = Find(root, trackId);
        UiNodeViewModel? label = Find(root, labelId);
        if (track == null || label == null)
            return;

        void Sync() => label.SetDisplayText(track.SelectedIndex.ToString());
        track.SelectionChanged -= Sync;
        track.SelectionChanged += Sync;
        Sync();
    }

    private static void SetTrackbar(UiNodeViewModel root, string id, string labelId, double volume01)
    {
        int value = (int)Math.Clamp(Math.Round(volume01 * VolumeScale), 0, VolumeScale);
        UiNodeViewModel? track = Find(root, id);
        track?.SetSelectedIndexSilent(value);
        Find(root, labelId)?.SetDisplayText(value.ToString());
    }

    private static int ReadTrackbar(UiNodeViewModel root, string id)
    {
        UiNodeViewModel? track = Find(root, id);
        if (track == null)
            return 7;

        return Math.Clamp(track.SelectedIndex, 0, VolumeScale);
    }

    private static void SetCheck(UiNodeViewModel root, string id, bool value)
        => Find(root, id)?.SetIsCheckedSilent(value);

    private static bool ReadCheck(UiNodeViewModel root, string id, bool fallback)
        => Find(root, id)?.IsChecked ?? fallback;

    private static UiNodeViewModel? Find(UiNodeViewModel root, string id)
    {
        if (root.Id.Equals(id, StringComparison.OrdinalIgnoreCase))
            return root;

        foreach (UiNodeViewModel child in root.Children)
        {
            UiNodeViewModel? found = Find(child, id);
            if (found != null)
                return found;
        }

        return null;
    }
}
