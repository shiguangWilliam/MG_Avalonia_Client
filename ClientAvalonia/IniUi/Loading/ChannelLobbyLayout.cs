using ClientAvalonia.IniUi.Layout;
using ClientAvalonia.IniUi.Models;

namespace ClientAvalonia.IniUi.Loading;

/// <summary>
/// CnCNet/LAN browser lobby geometry aligned with XNA <c>CnCNetLobby.cs</c> (controls built in code).
/// </summary>
internal static class ChannelLobbyLayout
{
    private const int SideMargin = 12;
    private const int TopContentY = 41;
    private const int HeaderY = 14;
    private const int ButtonWidth = 133;
    private const int ButtonHeight = 23;
    private const int ButtonGap = 12;
    private const int PlayerListWidth = 190;
    private const int PlayerListRightInset = 202;
    private const int ChatColumnGap = 24;
    private const int ColorDropdownWidth = 150;
    private const int ChannelDropdownWidth = 200;

    public static void Apply(UiNodeTree tree, LayoutContext context, string windowSectionName)
    {
        UiNode? windowRoot = tree.FindNode(windowSectionName) ?? tree.Root;
        int parentW = windowRoot.GetIntProp("Width");
        int parentH = windowRoot.GetIntProp("Height");
        if (parentW <= 0)
            parentW = context.Width;
        if (parentH <= 0)
            parentH = context.Height;

        int bottomToolbarY = parentH - 29;
        int listHeight = Math.Max(120, bottomToolbarY - TopContentY - 6);

        int playerListX = parentW - PlayerListRightInset;
        int gameListX = SideMargin;
        int gameListWidth = ButtonWidth + ButtonGap + ButtonWidth;
        int chatX = gameListX + gameListWidth + ButtonGap;
        int chatWidth = Math.Max(200, playerListX - chatX - ChatColumnGap);

        PositionList(tree, "lbGameList", gameListX, TopContentY, gameListWidth, listHeight);
        PositionList(tree, "lbPlayerList", playerListX, 20, PlayerListWidth, bottomToolbarY - 26 - 20);
        PositionList(tree, "lbChatMessages", chatX, TopContentY, chatWidth, listHeight);

        PositionBottomButtons(tree, bottomToolbarY, parentW, windowSectionName);
        PositionChatInput(tree, chatX, chatWidth, bottomToolbarY);
        PositionHeaderRow(tree, chatX, chatWidth, playerListX);
    }

    private static void PositionList(UiNodeTree tree, string id, int x, int y, int width, int height)
    {
        UiNode? node = tree.FindNode(id);
        if (node == null)
            return;

        node.Props["CanvasLeft"] = (double)x;
        node.Props["CanvasTop"] = (double)y;
        node.Props["Width"] = (double)Math.Max(width, 80);
        node.Props["Height"] = (double)Math.Max(height, 80);
    }

    private static void PositionBottomButtons(UiNodeTree tree, int y, int parentW, string windowSectionName)
    {
        SetButton(tree, "btnNewGame", SideMargin, y);
        SetButton(tree, "btnJoinGame", SideMargin + ButtonWidth + ButtonGap, y);

        // XNA LANLobby uses btnMainMenu; CnCNetLobby uses btnLogout. MG LANLobby.ini
        // BasedOn=CnCNetLobby.ini and also declares btnMainMenu at the same corner —
        // keep only the LAN main-menu button so the two do not overlap.
        bool isLan = windowSectionName.Equals("LANLobby", StringComparison.OrdinalIgnoreCase);
        if (isLan && tree.FindNode("btnMainMenu") != null)
        {
            SetButton(tree, "btnMainMenu", parentW - 145, y);
            HideButton(tree, "btnLogout");
        }
        else
        {
            SetButton(tree, "btnLogout", parentW - 145, y);
            HideButton(tree, "btnMainMenu");
        }
    }

    private static void SetButton(UiNodeTree tree, string id, int x, int y)
    {
        UiNode? btn = tree.FindNode(id);
        if (btn == null)
            return;

        btn.Props["CanvasLeft"] = (double)x;
        btn.Props["CanvasTop"] = (double)y;
        btn.Props["Width"] = (double)ButtonWidth;
        btn.Props["Height"] = (double)ButtonHeight;
        btn.Props["IsVisible"] = true;
    }

    private static void HideButton(UiNodeTree tree, string id)
    {
        UiNode? btn = tree.FindNode(id);
        if (btn == null)
            return;

        btn.Props["IsVisible"] = false;
    }

    private static void PositionChatInput(UiNodeTree tree, int chatX, int chatWidth, int y)
    {
        UiNode? input = tree.FindNode("tbChatInput");
        if (input == null)
            return;

        input.Props["CanvasLeft"] = (double)chatX;
        input.Props["CanvasTop"] = (double)y;
        input.Props["Width"] = (double)chatWidth;
        input.Props["Height"] = (double)ButtonHeight;
    }

    private static void PositionHeaderRow(UiNodeTree tree, int chatX, int chatWidth, int playerListX)
    {
        int channelDropdownX = chatX + chatWidth - ChannelDropdownWidth;
        if (channelDropdownX < chatX + 260)
            channelDropdownX = playerListX - ChannelDropdownWidth;

        UiNode? lblChannel = tree.FindNode("lblCurrentChannel");
        if (lblChannel != null)
        {
            lblChannel.Props["CanvasLeft"] = (double)(channelDropdownX - 150);
            lblChannel.Props["CanvasTop"] = (double)(HeaderY + 2);
            lblChannel.Props["Width"] = 140.0;
            lblChannel.Props["Height"] = 18.0;
        }

        UiNode? ddChannel = tree.FindNode("ddCurrentChannel");
        if (ddChannel != null)
        {
            ddChannel.Props["CanvasLeft"] = (double)channelDropdownX;
            ddChannel.Props["CanvasTop"] = (double)(HeaderY - 2);
            ddChannel.Props["Width"] = (double)ChannelDropdownWidth;
            ddChannel.Props["Height"] = 21.0;
        }

        UiNode? lblColor = tree.FindNode("lblColor");
        if (lblColor != null)
        {
            lblColor.Props["CanvasLeft"] = (double)chatX;
            lblColor.Props["CanvasTop"] = (double)HeaderY;
            lblColor.Props["Width"] = 95.0;
            lblColor.Props["Height"] = 18.0;
        }

        UiNode? ddColor = tree.FindNode("ddColor");
        if (ddColor != null)
        {
            ddColor.TemplateKey = "DxLobbyComboBox";
            ddColor.Props["CanvasLeft"] = (double)(chatX + 95);
            ddColor.Props["CanvasTop"] = (double)(HeaderY - 2);
            ddColor.Props["Width"] = (double)ColorDropdownWidth;
            ddColor.Props["Height"] = 21.0;
        }
    }
}
