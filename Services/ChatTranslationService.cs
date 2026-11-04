using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Dalamud.Game.Chat;
using Dalamud.Game.ClientState.Conditions;
using Dalamud.Game.Text;
using Dalamud.Game.Text.SeStringHandling;
using Dalamud.Game.Text.SeStringHandling.Payloads;
using Dalamud.Plugin.Services;
using DalamudTataru.Models;

namespace DalamudTataru.Services;

public partial class ChatTranslationService : IDisposable
{
    private readonly Configuration config;
    private readonly GeminiTranslator translator;
    private readonly IChatGui chatGui;
    private readonly IClientState clientState;
    private readonly ICondition? condition;
    private readonly IPluginLog pluginLog;

    private readonly List<ChatHistoryEntry> history = new();
    private readonly object historyLock = new();
    private const int MaxHistoryCount = 100;

    private readonly ConcurrentDictionary<string, byte> recentPluginOutputs = new();
    private readonly Dictionary<string, int> lastMessageTime = new();
    private readonly object spamLock = new();

    public event Action<ChatHistoryEntry>? OnChatTranslated;

    [GeneratedRegex(@"^<se\.\d+>$", RegexOptions.IgnoreCase)]
    private static partial Regex SoundMacroRegex();

    [GeneratedRegex(@"^\s*https?://\S+(\s+https?://\S+)*\s*$", RegexOptions.IgnoreCase)]
    private static partial Regex PureLinkRegex();

    [GeneratedRegex(@"[\u3040-\u309F\u30A0-\u30FF\u3400-\u4DBF\u4E00-\u9FFF]|\p{L}{2,}")]
    private static partial Regex HasTranslatableContentRegex();

    [GeneratedRegex(@"[\uE000-\uF8FF]+")]
    private static partial Regex SpecialGlyphRegex();

    public ChatTranslationService(
        Configuration config,
        GeminiTranslator translator,
        IChatGui chatGui,
        IClientState clientState,
        ICondition? condition,
        IPluginLog pluginLog)
    {
        this.config = config;
        this.translator = translator;
        this.chatGui = chatGui;
        this.clientState = clientState;
        this.condition = condition;
        this.pluginLog = pluginLog;

        this.chatGui.ChatMessage += OnChatMessage;
    }

    public IReadOnlyList<ChatHistoryEntry> GetHistory()
    {
        lock (historyLock)
        {
            return history.ToList();
        }
    }

    public void ClearHistory()
    {
        lock (historyLock)
        {
            history.Clear();
        }
    }

    private void OnChatMessage(IHandleableChatMessage message)
    {
        try
        {
            if (message.IsHandled) return;
            if (!config.Enabled || !config.EnableChatTranslation) return;
            if (!config.SelectedChatTypes.Contains(message.LogKind)) return;

            if (!config.ChatEnabledInDuty && condition != null && condition[ConditionFlag.BoundByDuty])
                return;

            var senderText = message.Sender.TextValue;
            var messageText = message.Message.TextValue;

            // 1. Prevent recursion: skip our own translated outputs
            if (IsPluginOutput(senderText, messageText))
                return;

            // 2. Resolve sender name
            var playerPayload = message.Sender.Payloads.OfType<PlayerPayload>().FirstOrDefault();
            string senderName = playerPayload?.PlayerName ?? senderText;
            string localPlayerName = PlayerNameHelper.GetPlayerFullName(config);

            if (message.LogKind == XivChatType.TellOutgoing)
            {
                senderName = localPlayerName;
            }

            // 3. Ignore self messages if configured
            if (config.ChatIgnoreOwnMessages && !string.IsNullOrEmpty(localPlayerName) &&
                senderName.Equals(localPlayerName, StringComparison.OrdinalIgnoreCase))
            {
                return;
            }

            // 4. Macro spam protection
            if (config.ChatFilterMacros && IsSpam(senderName))
            {
                return;
            }

            // 5. Translatable content extraction & sanitization
            string rawContent = ExtractText(message.Message);
            string cleaned = Sanitize(rawContent).Trim();

            if (IsFilteredMessage(cleaned))
            {
                return;
            }

            // 6. Check if original message should be intercepted/suppressed
            bool wasSuppressed = false;
            if (config.ChatSuppressOriginal && config.ChatOutputToChatLog)
            {
                message.PreventOriginal();
                wasSuppressed = true;
            }

            var rawSender = message.Sender;
            var rawMessage = message.Message;

            // 7. Fire asynchronous translation task
            Task.Run(async () => await ProcessChatMessageAsync(
                message.LogKind,
                senderName,
                cleaned,
                messageText,
                wasSuppressed,
                rawSender,
                rawMessage));
        }
        catch (Exception ex)
        {
            pluginLog.Error(ex, "處理聊天訊息時發生未預期異常。");
        }
    }

    private async Task ProcessChatMessageAsync(
        XivChatType chatType,
        string sender,
        string textToTranslate,
        string fullOriginalText,
        bool wasSuppressed,
        Dalamud.Game.Text.SeStringHandling.SeString rawSender,
        Dalamud.Game.Text.SeStringHandling.SeString rawMessage)
    {
        try
        {
            var (translatedSpeaker, translatedText) = await translator.TranslateAsync(sender, textToTranslate);

            if (string.IsNullOrWhiteSpace(translatedText))
            {
                if (wasSuppressed)
                {
                    RestoreOriginalChatMessage(chatType, rawSender, rawMessage);
                }
                return;
            }

            // Skip output if translation resulted in an internal error string
            if (translatedText.StartsWith("[") && (translatedText.Contains("失敗") || translatedText.Contains("異常") || translatedText.Contains("錯誤")))
            {
                pluginLog.Warning($"聊天訊息翻譯未成功: {translatedText}");
                if (wasSuppressed)
                {
                    RestoreOriginalChatMessage(chatType, rawSender, rawMessage);
                }
                return;
            }

            // Skip if translation is identical to original (e.g. text was already in Chinese)
            if (string.Equals(textToTranslate.Trim(), translatedText.Trim(), StringComparison.OrdinalIgnoreCase))
            {
                if (wasSuppressed)
                {
                    RestoreOriginalChatMessage(chatType, rawSender, rawMessage);
                }
                return;
            }

            var entry = new ChatHistoryEntry
            {
                Timestamp = DateTime.Now,
                ChatType = chatType,
                Sender = string.IsNullOrWhiteSpace(translatedSpeaker) ? sender : translatedSpeaker,
                OriginalText = textToTranslate,
                TranslatedText = translatedText
            };

            lock (historyLock)
            {
                history.Add(entry);
                if (history.Count > MaxHistoryCount)
                {
                    history.RemoveAt(0);
                }
            }

            try
            {
                OnChatTranslated?.Invoke(entry);
            }
            catch (Exception ex)
            {
                pluginLog.Warning(ex, "調用 OnChatTranslated 事件回調失敗。");
            }

            if (config.ChatOutputToChatLog)
            {
                OutputToGameChatLog(chatType, entry.Sender, textToTranslate, translatedText);
            }
        }
        catch (Exception ex)
        {
            pluginLog.Error(ex, $"異步翻譯聊天訊息失敗 (來源: {sender}): {textToTranslate}");
            if (wasSuppressed)
            {
                RestoreOriginalChatMessage(chatType, rawSender, rawMessage);
            }
        }
    }

    private void RestoreOriginalChatMessage(
        XivChatType chatType,
        Dalamud.Game.Text.SeStringHandling.SeString rawSender,
        Dalamud.Game.Text.SeStringHandling.SeString rawMessage)
    {
        try
        {
            RecordPluginOutput(rawMessage.TextValue);
            chatGui.Print(new XivChatEntry
            {
                Type = chatType,
                Name = rawSender,
                Message = rawMessage
            });
        }
        catch (Exception ex)
        {
            pluginLog.Error(ex, "還原原生未翻譯訊息時發生異常。");
        }
    }

    private void OutputToGameChatLog(XivChatType chatType, string sender, string originalText, string translatedText)
    {
        var outputType = config.ChatUseEchoChannel ? XivChatType.Echo : chatType;
        var prefix = string.IsNullOrWhiteSpace(config.ChatPrefix) ? "[Tataru]" : config.ChatPrefix.Trim();

        string outOriginal = config.ChatEnableGlyphFolding ? GlyphFolder.Fold(originalText, config.CustomGlyphEntries) : originalText;
        string outTranslated = config.ChatEnableGlyphFolding ? GlyphFolder.Fold(translatedText, config.CustomGlyphEntries) : translatedText;
        string outSender = config.ChatEnableGlyphFolding ? GlyphFolder.Fold(sender, config.CustomGlyphEntries) : sender;

        var sb = new SeStringBuilder();
        bool prefixInMessage = string.IsNullOrEmpty(sender) || config.ChatUseEchoChannel;
        if (prefixInMessage)
        {
            if (config.ChatShowColoredPrefix)
            {
                // 58 is light green/teal in Dalamud UI foreground
                sb.AddUiForeground(prefix + " ", 58);
            }
            else
            {
                sb.Append(prefix + " ");
            }
        }

        if (config.ChatHideOriginal || config.ChatSuppressOriginal)
        {
            sb.Append(outTranslated);
        }
        else
        {
            sb.Append(outOriginal);
            if (config.ChatShowColoredPrefix)
            {
                sb.AddUiForeground("  [譯] ", 60);
            }
            else
            {
                sb.Append("  [譯] ");
            }
            sb.Append(outTranslated);
        }

        var messagePayload = sb.Build();
        RecordPluginOutput(messagePayload.TextValue);

        SeString namePayload;
        if (string.IsNullOrEmpty(sender) || config.ChatUseEchoChannel)
        {
            namePayload = SeString.Empty;
            if (config.ChatUseEchoChannel && !string.IsNullOrEmpty(sender))
            {
                var echoSb = new SeStringBuilder();
                echoSb.Append($"[{outSender}] ").Append(messagePayload);
                messagePayload = echoSb.Build();
                RecordPluginOutput(messagePayload.TextValue);
            }
        }
        else
        {
            namePayload = new SeString(new PlayerPayload($"{prefix} {outSender}", 0));
            RecordPluginOutput(namePayload.TextValue);
        }

        chatGui.Print(new XivChatEntry
        {
            Type = outputType,
            Name = namePayload,
            Message = messagePayload
        });
    }

    private bool IsPluginOutput(string senderText, string messageText)
    {
        var prefix = string.IsNullOrWhiteSpace(config.ChatPrefix) ? "[Tataru]" : config.ChatPrefix.Trim();
        if (senderText.Contains(prefix) || messageText.Contains(prefix) ||
            senderText.Contains("[Tataru]") || messageText.Contains("[Tataru]") ||
            senderText.Contains("[CT]") || messageText.Contains("[CT]"))
        {
            return true;
        }

        if (recentPluginOutputs.ContainsKey(messageText) || recentPluginOutputs.ContainsKey(senderText))
        {
            return true;
        }

        return false;
    }

    private void RecordPluginOutput(string text)
    {
        if (string.IsNullOrEmpty(text)) return;
        recentPluginOutputs[text] = 1;

        if (recentPluginOutputs.Count > 100)
        {
            var keys = recentPluginOutputs.Keys.Take(30).ToList();
            foreach (var k in keys)
            {
                recentPluginOutputs.TryRemove(k, out _);
            }
        }
    }

    private bool IsSpam(string sender)
    {
        if (string.IsNullOrEmpty(sender)) return false;

        int now = Environment.TickCount;
        lock (spamLock)
        {
            if (lastMessageTime.Count > 30)
            {
                var oldKeys = lastMessageTime.Where(kv => now - kv.Value > 3000).Select(kv => kv.Key).ToList();
                foreach (var k in oldKeys)
                {
                    lastMessageTime.Remove(k);
                }
            }

            bool isSpam = lastMessageTime.TryGetValue(sender, out int lastTime) && now - lastTime < 600;
            lastMessageTime[sender] = now;
            return isSpam;
        }
    }

    private bool IsFilteredMessage(string text)
    {
        if (text.Length < 2) return true;
        if (config.ChatFilterLinks && PureLinkRegex().IsMatch(text)) return true;
        if (SoundMacroRegex().IsMatch(text)) return true;
        if (!HasTranslatableContentRegex().IsMatch(text)) return true;

        return false;
    }

    private static string ExtractText(SeString seString)
    {
        var sb = new StringBuilder();
        bool inLink = false;

        for (int i = 0; i < seString.Payloads.Count; i++)
        {
            switch (seString.Payloads[i])
            {
                case TextPayload textPayload when !inLink:
                    sb.Append(textPayload.Text);
                    break;

                case PlayerPayload:
                case ItemPayload:
                case QuestPayload:
                case MapLinkPayload:
                case StatusPayload:
                case PartyFinderPayload:
                    inLink = true;
                    break;

                case RawPayload when inLink:
                    inLink = false;
                    break;

                case AutoTranslatePayload at:
                    sb.Append(at.Text);
                    break;
            }
        }

        return sb.Length > 0 ? sb.ToString() : seString.TextValue;
    }

    private static string Sanitize(string input)
    {
        return SpecialGlyphRegex().Replace(input, " ");
    }

    public void Dispose()
    {
        chatGui.ChatMessage -= OnChatMessage;
        recentPluginOutputs.Clear();
        lock (historyLock)
        {
            history.Clear();
        }
    }
}
