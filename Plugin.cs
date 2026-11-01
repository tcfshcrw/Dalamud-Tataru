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
        configWindow = new ConfigWindow(Configuration, translator, lmStudioTranslator, youdaoTranslator, googleTranslator, overlayWindow);

        WindowSystem.AddWindow(overlayWindow);
        WindowSystem.AddWindow(configWindow);

        dialogueWatcher = new DialogueWatcher(Framework, GameGui, PluginLog, Configuration);
        dialogueWatcher.OnDialogueChanged += HandleDialogueChanged;
        dialogueWatcher.OnDialogueClosed += HandleDialogueClosed;

        CommandManager.AddHandler(CommandName, new CommandInfo(OnCommand)
        {
            HelpMessage = "開啟 Tataru 設定視窗 (/tataru config) 或開關字幕視窗 (/tataru toggle)"
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
        var arg = args.Trim().ToLowerInvariant();
        if (arg == "toggle")
        {
            overlayWindow.IsOpen = !overlayWindow.IsOpen;
        }
        else
        {
            configWindow.IsOpen = !configWindow.IsOpen;
        }
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
