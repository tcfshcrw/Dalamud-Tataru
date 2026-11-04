using System;
using System.Linq;
using System.Text.Unicode;
using System.Threading.Tasks;
using Dalamud;
using Dalamud.Game.Command;
using Dalamud.Game.Gui.Dtr;
using Dalamud.Interface;
using Dalamud.Interface.ManagedFontAtlas;
using Dalamud.Interface.Windowing;
using Dalamud.IoC;
using Dalamud.Plugin;
using Dalamud.Plugin.Services;
using DalamudTataru.Models;
using DalamudTataru.Services;
using DalamudTataru.Windows;

namespace DalamudTataru;

public class Plugin : IDalamudPlugin
{
    public string Name => "Tataru Native Translator";
    private const string CommandName = "/tataru";

    internal static Plugin? Instance { get; private set; }

    [PluginService] internal static IDalamudPluginInterface PluginInterface { get; private set; } = null!;
    [PluginService] internal static ICommandManager CommandManager { get; private set; } = null!;
    [PluginService] internal static IFramework Framework { get; private set; } = null!;
    [PluginService] internal static IGameGui GameGui { get; private set; } = null!;
    [PluginService] internal static IChatGui ChatGui { get; private set; } = null!;
    [PluginService] internal static IPluginLog PluginLog { get; private set; } = null!;
    [PluginService] internal static IDtrBar DtrBar { get; private set; } = null!;
    [PluginService] internal static IClientState ClientState { get; private set; } = null!;
    [PluginService] internal static IObjectTable ObjectTable { get; private set; } = null!;
    [PluginService] internal static ICondition Condition { get; private set; } = null!;

    public Configuration Configuration { get; init; }
    public WindowSystem WindowSystem { get; init; } = new("TataruPlugin");
    public IFontHandle? CjkFontHandle { get; private set; }

    private readonly GlossaryManager glossaryManager;
    private readonly YoudaoTranslator youdaoTranslator;
    private readonly GoogleTranslator googleTranslator;
    private readonly LmStudioTranslator lmStudioTranslator;
    private readonly GeminiTranslator translator;
    private readonly ChatTranslationService chatTranslationService;
    private readonly DialogueWatcher dialogueWatcher;
    private readonly SubtitleOverlayWindow overlayWindow;
    private readonly DialogueReplaceOverlayWindow replaceOverlayWindow;
    private readonly GlossaryEditorWindow glossaryEditorWindow;
    private readonly ChatHistoryWindow chatHistoryWindow;
    private readonly ConfigWindow configWindow;
    private readonly IDtrBarEntry? dtrEntry;

    public Plugin()
    {
        Instance = this;
        Configuration = PluginInterface.GetPluginConfig() as Configuration ?? new Configuration();
        Configuration.Initialize(PluginInterface);

        try
        {
            var glyphRanges = FontAtlasBuildToolkitUtilities.BeginGlyphRange(UnicodeRanges.BasicLatin)
                .With(
                    UnicodeRanges.Latin1Supplement,
                    UnicodeRanges.GeneralPunctuation,
                    UnicodeRanges.CjkSymbolsandPunctuation,
                    UnicodeRanges.HalfwidthandFullwidthForms,
                    UnicodeRanges.Hiragana,
                    UnicodeRanges.Katakana,
                    UnicodeRanges.CjkCompatibility
                )
                .WithLanguage("zh-TW")
                .WithLanguage("zh-CN")
                .Build(addFallbackCodepoints: true, addEllipsisCodepoints: true);

            var fontConfig = new SafeFontConfig
            {
                SizePt = 16f,
                GlyphRanges = glyphRanges,
            };

            CjkFontHandle = PluginInterface.UiBuilder.FontAtlas.NewDelegateFontHandle(
                e => e.OnPreBuild(
                    tk => tk.AddDalamudAssetFont(
                        DalamudAsset.NotoSansCjkMedium,
                        fontConfig)));
        }
        catch (Exception ex)
        {
            PluginLog.Warning(ex, "無法建立 NotoSansCjk 中文字型句柄，將使用預設字型。");
        }

        glossaryManager = new GlossaryManager(Configuration, PluginLog, PluginInterface.GetPluginConfigDirectory());
        youdaoTranslator = new YoudaoTranslator(Configuration, PluginLog, glossaryManager);
        googleTranslator = new GoogleTranslator(Configuration, PluginLog, glossaryManager);
        lmStudioTranslator = new LmStudioTranslator(Configuration, glossaryManager, PluginLog);
        translator = new GeminiTranslator(Configuration, glossaryManager, youdaoTranslator, googleTranslator, lmStudioTranslator, PluginLog);

        dialogueWatcher = new DialogueWatcher(Framework, GameGui, PluginLog, Configuration);
        dialogueWatcher.OnDialogueChanged += HandleDialogueChanged;
        dialogueWatcher.OnDialogueClosed += HandleDialogueClosed;

        chatTranslationService = new ChatTranslationService(Configuration, translator, ChatGui, ClientState, Condition, PluginLog);
        overlayWindow = new SubtitleOverlayWindow(Configuration, CjkFontHandle);
        replaceOverlayWindow = new DialogueReplaceOverlayWindow(Configuration, dialogueWatcher, CjkFontHandle);
        glossaryEditorWindow = new GlossaryEditorWindow(Configuration, glossaryManager, CjkFontHandle);
        chatHistoryWindow = new ChatHistoryWindow(Configuration, chatTranslationService, CjkFontHandle);
        configWindow = new ConfigWindow(Configuration, translator, lmStudioTranslator, youdaoTranslator, googleTranslator, overlayWindow, replaceOverlayWindow, dialogueWatcher, glossaryManager, glossaryEditorWindow, chatTranslationService, chatHistoryWindow, CjkFontHandle);

        WindowSystem.AddWindow(overlayWindow);
        WindowSystem.AddWindow(replaceOverlayWindow);
        WindowSystem.AddWindow(configWindow);
        WindowSystem.AddWindow(glossaryEditorWindow);
        WindowSystem.AddWindow(chatHistoryWindow);

        var version = GetType().Assembly.GetName().Version;
        string versionStr = version != null ? $"v{version}" : "v1.0.0";
        string engineStr = GetEngineDisplayName(Configuration.PrimaryEngine);
        string transStatusStr = Configuration.Enabled ? "ON" : "OFF (可輸入 /tataru on 開啟)";

        if (Configuration.AutoUpdateGlossary)
        {
            Task.Run(async () =>
            {
                string updateMsg;
                try
                {
                    var (success, hasUpdated, msg) = await glossaryManager.UpdateGlossariesFromTataruAsync();
                    if (success)
                    {
                        PluginLog.Information(msg);
                        updateMsg = hasUpdated ? "Updated to latest" : "Up to date";
                    }
                    else
                    {
                        PluginLog.Warning($"Tataru 文本庫檢查失敗：{msg}");
                        updateMsg = "Update check failed";
                    }
                }
                catch (Exception ex)
                {
                    PluginLog.Warning(ex, "啟動時自動更新 Tataru 文本庫失敗。");
                    updateMsg = "Connection error";
                }

                if (Configuration.ShowStartupNotification)
                {
                    ChatGui.Print($"[Tataru] {versionStr} loaded | Translation: {transStatusStr} | Engine: {engineStr} | Glossary: {updateMsg}");
                }
            });
        }
        else
        {
            if (Configuration.ShowStartupNotification)
            {
                ChatGui.Print($"[Tataru] {versionStr} loaded | Translation: {transStatusStr} | Engine: {engineStr} | Glossary: Auto-update disabled");
            }
        }

        CommandManager.AddHandler(CommandName, new CommandInfo(OnCommand)
        {
            HelpMessage = "開啟設定 (/tataru), 開關翻譯 (/tataru on / /tataru off), 或切換引擎 (/tataru LMS / /tataru gemini)"
        });

        // 允許視窗在過場動畫 (Cutscene) 及遊戲 UI 隱藏狀態下保持繪製與顯示
        PluginInterface.UiBuilder.DisableCutsceneUiHide = true;
        PluginInterface.UiBuilder.DisableAutomaticUiHide = true;
        PluginInterface.UiBuilder.DisableUserUiHide = true;

        PluginInterface.UiBuilder.Draw += DrawUI;
        PluginInterface.UiBuilder.OpenConfigUi += DrawConfigUI;
        PluginInterface.UiBuilder.OpenMainUi += DrawConfigUI;

        if (!Configuration.Enabled)
        {
            dialogueWatcher.RestoreTalkAddonState();
        }

        try
        {
            dtrEntry = DtrBar.Get("Tataru");
            if (dtrEntry != null)
            {
                dtrEntry.Shown = Configuration.ShowDtrStatus;
                dtrEntry.OnClick = e =>
                {
                    if (e.ClickType == MouseClickType.Right)
                    {
                        SetEnabled(!Configuration.Enabled);
                    }
                    else
                    {
                        configWindow.IsOpen = !configWindow.IsOpen;
                    }
                };
                UpdateDtrBar();
            }
        }
        catch (Exception ex)
        {
            PluginLog.Warning(ex, "無法初始化 DTR 伺服器資訊列狀態條目。");
        }
    }

    private void HandleDialogueChanged(DialogueEntry entry)
    {
        if (!Configuration.Enabled) return;

        // 1. Immediately display original dialogue while waiting for AI translation
        if (Configuration.ReplaceGameTalkAddon && entry.Type == DialogueType.Talk)
        {
            overlayWindow.IsOpen = false;
            replaceOverlayWindow.UpdateDialogue(entry);
        }
        else
        {
            replaceOverlayWindow.IsOpen = false;
            overlayWindow.UpdateDialogue(entry);
        }

        // 2. Asynchronously request Gemini translation
        Task.Run(async () =>
        {
            try
            {
                var (speaker, text) = await translator.TranslateAsync(entry.Speaker, entry.OriginalText);
                entry.TranslatedSpeaker = speaker;
                entry.TranslatedText = text;
                entry.IsTranslating = false;
            }
            catch (Exception ex)
            {
                PluginLog.Error(ex, "Failed to translate dialogue.");
                entry.TranslatedText = "[翻譯失敗]";
                entry.IsTranslating = false;
            }
        });
    }

    private void HandleDialogueClosed()
    {
        overlayWindow.CloseDialogue();
        replaceOverlayWindow.CloseDialogue();
    }

    public void SetEnabled(bool enable)
    {
        Configuration.Enabled = enable;
        Configuration.Save();

        if (!enable)
        {
            dialogueWatcher.RestoreTalkAddonState();
            HandleDialogueClosed();
            ChatGui.Print("[Tataru] 對話翻譯功能已關閉 (含覆蓋) (Translation disabled)");
        }
        else
        {
            ChatGui.Print("[Tataru] 對話翻譯功能已開啟 (Translation enabled)");
        }

        UpdateDtrBar();
    }
    private void OnCommand(string command, string args)
    {
        var trimmed = args.Trim();
        var parts = trimmed.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

        if (parts.Length == 0)
        {
            configWindow.IsOpen = !configWindow.IsOpen;
            return;
        }

        var subCommand = parts[0].ToLowerInvariant();

        switch (subCommand)
        {
            case "on" or "enable":
                SetEnabled(true);
                break;

            case "off" or "disable":
                SetEnabled(false);
                break;

            case "translate":
                if (parts.Length > 1)
                {
                    var state = parts[1].ToLowerInvariant();
                    if (state is "on" or "1" or "true")
                    {
                        SetEnabled(true);
                        return;
                    }
                    if (state is "off" or "0" or "false")
                    {
                        SetEnabled(false);
                        return;
                    }
                }
                SetEnabled(!Configuration.Enabled);
                break;

            case "status":
                ChatGui.Print($"[Tataru] 當前狀態: 翻譯功能 {(Configuration.Enabled ? "開啟 (ON)" : "已關閉 (OFF)")} | 引擎: {GetEngineDisplayName(Configuration.PrimaryEngine)} | 覆蓋模式: {(Configuration.ReplaceGameTalkAddon ? "開啟" : "關閉")}");
                break;

            case "toggle":
                if (Configuration.ReplaceGameTalkAddon)
                {
                    replaceOverlayWindow.IsOpen = !replaceOverlayWindow.IsOpen;
                }
                else
                {
                    overlayWindow.IsOpen = !overlayWindow.IsOpen;
                }
                break;

            case "glossary" or "dict" or "custom":
                glossaryEditorWindow.IsOpen = !glossaryEditorWindow.IsOpen;
                break;

            case "chat":
                chatHistoryWindow.IsOpen = !chatHistoryWindow.IsOpen;
                break;

            case "config" or "settings" or "setting":
                configWindow.IsOpen = !configWindow.IsOpen;
                break;

            case "gemini":
                if (parts.Length > 1)
                {
                    var state = parts[1].ToLowerInvariant();
                    if (state is "on" or "1" or "true")
                    {
                        SwitchEngine(TranslationEngine.Gemini, "Gemini");
                        return;
                    }
                    if (state is "off" or "0" or "false")
                    {
                        SwitchEngine(TranslationEngine.Youdao, "有道 (Youdao)");
                        return;
                    }
                }

                // 若只有 /tataru gemini 未指定參數，則在 Gemini 與 有道 之間切換
                if (Configuration.PrimaryEngine == TranslationEngine.Gemini)
                {
                    SwitchEngine(TranslationEngine.Youdao, "有道 (Youdao)");
                }
                else
                {
                    SwitchEngine(TranslationEngine.Gemini, "Gemini");
                }
                break;

            case "lms" or "lmstudio":
                if (parts.Length > 1)
                {
                    var state = parts[1].ToLowerInvariant();
                    if (state is "on" or "1" or "true")
                    {
                        SwitchEngine(TranslationEngine.LmStudio, "LM Studio (本地模型)");
                        return;
                    }
                    if (state is "off" or "0" or "false")
                    {
                        var fallback = string.IsNullOrWhiteSpace(Configuration.GeminiApiKey) ? TranslationEngine.Youdao : TranslationEngine.Gemini;
                        var fallbackName = fallback == TranslationEngine.Gemini ? "Gemini" : "有道 (Youdao)";
                        SwitchEngine(fallback, fallbackName);
                        return;
                    }
                }

                // 若只有 /tataru LMS 未指定參數，則在 LM Studio 與 雲端/備用引擎 (Gemini/有道) 之間切換開關
                if (Configuration.PrimaryEngine == TranslationEngine.LmStudio)
                {
                    var fallback = string.IsNullOrWhiteSpace(Configuration.GeminiApiKey) ? TranslationEngine.Youdao : TranslationEngine.Gemini;
                    var fallbackName = fallback == TranslationEngine.Gemini ? "Gemini" : "Youdao";
                    SwitchEngine(fallback, fallbackName);
                }
                else
                {
                    SwitchEngine(TranslationEngine.LmStudio, "LM Studio");
                }
                break;

            case "youdao":
                SwitchEngine(TranslationEngine.Youdao, "Youdao");
                break;

            case "google":
                SwitchEngine(TranslationEngine.Google, "Google Translate");
                break;

            case "update" or "glossary":
                ChatGui.Print("[Tataru] Checking glossary update from Tataru-Assistant...");
                Task.Run(async () =>
                {
                    var (success, hasUpdated, msg) = await glossaryManager.UpdateGlossariesFromTataruAsync(forceReload: true);
                    ChatGui.Print($"[Tataru] Glossary: {(success ? (hasUpdated ? "Updated to latest version." : "Already up to date.") : $"Update failed: {msg}")}");
                });
                break;

            case "clearcache" or "clear":
                var clearedCount = translator.ClearCache();
                ChatGui.Print($"[Tataru] Cleared {clearedCount} translation cache entries.");
                break;

            case "replace":
                if (parts.Length > 1)
                {
                    var state = parts[1].ToLowerInvariant();
                    Configuration.ReplaceGameTalkAddon = state is "on" or "1" or "true";
                }
                else
                {
                    Configuration.ReplaceGameTalkAddon = !Configuration.ReplaceGameTalkAddon;
                }
                Configuration.Save();
                if (configWindow.IsOpen)
                {
                    overlayWindow.IsOpen = !Configuration.ReplaceGameTalkAddon;
                    overlayWindow.IsConfigOpen = !Configuration.ReplaceGameTalkAddon;
                    replaceOverlayWindow.IsOpen = Configuration.ReplaceGameTalkAddon;
                    replaceOverlayWindow.IsConfigOpen = Configuration.ReplaceGameTalkAddon;
                }
                ChatGui.Print($"[Tataru] Dialogue replace overlay: {(Configuration.ReplaceGameTalkAddon ? "Enabled" : "Disabled")}");
                break;

            case "help":
                ChatGui.Print("[Tataru] Commands:");
                ChatGui.Print("  /tataru - 開啟/關閉設定視窗 (Toggle config window)");
                ChatGui.Print("  /tataru on / off - 啟用或關閉對話翻譯與覆蓋 (Enable/disable dialogue translation)");
                ChatGui.Print("  /tataru status - 查看當前翻譯開關與引擎狀態");
                ChatGui.Print("  /tataru toggle - 切換字幕懸浮窗顯示狀態 (Toggle subtitle overlay)");
                ChatGui.Print("  /tataru replace [on|off] - Toggle native dialogue replace overlay mode");
                ChatGui.Print("  /tataru LMS [on|off] - Toggle or switch primary engine to LM Studio");
                ChatGui.Print("  /tataru gemini [on|off] - Toggle or switch primary engine to Gemini");
                ChatGui.Print("  /tataru clearcache - Clear translation cache");
                ChatGui.Print("  /tataru update - Check and update glossary from Tataru-Assistant");
                break;

            default:
                configWindow.IsOpen = !configWindow.IsOpen;
                break;
        }
    }

    private string GetEngineDisplayName(TranslationEngine engine)
    {
        return engine switch
        {
            TranslationEngine.Gemini => $"Google Gemini (Model: {Configuration.GeminiModel})",
            TranslationEngine.LmStudio => $"LM Studio (Model: {(!string.IsNullOrEmpty(Configuration.LmStudioModel) ? Configuration.LmStudioModel : "Default")})",
            TranslationEngine.Youdao => "Youdao",
            TranslationEngine.Google => "Google Translate",
            _ => engine.ToString()
        };
    }

    private void SwitchEngine(TranslationEngine engine, string engineName)
    {
        Configuration.PrimaryEngine = engine;
        Configuration.Save();
        translator.ClearCache();
        ChatGui.Print($"[Tataru] Primary translation engine switched to: {GetEngineDisplayName(engine)}");
        UpdateDtrBar();
    }

    public void UpdateDtrBar()
    {
        if (dtrEntry == null) return;

        try
        {
            dtrEntry.Shown = Configuration.ShowDtrStatus;
            if (!Configuration.ShowDtrStatus) return;

            string emoji = string.IsNullOrWhiteSpace(Configuration.DtrCustomEmoji) ? "文" : Configuration.DtrCustomEmoji.Trim();
            if (emoji == "🌐" || emoji == "🗣️" || emoji == "💬" || emoji == "🤖" || emoji == "📖" || emoji.Any(char.IsSurrogate))
            {
                emoji = "文";
                Configuration.DtrCustomEmoji = "文";
                Configuration.Save();
            }

            dtrEntry.Text = Configuration.DtrStyle switch
            {
                DtrStatusStyle.EmojiText => Configuration.Enabled ? $"{emoji}: ON" : $"{emoji}: OFF",
                DtrStatusStyle.IconOnly => Configuration.Enabled ? emoji : $"{emoji} (Off)",
                DtrStatusStyle.Text => Configuration.Enabled ? "Tataru: ON" : "Tataru: OFF",
                _ => Configuration.Enabled ? $"{emoji}: ON" : $"{emoji}: OFF"
            };

            var engineName = Configuration.PrimaryEngine switch
            {
                TranslationEngine.Gemini => $"Gemini ({Configuration.GeminiModel})",
                TranslationEngine.LmStudio => $"LM Studio ({(!string.IsNullOrEmpty(Configuration.LmStudioModel) ? Configuration.LmStudioModel : "Default")})",
                TranslationEngine.Youdao => "有道 (Youdao)",
                TranslationEngine.Google => "Google Translate",
                _ => Configuration.PrimaryEngine.ToString()
            };

            var targetLangStr = Configuration.TargetLang switch
            {
                TargetLanguage.TraditionalChinese => "繁體中文",
                TargetLanguage.SimplifiedChinese => "簡體中文",
                _ => Configuration.TargetLang.ToString()
            };

            dtrEntry.Tooltip = $"【Tataru 翻譯器】\n狀態：{(Configuration.Enabled ? "開啟 (ON)" : "關閉 (OFF)")}\n引擎：{engineName}\n目標：{targetLangStr}\n\n[左鍵] 開啟/關閉設定視窗\n[右鍵] 快速切換開關";
        }
        catch (Exception ex)
        {
            PluginLog.Warning(ex, "更新 DTR 狀態時發生例外。");
        }
    }

    private void DrawUI()
    {
        if (configWindow.IsOpen)
        {
            if (Configuration.ReplaceGameTalkAddon)
            {
                overlayWindow.IsOpen = false;
                overlayWindow.IsConfigOpen = false;

                replaceOverlayWindow.IsConfigOpen = true;
                replaceOverlayWindow.IsOpen = true;
            }
            else
            {
                replaceOverlayWindow.IsOpen = false;
                replaceOverlayWindow.IsConfigOpen = false;

                overlayWindow.IsConfigOpen = true;
                overlayWindow.IsOpen = true;
            }
        }
        else
        {
            overlayWindow.IsConfigOpen = false;
            replaceOverlayWindow.IsConfigOpen = false;
        }

        WindowSystem.Draw();
    }

    private void DrawConfigUI()
    {
        configWindow.IsOpen = true;
    }

    public void Dispose()
    {
        Instance = null;
        try
        {
            if (dtrEntry != null)
            {
                dtrEntry.Shown = false;
                dtrEntry.Remove();
                dtrEntry.Dispose();
            }
        }
        catch (Exception ex)
        {
            PluginLog.Warning(ex, "釋放 DTR 條目時發生例外。");
        }
        CommandManager.RemoveHandler(CommandName);
        PluginInterface.UiBuilder.Draw -= DrawUI;
        PluginInterface.UiBuilder.OpenConfigUi -= DrawConfigUI;
        PluginInterface.UiBuilder.OpenMainUi -= DrawConfigUI;

        dialogueWatcher.OnDialogueChanged -= HandleDialogueChanged;
        dialogueWatcher.OnDialogueClosed -= HandleDialogueClosed;
        dialogueWatcher.Dispose();
        chatTranslationService.Dispose();

        translator.Dispose();
        lmStudioTranslator.Dispose();
        youdaoTranslator.Dispose();
        googleTranslator.Dispose();
        CjkFontHandle?.Dispose();
        WindowSystem.RemoveAllWindows();
    }
}
