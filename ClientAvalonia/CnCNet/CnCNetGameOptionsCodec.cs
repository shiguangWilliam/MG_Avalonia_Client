using ClientCore;
using Rampastring.Tools;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace ClientAvalonia.CnCNet;

/// <summary>Builds and parses GO CTCP bodies (XNA CnCNetGameLobby).</summary>
public static class CnCNetGameOptionsCodec
{
    public static string BuildBody(CnCNetGameOptionsState state)
    {
        int checkBoxCount = state.CheckBoxValues.Count;
        int checkBoxIntegerCount = (checkBoxCount / 32) + 1;

        bool[] optionValues = state.CheckBoxValues.ToArray();
        List<byte> byteList = Conversions.BoolArrayIntoBytes(optionValues).ToList();
        while (byteList.Count % 4 != 0)
            byteList.Add(0);

        byte[] byteArray = byteList.ToArray();
        var sb = new StringBuilder();

        for (int i = 0; i < checkBoxIntegerCount; i++)
        {
            if (sb.Length > 0)
                sb.Append(';');

            sb.Append(BitConverter.ToInt32(byteArray, i * 4));
        }

        foreach (int dropDownIndex in state.DropDownIndices)
        {
            sb.Append(';');
            sb.Append(dropDownIndex);
        }

        sb.Append(';').Append(Convert.ToInt32(state.MapOfficial));
        sb.Append(';').Append(state.MapSha1);
        sb.Append(';').Append(state.GameModeName);
        sb.Append(';').Append(state.FrameSendRate);
        sb.Append(';').Append(state.MaxAhead);
        sb.Append(';').Append(state.ProtocolVersion);
        sb.Append(';').Append(state.RandomSeed);
        sb.Append(';').Append(Convert.ToInt32(state.RemoveStartingLocations));
        sb.Append(';').Append(state.MapUntranslatedName);

        return sb.ToString();
    }

    public static bool TryParseBody(
        string message,
        int checkBoxCount,
        int dropDownCount,
        out CnCNetGameOptionsState? state,
        out string? error)
    {
        state = null;
        error = null;

        string[] parts = message.Split(';');
        int checkBoxIntegerCount = (checkBoxCount / 32) + 1;
        int partIndex = checkBoxIntegerCount + dropDownCount;

        if (parts.Length < partIndex + 6)
        {
            error = "invalid game options message length";
            return false;
        }

        var checkBoxValues = new List<bool>(checkBoxCount);
        for (int i = 0; i < checkBoxCount; i++)
            checkBoxValues.Add(false);

        for (int i = 0; i < checkBoxIntegerCount; i++)
        {
            if (!int.TryParse(parts[i], out int checkBoxStatusInt))
            {
                error = "failed to parse checkbox options";
                return false;
            }

            byte[] bytes = BitConverter.GetBytes(checkBoxStatusInt);
            bool[] boolArray = Conversions.BytesIntoBoolArray(bytes);

            for (int optionIndex = 0; optionIndex < boolArray.Length; optionIndex++)
            {
                int gameOptionIndex = i * 32 + optionIndex;
                if (gameOptionIndex >= checkBoxCount)
                    break;

                checkBoxValues[gameOptionIndex] = boolArray[optionIndex];
            }
        }

        var dropDownIndices = new List<int>(dropDownCount);
        for (int i = checkBoxIntegerCount; i < checkBoxIntegerCount + dropDownCount; i++)
        {
            if (!int.TryParse(parts[i], out int ddIndex))
            {
                error = "failed to parse dropdown options";
                return false;
            }

            dropDownIndices.Add(ddIndex);
        }

        bool mapOfficial = Conversions.BooleanFromString(parts[partIndex], true);
        string mapSha1 = parts[partIndex + 1];
        string gameModeName = parts[partIndex + 2];
        int frameSendRate = Conversions.IntFromString(parts[partIndex + 3], ClientConfiguration.Instance.DefaultFrameSendRate);
        int maxAhead = Conversions.IntFromString(parts[partIndex + 4], ClientConfiguration.Instance.DefaultMaxAhead);
        int protocolVersion = Conversions.IntFromString(parts[partIndex + 5], ClientConfiguration.Instance.DefaultProtocolVersion);

        int randomSeed = partIndex + 6 < parts.Length && int.TryParse(parts[partIndex + 6], out int parsedSeed)
            ? parsedSeed
            : 0;

        bool removeStartingLocations = partIndex + 7 < parts.Length
            && Convert.ToBoolean(Conversions.IntFromString(parts[partIndex + 7], 0));

        string mapName = partIndex + 8 < parts.Length ? parts[partIndex + 8] : string.Empty;

        state = new CnCNetGameOptionsState
        {
            CheckBoxValues = checkBoxValues,
            DropDownIndices = dropDownIndices,
            MapOfficial = mapOfficial,
            MapSha1 = mapSha1,
            GameModeName = gameModeName,
            FrameSendRate = frameSendRate,
            MaxAhead = maxAhead,
            ProtocolVersion = protocolVersion,
            RandomSeed = randomSeed,
            RemoveStartingLocations = removeStartingLocations,
            MapUntranslatedName = mapName,
        };

        return true;
    }
}
