using System;
using System.Threading.Tasks;
using Dalamud.Game.Command;
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

    private readonly GlossaryManager glossaryManager;
    private readonly YoudaoTranslator youdaoTranslator;
    private readonly GoogleTranslator googleTranslator;
    private readonly LmStudioTranslator lmStudioTranslator;
    private readonly GeminiTranslator translator;
    private readonly DialogueWatcher dialogueWatcher;
    private readonly SubtitleOverlayWindow overlayWindow;
    private readonly ConfigWindow configWindow;

    public Plugin()
    {
        Configuration = PluginInterface.GetPluginConfig() as Configuration ?? new Configuration();
        Configuration.Initialize(PluginInterface);

        glossaryManager = new GlossaryManager(PluginLog, PluginInterface.GetPluginConfigDirectory());
        youdaoTranslator = new YoudaoTranslator(PluginLog, glossaryManager);
        googleTranslator = new GoogleTranslator(PluginLog, glossaryManager);
        lmStudioTranslator = new LmStudioTranslator(Configuration, glossaryManager, PluginLog);
        translator = new GeminiTranslator(Configuration, glossaryManager, youdaoTranslator, googleTranslator, lmStudioTranslator, PluginLog);

        overlayWindow = new SubtitleOverlayWindow(Configuration);
        configWindow = new ConfigWindow(Configuration, translator, lmStudioTranslator, youdaoTranslator, googleTranslator, overlayWindow, glossaryManager);

        WindowSystem.AddWindow(overlayWindow);
        WindowSystem.AddWindow(configWindow);

        if (Configuration.AutoUpdateGlossary)
        {
            Task.Run(async () =>
            {
                try
                {
                    var (success, msg) = await glossaryManager.UpdateGlossariesFromTataruAsync();
                    if (success)
                    {
                        PluginLog.Information(msg);
                    }
                }
                catch (Exception ex)
                {
                    PluginLog.Warning(ex, "啟動時自動更新 Tataru 文本庫失敗。");
                }
            });
        }

        dialogueWatcher = new DialogueWatcher(Framework, GameGui, PluginLog, Configuration);
        dialogueWatcher.OnDialogueChanged += HandleDialogueChanged;
        dialogueWatcher.OnDialogueClosed += HandleDialogueClosed;

        CommandManager.AddHandler(CommandName, new CommandInfo(OnCommand)
        {
            HelpMessage = "開啟設定 (/tataru), 開關字幕 (/tataru toggle), 或切換引擎 (/tataru LMS / /tataru gemini)"
        });

        PluginInterface.UiBuilder.Draw += DrawUI;
        PluginInterface.UiBuilder.OpenConfigUi += DrawConfigUI;
        PluginInterface.UiBuilder.OpenMainUi += DrawConfigUI;
    }

    private void HandleDialogueChanged(DialogueEntry entry)
    {
        // 1. Immediately display original dialogue while waiting for AI translation
        overlayWindow.UpdateDialogue(entry);

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
                overlayWindow.IsOpen = !overlayWindow.IsOpen;
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
                    var fallbackName = fallback == TranslationEngine.Gemini ? "Gemini" : "有道 (Youdao)";
                    SwitchEngine(fallback, fallbackName);
                }
                else
                {
                    SwitchEngine(TranslationEngine.LmStudio, "LM Studio (本地模型)");
                }
                break;

            case "youdao":
                SwitchEngine(TranslationEngine.Youdao, "有道 (Youdao)");
                break;

            case "google":
                SwitchEngine(TranslationEngine.Google, "Google 翻譯");
                break;

            case "update" or "glossary":
                ChatGui.Print("[Tataru] 正在從 Tataru-Assistant 雲端更新文本與專有名詞庫...");
                Task.Run(async () =>
                {
                    var (success, msg) = await glossaryManager.UpdateGlossariesFromTataruAsync();
                    ChatGui.Print($"[Tataru] {msg}");
                });
                break;

            case "help":
                ChatGui.Print("[Tataru] 指令說明：");
                ChatGui.Print("  /tataru - 開啟或關閉設定視窗");
                ChatGui.Print("  /tataru toggle - 開啟或關閉字幕懸浮窗");
                ChatGui.Print("  /tataru LMS [on|off] - 開關或切換主引擎為 LM Studio 本地模型");
                ChatGui.Print("  /tataru gemini [on|off] - 開關或切換主引擎為 Gemini");
                ChatGui.Print("  /tataru update - 從 Tataru-Assistant 雲端更新最新文本與專有名詞庫");
                break;

            default:
                configWindow.IsOpen = !configWindow.IsOpen;
                break;
        }
    }

    private void SwitchEngine(TranslationEngine engine, string engineName)
    {
        Configuration.PrimaryEngine = engine;
        Configuration.Save();
        translator.ClearCache();
        ChatGui.Print($"[Tataru] 主翻譯引擎已切換為：{engineName}");
    }

    private void DrawUI()
    {
        if (configWindow.IsOpen)
        {
            overlayWindow.IsConfigOpen = true;
            overlayWindow.IsOpen = true;
        }
        else
        {
            overlayWindow.IsConfigOpen = false;
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
        WindowSystem.RemoveAllWindows();
    }
}
