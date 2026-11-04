using System;
using System.Collections.Generic;
using System.Numerics;
using Dalamud.Game.Text;

namespace DalamudTataru.Models;

public static class ChatChannelHelper
{
    public static readonly List<XivChatType> DefaultSelectedTypes = new()
    {
        XivChatType.Say,
        XivChatType.Shout,
        XivChatType.Yell,
        XivChatType.TellIncoming,
        XivChatType.Party,
        XivChatType.CrossParty,
        XivChatType.Alliance,
        XivChatType.FreeCompany,
        XivChatType.FreeCompanyAnnouncement,
        XivChatType.NoviceNetwork,
        XivChatType.SystemMessage,
        XivChatType.Notice,
        XivChatType.Echo
    };

    public static readonly List<XivChatType> SocialChatTypes = new()
    {
        XivChatType.Say,
        XivChatType.Shout,
        XivChatType.Yell,
        XivChatType.TellIncoming,
        XivChatType.TellOutgoing,
        XivChatType.Party,
        XivChatType.CrossParty,
        XivChatType.Alliance,
        XivChatType.FreeCompany,
        XivChatType.NoviceNetwork,
        XivChatType.CustomEmote,
        XivChatType.StandardEmote,
        XivChatType.PvPTeam
    };

    public static readonly List<XivChatType> LinkshellChatTypes = new()
    {
        XivChatType.Ls1,
        XivChatType.Ls2,
        XivChatType.Ls3,
        XivChatType.Ls4,
        XivChatType.Ls5,
        XivChatType.Ls6,
        XivChatType.Ls7,
        XivChatType.Ls8
    };

    public static readonly List<XivChatType> CrossWorldLinkshellChatTypes = new()
    {
        XivChatType.CrossLinkShell1,
        XivChatType.CrossLinkShell2,
        XivChatType.CrossLinkShell3,
        XivChatType.CrossLinkShell4,
        XivChatType.CrossLinkShell5,
        XivChatType.CrossLinkShell6,
        XivChatType.CrossLinkShell7,
        XivChatType.CrossLinkShell8
    };

    public static readonly List<XivChatType> SystemChatTypes = new()
    {
        XivChatType.SystemMessage,
        XivChatType.FreeCompanyAnnouncement,
        XivChatType.Notice,
        XivChatType.Urgent,
        XivChatType.Echo,
        XivChatType.RetainerSale,
        XivChatType.LootNotice,
        XivChatType.LootRoll,
        XivChatType.Progress,
        XivChatType.GatheringSystemMessage,
        XivChatType.Crafting,
        XivChatType.Gathering,
        XivChatType.ErrorMessage,
        XivChatType.SystemError,
        XivChatType.FreeCompanyLoginLogout,
        XivChatType.NoviceNetworkSystem,
        XivChatType.Alarm,
        XivChatType.GlamourNotifications
    };

    public static string GetDisplayName(XivChatType type) => type switch
    {
        XivChatType.Say => "說話 (Say)",
        XivChatType.Shout => "喊話 (Shout)",
        XivChatType.Yell => "呼喊 (Yell)",
        XivChatType.TellIncoming => "悄悄話/私聊-接收 (Tell In)",
        XivChatType.TellOutgoing => "悄悄話/私聊-發送 (Tell Out)",
        XivChatType.Party => "小隊 (Party)",
        XivChatType.CrossParty => "跨服小隊 (Cross Party)",
        XivChatType.Alliance => "團隊 (Alliance)",
        XivChatType.FreeCompany => "部隊 / 公會 (Free Company)",
        XivChatType.NoviceNetwork => "新人頻道 (Novice Network)",
        XivChatType.CustomEmote => "自訂情感動作 (Custom Emote)",
        XivChatType.StandardEmote => "標準情感動作 (Standard Emote)",
        XivChatType.PvPTeam => "PvP 戰隊 (PvP Team)",

        XivChatType.Ls1 => "通訊貝 1 (LS 1)",
        XivChatType.Ls2 => "通訊貝 2 (LS 2)",
        XivChatType.Ls3 => "通訊貝 3 (LS 3)",
        XivChatType.Ls4 => "通訊貝 4 (LS 4)",
        XivChatType.Ls5 => "通訊貝 5 (LS 5)",
        XivChatType.Ls6 => "通訊貝 6 (LS 6)",
        XivChatType.Ls7 => "通訊貝 7 (LS 7)",
        XivChatType.Ls8 => "通訊貝 8 (LS 8)",

        XivChatType.CrossLinkShell1 => "跨服通訊貝 1 (CWLS 1)",
        XivChatType.CrossLinkShell2 => "跨服通訊貝 2 (CWLS 2)",
        XivChatType.CrossLinkShell3 => "跨服通訊貝 3 (CWLS 3)",
        XivChatType.CrossLinkShell4 => "跨服通訊貝 4 (CWLS 4)",
        XivChatType.CrossLinkShell5 => "跨服通訊貝 5 (CWLS 5)",
        XivChatType.CrossLinkShell6 => "跨服通訊貝 6 (CWLS 6)",
        XivChatType.CrossLinkShell7 => "跨服通訊貝 7 (CWLS 7)",
        XivChatType.CrossLinkShell8 => "跨服通訊貝 8 (CWLS 8)",

        XivChatType.SystemMessage => "系統訊息 (System Message)",
        XivChatType.FreeCompanyAnnouncement => "部隊公告/留言板 (FC Board)",
        XivChatType.Notice => "系統通知 (Notice)",
        XivChatType.Urgent => "緊急公告 (Urgent)",
        XivChatType.Echo => "回聲/自訂宏輸出 (Echo)",
        XivChatType.RetainerSale => "雇員出售通知 (Retainer Sale)",
        XivChatType.LootNotice => "戰利品獲取 (Loot Notice)",
        XivChatType.LootRoll => "戰利品擲骰 (Loot Roll)",
        XivChatType.Progress => "任務/進度提示 (Progress)",
        XivChatType.GatheringSystemMessage => "採集系統訊息 (Gathering System)",
        XivChatType.Crafting => "製作訊息 (Crafting)",
        XivChatType.Gathering => "採集訊息 (Gathering)",
        XivChatType.ErrorMessage => "錯誤訊息 (Error Message)",
        XivChatType.SystemError => "系統錯誤 (System Error)",
        XivChatType.FreeCompanyLoginLogout => "部隊登入/登出 (FC Member)",
        XivChatType.NoviceNetworkSystem => "新人系統提示 (Novice System)",
        XivChatType.Alarm => "鬧鐘提示 (Alarm)",
        XivChatType.GlamourNotifications => "外觀投影通知 (Glamour)",

        _ => type.ToString()
    };

    public static Vector4 GetChannelColor(XivChatType type) => type switch
    {
        XivChatType.Say => new Vector4(0.9f, 0.9f, 0.9f, 1f),
        XivChatType.Shout => new Vector4(1.0f, 0.6f, 0.2f, 1f),
        XivChatType.Yell => new Vector4(1.0f, 0.9f, 0.2f, 1f),
        XivChatType.TellIncoming or XivChatType.TellOutgoing => new Vector4(1.0f, 0.6f, 0.8f, 1f),
        XivChatType.Party or XivChatType.CrossParty => new Vector4(0.4f, 0.85f, 1.0f, 1f),
        XivChatType.Alliance => new Vector4(1.0f, 0.65f, 0.3f, 1f),
        XivChatType.FreeCompany or XivChatType.FreeCompanyAnnouncement => new Vector4(0.45f, 0.75f, 1.0f, 1f),
        XivChatType.NoviceNetwork => new Vector4(0.6f, 0.95f, 0.6f, 1f),
        XivChatType.Ls1 or XivChatType.Ls2 or XivChatType.Ls3 or XivChatType.Ls4 or
        XivChatType.Ls5 or XivChatType.Ls6 or XivChatType.Ls7 or XivChatType.Ls8 => new Vector4(0.6f, 0.9f, 0.6f, 1f),
        XivChatType.CrossLinkShell1 or XivChatType.CrossLinkShell2 or XivChatType.CrossLinkShell3 or XivChatType.CrossLinkShell4 or
        XivChatType.CrossLinkShell5 or XivChatType.CrossLinkShell6 or XivChatType.CrossLinkShell7 or XivChatType.CrossLinkShell8 => new Vector4(0.85f, 0.7f, 1.0f, 1f),
        XivChatType.SystemMessage or XivChatType.Notice => new Vector4(0.8f, 0.8f, 0.8f, 1f),
        XivChatType.Urgent or XivChatType.ErrorMessage or XivChatType.SystemError => new Vector4(1.0f, 0.4f, 0.4f, 1f),
        XivChatType.Echo => new Vector4(0.7f, 0.8f, 0.9f, 1f),
        XivChatType.RetainerSale or XivChatType.LootNotice => new Vector4(0.95f, 0.85f, 0.4f, 1f),
        _ => new Vector4(0.75f, 0.75f, 0.75f, 1f)
    };
}
