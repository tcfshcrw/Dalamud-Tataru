using System;
using System.Text.RegularExpressions;
using Dalamud.Game.ClientState.Objects.SubKinds;
using DalamudTataru.Models;

namespace DalamudTataru.Services;

/// <summary>
/// 處理玩家角色名稱的辨識、保護與反遮罩替換，防止 AI 或機器翻譯將玩家的英文/拼音名字音譯為漢字（例如 Crew -> 克魯威）。
/// </summary>
public static class PlayerNameHelper
{
    public const string PlayerPlaceholder = "⟦PLAYER⟧";

    // 快取最後已知之玩家全名（防止在過場動畫或傳送讀取時 LocalPlayer 暫時為 null）
    public static string LastKnownPlayerFullName { get; set; } = string.Empty;

    /// <summary>
    /// 取得當前玩家全名（First Last）
    /// </summary>
    public static string GetPlayerFullName(Configuration? config = null)
    {
        try
        {
            var localPlayer = Plugin.ClientState?.LocalPlayer;
            if (localPlayer != null && !string.IsNullOrWhiteSpace(localPlayer.Name.TextValue))
            {
                LastKnownPlayerFullName = localPlayer.Name.TextValue.Trim();
                return LastKnownPlayerFullName;
            }
        }
        catch
        {
            // Ignore in unit test or early initialization
        }

        if (!string.IsNullOrWhiteSpace(LastKnownPlayerFullName))
        {
            return LastKnownPlayerFullName;
        }

        if (config != null && !string.IsNullOrWhiteSpace(config.ManualPlayerCharacterName))
        {
            return config.ManualPlayerCharacterName.Trim();
        }

        return string.Empty;
    }

    /// <summary>
    /// 取得玩家的名字 (First Name)
    /// </summary>
    public static string GetPlayerFirstName(Configuration? config = null)
    {
        var fullName = GetPlayerFullName(config);
        if (string.IsNullOrWhiteSpace(fullName)) return string.Empty;

        var parts = fullName.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        return parts.Length > 0 ? parts[0] : fullName;
    }

    /// <summary>
    /// 取得玩家的姓氏 (Last Name)
    /// </summary>
    public static string GetPlayerLastName(Configuration? config = null)
    {
        var fullName = GetPlayerFullName(config);
        if (string.IsNullOrWhiteSpace(fullName)) return string.Empty;

        var parts = fullName.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        return parts.Length > 1 ? parts[1] : string.Empty;
    }

    /// <summary>
    /// 取得最終欲顯示在翻譯台詞中的玩家稱呼（若有自訂名稱則優先採用，否則採用角色名）
    /// </summary>
    public static string GetTargetDisplayName(Configuration config)
    {
        if (!string.IsNullOrWhiteSpace(config.CustomPlayerNameReplacement))
        {
            return config.CustomPlayerNameReplacement.Trim();
        }

        var firstName = GetPlayerFirstName(config);
        if (!string.IsNullOrWhiteSpace(firstName))
        {
            return firstName;
        }

        return string.Empty;
    }

    /// <summary>
    /// 在向 AI 或翻譯 API 發送前，將台詞中的玩家名稱替換為特殊佔位符 ⟦PLAYER⟧，防止被音譯。
    /// </summary>
    public static (string maskedSpeaker, string maskedText) Mask(string speaker, string text, Configuration config)
    {
        if (!config.ProtectPlayerName)
        {
            return (speaker, text);
        }

        var fullName = GetPlayerFullName(config);
        var firstName = GetPlayerFirstName(config);
        var lastName = GetPlayerLastName(config);

        string maskedSpeaker = speaker;
        string maskedText = text;

        // 1. 若說話者本身就是玩家自身
        if (!string.IsNullOrWhiteSpace(fullName) && string.Equals(speaker.Trim(), fullName, StringComparison.OrdinalIgnoreCase))
        {
            maskedSpeaker = PlayerPlaceholder;
        }
        else if (!string.IsNullOrWhiteSpace(firstName) && firstName.Length >= 2 && string.Equals(speaker.Trim(), firstName, StringComparison.OrdinalIgnoreCase))
        {
            maskedSpeaker = PlayerPlaceholder;
        }

        // 2. 遮罩台詞文字中的玩家全名 (例如 "Crew Azure")
        if (!string.IsNullOrWhiteSpace(fullName) && fullName.Length >= 3 && maskedText.Contains(fullName, StringComparison.OrdinalIgnoreCase))
        {
            var pattern = $@"(?<![a-zA-Z0-9]){Regex.Escape(fullName)}(?![a-zA-Z0-9])";
            maskedText = Regex.Replace(maskedText, pattern, PlayerPlaceholder, RegexOptions.IgnoreCase);
        }

        // 3. 遮罩台詞文字中的玩家名 (例如 "Crew")
        if (!string.IsNullOrWhiteSpace(firstName) && firstName.Length >= 2 && maskedText.Contains(firstName, StringComparison.OrdinalIgnoreCase))
        {
            var pattern = $@"(?<![a-zA-Z0-9]){Regex.Escape(firstName)}(?![a-zA-Z0-9])";
            maskedText = Regex.Replace(maskedText, pattern, PlayerPlaceholder, RegexOptions.IgnoreCase);
        }

        // 4. 遮罩台詞文字中的玩家姓氏 (例如 "Azure")（僅當姓氏長度 >= 3 且不是通用單詞時）
        if (!string.IsNullOrWhiteSpace(lastName) && lastName.Length >= 4 && lastName != firstName && maskedText.Contains(lastName, StringComparison.OrdinalIgnoreCase))
        {
            var pattern = $@"(?<![a-zA-Z0-9]){Regex.Escape(lastName)}(?![a-zA-Z0-9])";
            maskedText = Regex.Replace(maskedText, pattern, PlayerPlaceholder, RegexOptions.IgnoreCase);
        }

        return (maskedSpeaker, maskedText);
    }

    /// <summary>
    /// 在翻譯完成後，將佔位符 ⟦PLAYER⟧ 還原為目標玩家稱呼（或使用者自訂譯名）。
    /// </summary>
    public static string Unmask(string text, Configuration config)
    {
        if (string.IsNullOrEmpty(text)) return string.Empty;

        var targetName = GetTargetDisplayName(config);
        if (string.IsNullOrEmpty(targetName))
        {
            // 若無法解析出玩家名字，則移除佔位括號但保留通用名或原文
            targetName = "冒險者";
        }

        // 支援各種模型可能輸出的變形括號與空白 (如 ⟦PLAYER⟧, [PLAYER], {{PLAYER}}, <PLAYER>)
        var pattern = @"\u27e6\s*PLAYER\s*\u27e7|\[\s*PLAYER\s*\]|\{\{\s*PLAYER\s*\}\}|<\s*PLAYER\s*>|⟦\s*PLAYER\s*⟧";
        return Regex.Replace(text, pattern, targetName);
    }
}
