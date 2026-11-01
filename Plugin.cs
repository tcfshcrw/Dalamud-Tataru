using System;
using System.Text.Unicode;
using System.Threading.Tasks;
using Dalamud;
using Dalamud.Game.Command;
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

    [PluginService] internal static IDalamudPluginInterface PluginInterface { get; private set; } = null!;
    [PluginService] internal static ICommandManager CommandManager { get; private set; } = null!;
    [PluginService] internal static IFramework Framework { get; private set; } = null!;
    [PluginService] internal static IGameGui GameGui { get; private set; } = null!;
    [PluginService] internal static IChatGui ChatGui { get; private set; } = null!;
    [PluginService] internal static IPluginLog PluginLog { get; private set; } = null!;

    public Configuration Configuration { get; init; }
    public WindowSystem WindowSystem { get; init; } = new("TataruPlugin");
    public IFontHandle? CjkFontHandle { get; private set; }

    private readonly GlossaryManager glossaryManager;
    private readonly YoudaoTranslator youdaoTranslator;
    private readonly GoogleTranslator googleTranslator;
    private readonly LmStudioTranslator lmStudioTranslator;
    private readonly GeminiTranslator translator;
    private readonly DialogueWatcher dialogueWatcher;
    private readonly SubtitleOverlayWindow overlayWindow;
    private readonly DialogueReplaceOverlayWindow replaceOverlayWindow;
    private readonly ConfigWindow configWindow;

    public Plugin()
    {
        Configuration = PluginInterface.GetPluginConfig() as Configuration ?? new Configuration();
        Configuration.Initialize(PluginInterface);

        try
        {
            var glyphRanges = FontAtlasBuildToolkitUtilities.BeginGlyphRange(UnicodeRanges.BasicLatin)
                .With(
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

        glossaryManager = new GlossaryManager(PluginLog, PluginInterface.GetPluginConfigDirectory());
        youdaoTranslator = new YoudaoTranslator(Configuration, PluginLog, glossaryManager);
        googleTranslator = new GoogleTranslator(Configuration, PluginLog, glossaryManager);
        lmStudioTranslator = new LmStudioTranslator(Configuration, glossaryManager, PluginLog);
        translator = new GeminiTranslator(Configuration, glossaryManager, youdaoTranslator, googleTranslator, lmStudioTranslator, PluginLog);

        dialogueWatcher = new DialogueWatcher(Framework, GameGui, PluginLog, Configuration);
        dialogueWatcher.OnDialogueChanged += HandleDialogueChanged;
        dialogueWatcher.OnDialogueClosed += HandleDialogueClosed;

        overlayWindow = new SubtitleOverlayWindow(Configuration, CjkFontHandle);
        replaceOverlayWindow = new DialogueReplaceOverlayWindow(Configuration, dialogueWatcher, CjkFontHandle);
        configWindow = new ConfigWindow(Configuration, translator, lmStudioTranslator, youdaoTranslator, googleTranslator, overlayWindow, replaceOverlayWindow, dialogueWatcher, glossaryManager, CjkFontHandle);

        WindowSystem.AddWindow(overlayWindow);
        WindowSystem.AddWindow(replaceOverlayWindow);
        WindowSystem.AddWindow(configWindow);

        var version = GetType().Assembly.GetName().Version;
        string versionStr = version != null ? $"v{version}" : "v1.0.0";
        string engineStr = GetEngineDisplayName(Configuration.PrimaryEngine);

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
                    ChatGui.Print($"[Tataru] {versionStr} loaded | Engine: {engineStr} | Glossary: {updateMsg}");
                }
            });
        }
        else
        {
            if (Configuration.ShowStartupNotification)
            {
                ChatGui.Print($"[Tataru] {versionStr} loaded | Engine: {engineStr} | Glossary: Auto-update disabled");
            }
        }

        CommandManager.AddHandler(CommandName, new CommandInfo(OnCommand)
        {
            HelpMessage = "開啟設定 (/tataru), 開關字幕 (/tataru toggle), 或切換引擎 (/tataru LMS / /tataru gemini)"
        });

        // 允許視窗在過場動畫 (Cutscene) 及遊戲 UI 隱藏狀態下保持繪製與顯示
        PluginInterface.UiBuilder.DisableCutsceneUiHide = true;
        PluginInterface.UiBuilder.DisableAutomaticUiHide = true;
        PluginInterface.UiBuilder.DisableUserUiHide = true;

        PluginInterface.UiBuilder.Draw += DrawUI;
        PluginInterface.UiBuilder.OpenConfigUi += DrawConfigUI;
        PluginInterface.UiBuilder.OpenMainUi += DrawConfigUI;
    }

    private void HandleDialogueChanged(DialogueEntry entry)
    {
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
                ChatGui.Print("  /tataru - Toggle config window");
                ChatGui.Print("  /tataru toggle - Toggle subtitle overlay window");
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
        CommandManager.RemoveHandler(CommandName);
        PluginInterface.UiBuilder.Draw -= DrawUI;
        PluginInterface.UiBuilder.OpenConfigUi -= DrawConfigUI;
        PluginInterface.UiBuilder.OpenMainUi -= DrawConfigUI;

        dialogueWatcher.OnDialogueChanged -= HandleDialogueChanged;
        dialogueWatcher.OnDialogueClosed -= HandleDialogueClosed;
        dialogueWatcher.Dispose();

        translator.Dispose();
        lmStudioTranslator.Dispose();
        youdaoTranslator.Dispose();
        googleTranslator.Dispose();
        CjkFontHandle?.Dispose();
        WindowSystem.RemoveAllWindows();
    }
}
