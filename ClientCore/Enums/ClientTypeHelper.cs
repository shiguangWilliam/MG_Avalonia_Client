using System;
using System.Linq;
using ClientCore.Extensions;

namespace ClientCore.Enums
{
    public static class ClientTypeHelper
    {
        /// <summary>
        /// Avalonia workspace-picker fallback when ClientDefinitions.ini lacks ClientGameType.
        /// Cleared on workspace teardown. Does not rewrite the ini on disk.
        /// </summary>
        public static ClientType? SessionFallback { get; set; }

        public static void ClearSessionFallback() => SessionFallback = null;

        public static bool TryParse(string? value, out ClientType clientType)
        {
            clientType = default;
            if (string.IsNullOrWhiteSpace(value))
                return false;

            switch (value.Trim())
            {
                case "TS":
                    clientType = ClientType.TS;
                    return true;
                case "YR":
                    clientType = ClientType.YR;
                    return true;
                case "Ares":
                    clientType = ClientType.Ares;
                    return true;
                case "RA":
                    clientType = ClientType.RA;
                    return true;
                default:
                    return false;
            }
        }

        public static ClientType FromString(string value)
        {
            if (TryParse(value, out ClientType parsed))
                return parsed;

            if (SessionFallback is { } fallback)
                return fallback;

            throw new Exception(string.Format((
                "客户端配置似乎尚未迁移以适配 v2.12 变更。\n" +
                "请在 Resources\\ClientDefinitions.ini 的 [Settings] 中指定 ClientGameType" +
                "（可选值：{0}），或在 Avalonia 工作区选择器中手动选择游戏类型后注册/启动。\n\n" +
                "详情见客户端文档：{1}\n（该链接也会写入日志文件。）").L10N("Client:Main:ClientGameTypeNotFoundException"),
                EnumExtensions.GetNames<ClientType>(),
                "https://github.com/CnCNet/xna-cncnet-client/"));
        }
    }
}
