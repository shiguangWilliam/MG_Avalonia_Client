using System;
using ClientAvalonia.Domain;
using ClientAvalonia.IniUi.Behaviors;

namespace ClientAvalonia.IniUi.Actions;

/// <summary>
/// 把内置（非 mod 提供的）动作注册到 <see cref="IIniActionCatalog"/>。
///
/// 这是「字符串名 → host 回调」的内置映射，对应 DX 启动器里按钮硬编码的
/// 一行调用（如 <c>btnExit → host.ExitApplication()</c>）。Mod 可通过 INI 写
/// <c>$LeftClickAction=ExitApplication</c> 把任意按钮绑到这些动作上。
///
/// 设计原则（与设计文档 §2.6 一致）：
///   - 不接管状态变更——那是 <see cref="UiAction{TContext}"/> 子类的职责
///   - 仅做"调用 host 的对应方法"这种薄包装
///   - 后续 mod 可覆盖同名注册（catalog 后注册覆盖先注册）
/// </summary>
public static class BuiltinIniActions
{
    /// <summary>
    /// 注册全部内置动作到指定 catalog。应在启动期调用一次。
    /// </summary>
    public static void RegisterAll(IIniActionCatalog catalog)
    {
        if (catalog == null) throw new ArgumentNullException(nameof(catalog));

        // 简单无参动作：直接转发到 host 同名方法。
        catalog.Register("ExitApplication", (_, host) => host.ExitApplication());
        catalog.Register("CheckForUpdates", (_, host) => host.CheckForUpdates());
        catalog.Register("RefreshMainMenuState", (_, host) => host.RefreshMainMenuState());
        catalog.Register("NavigateBack", (_, host) => host.NavigateBack());
        catalog.Register("LogoutToMainMenu", (_, host) => host.LogoutToMainMenu());
        catalog.Register("CloseFloatingOverlay", (_, host) => host.CloseFloatingOverlay());
        catalog.Register("CloseOptionsOverlay", (_, host) => host.CloseOptionsOverlay());
        catalog.Register("OpenCampaignOverlay", (_, host) => host.OpenCampaignOverlay());
        catalog.Register("OpenGameCreationOverlay", (_, host) => host.OpenGameCreationOverlay());
        catalog.Register("CloseGameCreationOverlay", (_, host) => host.CloseGameCreationOverlay());
        catalog.Register("TogglePlayerExtraOptionsPanel", (_, host) => host.TogglePlayerExtraOptionsPanel());
        catalog.Register("PickRandomLobbyMap", (_, host) => host.PickRandomLobbyMap());
        catalog.Register("ToggleFavoriteLobbyMap", (_, host) => host.ToggleFavoriteLobbyMap());
        catalog.Register("RefreshCnCNetGameListing", (_, host) => host.RefreshCnCNetGameListing());
        catalog.Register("TryJoinSelectedCnCNetGame", (_, host) => host.TryJoinSelectedCnCNetGame());
        catalog.Register("EnterCnCNetGameLobbyConnecting", (_, host) => host.EnterCnCNetGameLobbyConnecting());

        // 启动游戏类（带 out 参数的，包装成"启动 + 显示状态"）。
        catalog.Register("LaunchSkirmish", (_, host) =>
        {
            if (host.TryLaunchSkirmish(out string message))
                return;
            host.ShowStatus(string.IsNullOrWhiteSpace(message) ? "Launch failed." : message);
        });

        catalog.Register("LaunchCampaign", (_, host) =>
        {
            if (host.TryLaunchCampaign(out string message))
                return;
            host.ShowStatus(string.IsNullOrWhiteSpace(message) ? "Launch failed." : message);
        });

        catalog.Register("LaunchCnCNetGame", (_, host) =>
        {
            if (host.TryLaunchCnCNetGame(out string message))
                return;
            host.ShowStatus(string.IsNullOrWhiteSpace(message) ? "Launch failed." : message);
        });

        // 带参数动作：从冒号后取参数。
        catalog.Register("NavigateTo", (args, host) =>
        {
            string target = args.Trim();
            if (string.IsNullOrWhiteSpace(target))
            {
                host.ShowStatus("NavigateTo: missing target window name");
                return;
            }
            host.NavigateTo(target);
        });

        catalog.Register("OpenFloatingOverlay", (args, host) =>
        {
            string target = args.Trim();
            if (string.IsNullOrWhiteSpace(target))
            {
                host.ShowStatus("OpenFloatingOverlay: missing target window name");
                return;
            }
            host.OpenFloatingOverlay(target);
        });

        catalog.Register("ShowStatus", (args, host) =>
        {
            string message = args.Trim();
            if (string.IsNullOrWhiteSpace(message))
                return;
            host.ShowStatus(message);
        });

        catalog.Register("SelectOptionsTab", (args, host) =>
        {
            if (int.TryParse(args.Trim(), out int index))
                host.SelectOptionsTab(index);
        });

        catalog.Register("FilterCampaignBySide", (args, host) =>
        {
            string token = args.Trim();
            if (string.IsNullOrWhiteSpace(token))
                return;

            // 接受枚举名（GDI / Nod / Soviet / Allies / Yuri / All）或整数。
            if (Enum.TryParse<CampaignSideFilter>(token, ignoreCase: true, out var filter))
            {
                host.FilterCampaignBySide(filter);
                return;
            }

            if (int.TryParse(token, out int idx)
                && idx >= 0
                && idx <= (int)CampaignSideFilter.All)
            {
                host.FilterCampaignBySide((CampaignSideFilter)idx);
            }
        });
    }
}
