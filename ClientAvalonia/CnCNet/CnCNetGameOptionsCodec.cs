using ClientAvalonia.Configuration;
using ClientAvalonia.GlobalState.Environment;
using ClientCore;
using Rampastring.Tools;
using System;
using System.Collections.Generic;
using System.Text;

namespace ClientAvalonia.CnCNet;

/// <summary>Builds and parses GO CTCP bodies (XNA CnCNetGameLobby).</summary>
public static class CnCNetGameOptionsCodec
{
    public static string BuildBody(CnCNetGameOptionsState state)
        => BuildBody(state, state.CheckBoxValues.Count, state.DropDownIndices.Count);

    /// <summary>
    /// Builds GO body with explicit control counts so joiners (DXMain ApplyGameOptions) can index
    /// <c>parts[checkBoxIntegerCount + dropDownCount + 8]</c> without IndexOutOfRangeException.
    /// </summary>
    public static string BuildBody(CnCNetGameOptionsState state, int checkBoxCount, int dropDownCount)
    {
        int checkBoxIntegerCount = checkBoxCount > 0 ? (checkBoxCount / 32) + 1 : 0;

        var checkBoxValues = new bool[checkBoxCount];
        for (int i = 0; i < checkBoxCount; i++)
            checkBoxValues[i] = i < state.CheckBoxValues.Count && state.CheckBoxValues[i];

        List<byte> byteList = checkBoxCount > 0
            ? Conversions.BoolArrayIntoBytes(checkBoxValues).ToList()
            : [];

        while (byteList.Count < checkBoxIntegerCount * 4)
            byteList.Add(0);

        byte[] byteArray = byteList.ToArray();
        var sb = new StringBuilder();

        for (int i = 0; i < checkBoxIntegerCount; i++)
        {
            if (sb.Length > 0)
                sb.Append(';');

            sb.Append(BitConverter.ToInt32(byteArray, i * 4));
        }

        for (int d = 0; d < dropDownCount; d++)
        {
            int index = d < state.DropDownIndices.Count ? state.DropDownIndices[d] : 0;
            sb.Append(';');
            sb.Append(index);
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
        int checkBoxIntegerCount = checkBoxCount > 0 ? (checkBoxCount / 32) + 1 : 0;
        int partIndex = checkBoxIntegerCount + dropDownCount;

        if (parts.Length < partIndex + 9)
        {
            error = $"invalid game options message length ({parts.Length}, need {partIndex + 9})";
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
        IGameConfiguration config = EnvironmentServices.Resolve<IGameConfiguration>();
        int frameSendRate = Conversions.IntFromString(parts[partIndex + 3], config.DefaultFrameSendRate);
        int maxAhead = Conversions.IntFromString(parts[partIndex + 4], config.DefaultMaxAhead);
        int protocolVersion = Conversions.IntFromString(parts[partIndex + 5], config.DefaultProtocolVersion);

        int randomSeed = int.TryParse(parts[partIndex + 6], out int parsedSeed)
            ? parsedSeed
            : 0;

        bool removeStartingLocations = Convert.ToBoolean(Conversions.IntFromString(parts[partIndex + 7], 0));
        string mapName = parts[partIndex + 8];

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
