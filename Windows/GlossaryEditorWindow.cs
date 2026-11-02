using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Numerics;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Dalamud.Interface.ManagedFontAtlas;
using Dalamud.Interface.Windowing;
using DalamudTataru.Models;
using DalamudTataru.Services;
using Dalamud.Bindings.ImGui;

namespace DalamudTataru.Windows;

public class GlossaryEditorWindow : Window
{
    private readonly Configuration config;
    private readonly GlossaryManager glossary;
    private readonly IFontHandle? fontHandle;

    // Working state (Buffered, Manual Save only)
    private List<CustomGlossaryEntry> workingEntries = new();
    private bool workingEnableCustom = true;
    private bool isDirty = false;
    private bool isInitialized = false;
    private string saveStatusMessage = string.Empty;

    // Search & Filter
    private string glossarySearchFilter = string.Empty;

    // Add / Edit Entry State
    private bool isEditingGlossary = false;
    private string? editingEntryId = null;
    private string editJp = string.Empty;
    private string editEn = string.Empty;
    private string editCht = string.Empty;
    private string editChs = string.Empty;
    private bool editMatchSpeaker = true;
    private bool editMatchDialogue = true;
    private string editNotes = string.Empty;
    private string editorValidationMsg = string.Empty;

    // Import / Export State
    private bool showImportExportSection = false;
    private string importExportBuffer = string.Empty;
    private string importExportStatus = string.Empty;

    // Live Match Tester State
    private string testSpeakerInput = "タタル";
    private string testDialogueInput = "光の戦士さん、暁の血盟の本部へ行きましょう！";
    private TargetLanguage testTargetLang = TargetLanguage.TraditionalChinese;
    private string testResultSpeaker = string.Empty;
    private string testResultDialogue = string.Empty;
    private string testMatchedRules = string.Empty;
    private bool showClearAllConfirm = false;

    public GlossaryEditorWindow(Configuration config, GlossaryManager glossary, IFontHandle? fontHandle = null)
        : base("Tataru 自訂多語名詞對照表編輯器###TataruGlossaryEditorWindow")
    {
        this.config = config;
        this.glossary = glossary;
        this.fontHandle = fontHandle;

        RespectCloseHotkey = true;
        Size = new Vector2(900, 700);
        SizeCondition = ImGuiCond.FirstUseEver;

        SizeConstraints = new WindowSizeConstraints
        {
            MinimumSize = new Vector2(620, 420),
            MaximumSize = new Vector2(3840, 2160)
        };
    }

    public override void OnOpen()
    {
        base.OnOpen();
        ReloadFromConfig();
    }

    public void ReloadFromConfig()
    {
        workingEntries = glossary.LoadCustomGlossaryFromFile().Select(e => e.Clone()).ToList();
        workingEnableCustom = config.EnableCustomGlossary;
        isDirty = false;
        saveStatusMessage = string.Empty;
        isEditingGlossary = false;
        editorValidationMsg = string.Empty;
        showClearAllConfirm = false;
        isInitialized = true;
    }

    private void SaveChanges()
    {
        glossary.SaveCustomGlossaryToFile(workingEntries);
        config.EnableCustomGlossary = workingEnableCustom;
        config.Save();
        isDirty = false;
        saveStatusMessage = $"[OK] 已成功儲存至 CustomGlossary.json（共 {workingEntries.Count} 筆規則）！時間：{DateTime.Now:HH:mm:ss}";
    }

    public override void Draw()
    {
        if (!isInitialized)
        {
            ReloadFromConfig();
        }

        IDisposable? fontScope = null;
        if (fontHandle?.Available == true)
        {
            fontScope = fontHandle.Push();
        }

        try
        {
            DrawEditorContent();
        }
        finally
        {
            fontScope?.Dispose();
        }
    }

    private void DrawEditorContent()
    {
        ImGui.Spacing();

        // 1. Top Save & Status Bar (Manual Save Control)
        if (isDirty)
        {
            ImGui.PushStyleColor(ImGuiCol.Button, new Vector4(0.18f, 0.58f, 0.28f, 1.0f));
            ImGui.PushStyleColor(ImGuiCol.ButtonHovered, new Vector4(0.25f, 0.72f, 0.35f, 1.0f));
            ImGui.PushStyleColor(ImGuiCol.ButtonActive, new Vector4(0.12f, 0.45f, 0.20f, 1.0f));
        }

        if (ImGui.Button("儲存至 CustomGlossary.json", new Vector2(0, 32)))
        {
            SaveChanges();
        }

        if (isDirty)
        {
            ImGui.PopStyleColor(3);
        }

        ImGui.SameLine();
        if (ImGui.Button("放棄修改 / 重新載入", new Vector2(0, 32)))
        {
            ReloadFromConfig();
        }

        ImGui.SameLine();
        if (ImGui.Button("開啟 JSON 所在資料夾", new Vector2(0, 32)))
        {
            try
            {
                System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
                {
                    FileName = glossary.ConfigDirectory,
                    UseShellExecute = true
                });
            }
            catch { }
        }

        ImGui.SameLine();
        if (isDirty)
        {
            ImGui.TextColored(new Vector4(1.0f, 0.75f, 0.2f, 1.0f), "[!] 檔案有未儲存的變更（請點擊「儲存至 CustomGlossary.json」按鈕以寫入檔案）");
        }
        else
        {
            ImGui.TextColored(new Vector4(0.4f, 1.0f, 0.5f, 1.0f), "[OK] 已與 CustomGlossary.json 同步");
        }

        if (!string.IsNullOrEmpty(saveStatusMessage))
        {
            ImGui.Spacing();
            ImGui.TextColored(new Vector4(0.35f, 0.95f, 0.6f, 1.0f), saveStatusMessage);
        }

        ImGui.Spacing();
        ImGui.TextDisabled($"獨立儲存檔案位置：{glossary.CustomGlossaryFilePath}");

        ImGui.Spacing();
        ImGui.Separator();
        ImGui.Spacing();

        // 2. Enable switch & count
        if (ImGui.Checkbox("啟用自訂名詞對照表 (Custom Glossary)", ref workingEnableCustom))
        {
            isDirty = true;
        }
        ImGui.SameLine();
        ImGui.TextDisabled("（自訂規則優先度高於官方辭典，且會自動注入 AI 提示詞與後處理取代）");

        int totalCount = workingEntries.Count;
        int enabledCount = workingEntries.Count(e => e.Enabled);
        ImGui.TextColored(new Vector4(0.85f, 0.85f, 0.85f, 1.0f), $"目前暫存清單：共 {totalCount} 筆對照規則（已啟用 {enabledCount} 筆）");
        ImGui.Spacing();

        // 3. Action Toolbar
        if (ImGui.Button("+ 新增對照項目", new Vector2(0, 28)))
        {
            StartNewGlossaryEntry();
        }
        ImGui.SameLine();
        if (ImGui.Button("載入經典預設範本", new Vector2(0, 28)))
        {
            LoadPresetGlossaryEntries();
        }
        ImGui.SameLine();
        if (ImGui.Button(showImportExportSection ? "關閉匯入/匯出" : "匯入 / 匯出", new Vector2(0, 28)))
        {
            showImportExportSection = !showImportExportSection;
            importExportStatus = string.Empty;
        }
        ImGui.SameLine();
        if (!showClearAllConfirm)
        {
            if (ImGui.Button("清空清單", new Vector2(0, 28)))
            {
                showClearAllConfirm = true;
            }
        }
        else
        {
            ImGui.TextColored(new Vector4(1f, 0.3f, 0.3f, 1f), "確定清空？");
            ImGui.SameLine();
            if (ImGui.Button("確認清空", new Vector2(0, 28)))
            {
                workingEntries.Clear();
                isDirty = true;
                showClearAllConfirm = false;
                isEditingGlossary = false;
            }
            ImGui.SameLine();
            if (ImGui.Button("取消", new Vector2(0, 28)))
            {
                showClearAllConfirm = false;
            }
        }

        ImGui.Spacing();

        // 4. Search Bar
        float clearBtnW = ImGui.CalcTextSize("清除搜尋").X + ImGui.GetStyle().FramePadding.X * 2 + 6;
        ImGui.SetNextItemWidth(ImGui.GetContentRegionAvail().X - clearBtnW - ImGui.GetStyle().ItemSpacing.X - 4);
        ImGui.InputTextWithHint("##GlossarySearch", "快速過濾日文 / 英文 / 繁中 / 簡中 / 備註...", ref glossarySearchFilter, 100);
        ImGui.SameLine();
        if (ImGui.Button("清除搜尋"))
        {
            glossarySearchFilter = string.Empty;
        }

        ImGui.Spacing();

        // 5. Import / Export drawer
        if (showImportExportSection)
        {
            DrawImportExportSection();
            ImGui.Spacing();
            ImGui.Separator();
            ImGui.Spacing();
        }

        // 6. Add / Edit Form
        if (isEditingGlossary)
        {
            DrawGlossaryEditForm();
            ImGui.Spacing();
            ImGui.Separator();
            ImGui.Spacing();
        }

        // 7. Glossary Entries Table
        DrawGlossaryTable();

        ImGui.Spacing();
        ImGui.Separator();
        ImGui.Spacing();

        // 8. Live Tester
        DrawLiveGlossaryTester();
    }

    private void DrawGlossaryEditForm()
    {
        bool isNew = string.IsNullOrEmpty(editingEntryId);
        ImGui.BeginGroup();
        ImGui.TextColored(new Vector4(1.0f, 0.85f, 0.35f, 1.0f), isNew ? "【新增自訂對照項目】" : "【編輯自訂對照項目】");
        ImGui.Spacing();

        float colWidth = (ImGui.GetContentRegionAvail().X - 24) / 2f;

        // Row 1: Japanese & English
        ImGui.BeginGroup();
        ImGui.Text("日文原文 (Japanese)：");
        ImGui.SetNextItemWidth(colWidth);
        ImGui.InputText("##EditJp", ref editJp, 100);
        ImGui.EndGroup();

        ImGui.SameLine(0, 16);

        ImGui.BeginGroup();
        ImGui.Text("英文原文 (English)：");
        ImGui.SetNextItemWidth(colWidth);
        ImGui.InputText("##EditEn", ref editEn, 100);
        ImGui.EndGroup();

        ImGui.Spacing();

        // Row 2: Traditional & Simplified Chinese
        ImGui.BeginGroup();
        ImGui.Text("繁體中文譯名 (Traditional Chinese)：");
        ImGui.SetNextItemWidth(colWidth);
        ImGui.InputText("##EditCht", ref editCht, 100);
        ImGui.EndGroup();

        ImGui.SameLine(0, 16);

        ImGui.BeginGroup();
        ImGui.Text("簡體中文譯名 (Simplified Chinese)：");
        ImGui.SetNextItemWidth(colWidth);
        ImGui.InputText("##EditChs", ref editChs, 100);
        ImGui.EndGroup();

        ImGui.Spacing();

        // Auto copy helper buttons
        if (ImGui.Button("繁中 -> 簡中", new Vector2(0, 26)))
        {
            if (!string.IsNullOrWhiteSpace(editCht)) editChs = editCht;
        }
        ImGui.SameLine();
        if (ImGui.Button("簡中 -> 繁中", new Vector2(0, 26)))
        {
            if (!string.IsNullOrWhiteSpace(editChs)) editCht = editChs;
        }
        ImGui.SameLine();
        if (ImGui.Button("日文 -> 英文", new Vector2(0, 26)))
        {
            if (!string.IsNullOrWhiteSpace(editJp)) editEn = editJp;
        }
        ImGui.SameLine();
        if (ImGui.Button("英文 -> 日文", new Vector2(0, 26)))
        {
            if (!string.IsNullOrWhiteSpace(editEn)) editJp = editEn;
        }

        ImGui.Spacing();

        // Row 3: Scopes
        ImGui.Checkbox("套用於說話者名稱 (Speaker)", ref editMatchSpeaker);
        ImGui.SameLine(0, 25);
        ImGui.Checkbox("套用於對話正文/名詞 (Dialogue)", ref editMatchDialogue);

        ImGui.Spacing();

        // Row 4: Notes
        ImGui.Text("備註 / 分類說明 (選填)：");
        ImGui.SetNextItemWidth(-1);
        ImGui.InputText("##EditNotes", ref editNotes, 100);

        if (!string.IsNullOrEmpty(editorValidationMsg))
        {
            ImGui.Spacing();
            ImGui.TextColored(new Vector4(1f, 0.4f, 0.4f, 1f), editorValidationMsg);
        }

        ImGui.Spacing();

        if (ImGui.Button("確認加入清單 (需點擊上方儲存)", new Vector2(0, 30)))
        {
            SaveCurrentEditEntry();
        }
        ImGui.SameLine();
        if (ImGui.Button("取消", new Vector2(0, 30)))
        {
            isEditingGlossary = false;
            editorValidationMsg = string.Empty;
        }

        ImGui.EndGroup();
    }

    private void DrawGlossaryTable()
    {
        var filter = glossarySearchFilter?.Trim() ?? string.Empty;

        var filtered = workingEntries.Where(e =>
            string.IsNullOrEmpty(filter) ||
            (!string.IsNullOrEmpty(e.Japanese) && e.Japanese.Contains(filter, StringComparison.OrdinalIgnoreCase)) ||
            (!string.IsNullOrEmpty(e.English) && e.English.Contains(filter, StringComparison.OrdinalIgnoreCase)) ||
            (!string.IsNullOrEmpty(e.TraditionalChinese) && e.TraditionalChinese.Contains(filter, StringComparison.OrdinalIgnoreCase)) ||
            (!string.IsNullOrEmpty(e.SimplifiedChinese) && e.SimplifiedChinese.Contains(filter, StringComparison.OrdinalIgnoreCase)) ||
            (!string.IsNullOrEmpty(e.Notes) && e.Notes.Contains(filter, StringComparison.OrdinalIgnoreCase))
        ).ToList();

        if (filtered.Count == 0)
        {
            ImGui.TextDisabled(string.IsNullOrEmpty(filter) ? "目前尚無任何自訂對照項目。點擊上方「+ 新增對照項目」或「載入經典預設範本」開始建立！" : "找不到符合搜尋條件的對照項目。");
            return;
        }

        var tableFlags = ImGuiTableFlags.Borders | ImGuiTableFlags.RowBg | ImGuiTableFlags.Resizable | ImGuiTableFlags.ScrollY | ImGuiTableFlags.NoSavedSettings;
        var tableSize = new Vector2(-1, 280);

        if (ImGui.BeginTable("CustomGlossaryStandaloneTableV3", 8, tableFlags, tableSize))
        {
            ImGui.TableSetupColumn("啟用", ImGuiTableColumnFlags.WidthFixed, 42);
            ImGui.TableSetupColumn("日文 (JP)", ImGuiTableColumnFlags.WidthStretch, 1.0f);
            ImGui.TableSetupColumn("英文 (EN)", ImGuiTableColumnFlags.WidthStretch, 1.0f);
            ImGui.TableSetupColumn("繁體中文 (CHT)", ImGuiTableColumnFlags.WidthStretch, 1.0f);
            ImGui.TableSetupColumn("簡體中文 (CHS)", ImGuiTableColumnFlags.WidthStretch, 1.0f);
            ImGui.TableSetupColumn("範圍", ImGuiTableColumnFlags.WidthFixed, 75);
            ImGui.TableSetupColumn("備註", ImGuiTableColumnFlags.WidthStretch, 0.8f);
            ImGui.TableSetupColumn("操作", ImGuiTableColumnFlags.WidthFixed, 200);
            ImGui.TableHeadersRow();

            CustomGlossaryEntry? toDelete = null;
            CustomGlossaryEntry? toClone = null;

            foreach (var entry in filtered)
            {
                ImGui.TableNextRow();

                // 1. Enabled
                ImGui.TableSetColumnIndex(0);
                var enabled = entry.Enabled;
                if (ImGui.Checkbox($"##en_{entry.Id}", ref enabled))
                {
                    entry.Enabled = enabled;
                    isDirty = true;
                }

                // 2. Japanese
                ImGui.TableSetColumnIndex(1);
                if (!string.IsNullOrWhiteSpace(entry.Japanese))
                {
                    ImGui.TextColored(new Vector4(0.5f, 0.9f, 1.0f, 1.0f), entry.Japanese);
                }
                else
                {
                    ImGui.TextDisabled("-");
                }

                // 3. English
                ImGui.TableSetColumnIndex(2);
                if (!string.IsNullOrWhiteSpace(entry.English))
                {
                    ImGui.TextColored(new Vector4(1.0f, 0.85f, 0.4f, 1.0f), entry.English);
                }
                else
                {
                    ImGui.TextDisabled("-");
                }

                // 4. Traditional Chinese
                ImGui.TableSetColumnIndex(3);
                if (!string.IsNullOrWhiteSpace(entry.TraditionalChinese))
                {
                    ImGui.TextColored(new Vector4(0.4f, 1.0f, 0.6f, 1.0f), entry.TraditionalChinese);
                }
                else
                {
                    ImGui.TextDisabled("-");
                }

                // 5. Simplified Chinese
                ImGui.TableSetColumnIndex(4);
                if (!string.IsNullOrWhiteSpace(entry.SimplifiedChinese))
                {
                    ImGui.Text(entry.SimplifiedChinese);
                }
                else
                {
                    ImGui.TextDisabled("-");
                }

                // 6. Scope
                ImGui.TableSetColumnIndex(5);
                if (entry.MatchSpeaker && entry.MatchDialogue)
                {
                    ImGui.Text("說話+內文");
                }
                else if (entry.MatchSpeaker)
                {
                    ImGui.Text("僅說話者");
                }
                else if (entry.MatchDialogue)
                {
                    ImGui.Text("僅內文");
                }
                else
                {
                    ImGui.TextDisabled("無");
                }

                // 7. Notes
                ImGui.TableSetColumnIndex(6);
                if (!string.IsNullOrWhiteSpace(entry.Notes))
                {
                    ImGui.TextDisabled(entry.Notes);
                }
                else
                {
                    ImGui.TextDisabled("-");
                }

                // 8. Actions
                ImGui.TableSetColumnIndex(7);
                ImGui.PushStyleVar(ImGuiStyleVar.FramePadding, new Vector2(6, 3));
                ImGui.PushStyleVar(ImGuiStyleVar.ItemSpacing, new Vector2(4, 0));
                if (ImGui.Button($"編輯##{entry.Id}"))
                {
                    StartEditGlossaryEntry(entry);
                }
                ImGui.SameLine();
                if (ImGui.Button($"複製##{entry.Id}"))
                {
                    toClone = entry;
                }
                ImGui.SameLine();
                if (ImGui.Button($"刪除##{entry.Id}"))
                {
                    toDelete = entry;
                }
                ImGui.PopStyleVar(2);
            }

            ImGui.EndTable();

            if (toClone != null)
            {
                var copy = toClone.Clone();
                if (!string.IsNullOrEmpty(copy.Notes)) copy.Notes += " (副本)";
                workingEntries.Add(copy);
                isDirty = true;
            }

            if (toDelete != null)
            {
                workingEntries.Remove(toDelete);
                isDirty = true;
                if (editingEntryId == toDelete.Id)
                {
                    isEditingGlossary = false;
                }
            }
        }
    }

    private void DrawImportExportSection()
    {
        ImGui.BeginGroup();
        ImGui.TextColored(new Vector4(0.4f, 0.8f, 1.0f, 1.0f), "【自訂對照表 匯入 / 匯出】");
        ImGui.Spacing();

        if (ImGui.Button("匯出為 JSON 到文字框", new Vector2(0, 28)))
        {
            var options = new JsonSerializerOptions { WriteIndented = true };
            importExportBuffer = JsonSerializer.Serialize(workingEntries, options);
            importExportStatus = $"已匯出 {workingEntries.Count} 筆對照為 JSON 格式。";
        }
        ImGui.SameLine();
        if (ImGui.Button("匯出為 CSV 到文字框", new Vector2(0, 28)))
        {
            var sb = new StringBuilder();
            sb.AppendLine("Japanese,English,TraditionalChinese,SimplifiedChinese,MatchSpeaker,MatchDialogue,Notes");
            foreach (var entry in workingEntries)
            {
                sb.AppendLine($"\"{entry.Japanese}\",\"{entry.English}\",\"{entry.TraditionalChinese}\",\"{entry.SimplifiedChinese}\",{entry.MatchSpeaker},{entry.MatchDialogue},\"{entry.Notes}\"");
            }
            importExportBuffer = sb.ToString();
            importExportStatus = $"已匯出 {workingEntries.Count} 筆對照為 CSV 格式。";
        }

        ImGui.Spacing();
        ImGui.SetNextItemWidth(-1);
        ImGui.InputTextMultiline("##ImportExportBuffer", ref importExportBuffer, 100000, new Vector2(-1, 140));

        ImGui.Spacing();
        if (ImGui.Button("從上方文字框匯入 (合併模式)", new Vector2(0, 28)))
        {
            ExecuteImport(false);
        }
        ImGui.SameLine();
        if (ImGui.Button("從上方文字框匯入 (覆蓋模式)", new Vector2(0, 28)))
        {
            ExecuteImport(true);
        }

        if (!string.IsNullOrEmpty(importExportStatus))
        {
            ImGui.Spacing();
            ImGui.TextColored(new Vector4(0.3f, 0.9f, 0.4f, 1.0f), importExportStatus);
        }

        ImGui.EndGroup();
    }

    private void DrawLiveGlossaryTester()
    {
        ImGui.TextColored(new Vector4(1.0f, 0.85f, 0.35f, 1.0f), "即時對照規則測試器 (Live Tester)");
        ImGui.TextDisabled("輸入測試的說話者姓名與日文/英文對話台詞，驗證清單內的對照規則是否能正確取代與匹配（含未儲存之最新項目）。");
        ImGui.Spacing();

        ImGui.Text("測試目標語言：");
        ImGui.SetNextItemWidth(320);
        int langIdx = testTargetLang == TargetLanguage.TraditionalChinese ? 0 : 1;
        string[] targetOptions = new[] { "繁體中文 (Traditional)", "簡體中文 (Simplified)" };
        if (ImGui.Combo("##TestLangCombo", ref langIdx, targetOptions, targetOptions.Length))
        {
            testTargetLang = langIdx == 0 ? TargetLanguage.TraditionalChinese : TargetLanguage.SimplifiedChinese;
        }

        ImGui.Spacing();
        ImGui.Text("測試說話者 (Speaker)：");
        ImGui.SetNextItemWidth(260);
        ImGui.InputText("##TestSpeakerInput", ref testSpeakerInput, 100);

        ImGui.Text("測試對話內容 (Dialogue)：");
        ImGui.SetNextItemWidth(-1);
        ImGui.InputText("##TestDialogueInput", ref testDialogueInput, 200);

        ImGui.Spacing();
        if (ImGui.Button("執行即時對照測試", new Vector2(0, 28)))
        {
            testResultSpeaker = TestTranslateSpeaker(testSpeakerInput, testTargetLang);
            testResultDialogue = TestApplyPostTranslation(testDialogueInput, testTargetLang);
            testMatchedRules = TestGetCustomGlossaryPrompt(testSpeakerInput, testDialogueInput, testTargetLang);
        }

        if (!string.IsNullOrEmpty(testResultSpeaker) || !string.IsNullOrEmpty(testResultDialogue))
        {
            ImGui.Spacing();
            ImGui.BeginChild("##TestResultsChild", new Vector2(-1, 105), true);
            ImGui.TextColored(new Vector4(0.4f, 0.8f, 1.0f, 1.0f), $"說話者對照結果：{testResultSpeaker}");
            ImGui.TextColored(new Vector4(0.4f, 1.0f, 0.6f, 1.0f), $"內文取代結果：{testResultDialogue}");
            if (!string.IsNullOrEmpty(testMatchedRules))
            {
                ImGui.Spacing();
                ImGui.TextColored(new Vector4(1.0f, 0.85f, 0.4f, 1.0f), "AI 提示詞注入規則：");
                ImGui.TextWrapped(testMatchedRules);
            }
            ImGui.EndChild();
        }
    }

    private string TestTranslateSpeaker(string speaker, TargetLanguage targetLang)
    {
        if (string.IsNullOrWhiteSpace(speaker)) return string.Empty;
        if (workingEnableCustom)
        {
            var trimmed = speaker.Trim();
            foreach (var entry in workingEntries)
            {
                if (!entry.Enabled || !entry.MatchSpeaker) continue;

                bool isMatch = false;
                if (!string.IsNullOrWhiteSpace(entry.Japanese) && string.Equals(trimmed, entry.Japanese.Trim(), StringComparison.OrdinalIgnoreCase)) isMatch = true;
                else if (!string.IsNullOrWhiteSpace(entry.English) && string.Equals(trimmed, entry.English.Trim(), StringComparison.OrdinalIgnoreCase)) isMatch = true;
                else if (!string.IsNullOrWhiteSpace(entry.TraditionalChinese) && string.Equals(trimmed, entry.TraditionalChinese.Trim(), StringComparison.OrdinalIgnoreCase)) isMatch = true;
                else if (!string.IsNullOrWhiteSpace(entry.SimplifiedChinese) && string.Equals(trimmed, entry.SimplifiedChinese.Trim(), StringComparison.OrdinalIgnoreCase)) isMatch = true;

                if (isMatch)
                {
                    var customResult = targetLang == TargetLanguage.TraditionalChinese ? entry.TraditionalChinese : entry.SimplifiedChinese;
                    if (!string.IsNullOrWhiteSpace(customResult)) return customResult.Trim();
                }
            }
        }
        return glossary.TranslateSpeaker(speaker, targetLang);
    }

    private string TestApplyPostTranslation(string text, TargetLanguage targetLang)
    {
        if (string.IsNullOrEmpty(text)) return string.Empty;
        text = glossary.ApplyPostTranslation(text, targetLang);

        if (workingEnableCustom)
        {
            foreach (var entry in workingEntries)
            {
                if (!entry.Enabled || !entry.MatchDialogue) continue;

                var targetReplacement = targetLang == TargetLanguage.TraditionalChinese ? entry.TraditionalChinese : entry.SimplifiedChinese;
                if (string.IsNullOrWhiteSpace(targetReplacement)) continue;
                targetReplacement = targetReplacement.Trim();

                if (!string.IsNullOrWhiteSpace(entry.Japanese) && text.Contains(entry.Japanese.Trim()))
                    text = text.Replace(entry.Japanese.Trim(), targetReplacement);

                if (!string.IsNullOrWhiteSpace(entry.English) && text.Contains(entry.English.Trim(), StringComparison.OrdinalIgnoreCase))
                    text = Regex.Replace(text, Regex.Escape(entry.English.Trim()), targetReplacement, RegexOptions.IgnoreCase);

                if (targetLang == TargetLanguage.TraditionalChinese && !string.IsNullOrWhiteSpace(entry.SimplifiedChinese) && text.Contains(entry.SimplifiedChinese.Trim()))
                    text = text.Replace(entry.SimplifiedChinese.Trim(), targetReplacement);
                else if (targetLang == TargetLanguage.SimplifiedChinese && !string.IsNullOrWhiteSpace(entry.TraditionalChinese) && text.Contains(entry.TraditionalChinese.Trim()))
                    text = text.Replace(entry.TraditionalChinese.Trim(), targetReplacement);
            }
        }
        return text;
    }

    private string TestGetCustomGlossaryPrompt(string? speaker, string text, TargetLanguage targetLang)
    {
        if (!workingEnableCustom || workingEntries.Count == 0) return string.Empty;

        var relevantRules = new List<string>();
        foreach (var entry in workingEntries)
        {
            if (!entry.Enabled) continue;

            var targetTranslation = targetLang == TargetLanguage.TraditionalChinese ? entry.TraditionalChinese : entry.SimplifiedChinese;
            if (string.IsNullOrWhiteSpace(targetTranslation)) continue;

            bool isRelevant = false;
            if (!string.IsNullOrEmpty(speaker))
            {
                if (!string.IsNullOrEmpty(entry.Japanese) && speaker.Contains(entry.Japanese.Trim(), StringComparison.OrdinalIgnoreCase)) isRelevant = true;
                if (!string.IsNullOrEmpty(entry.English) && speaker.Contains(entry.English.Trim(), StringComparison.OrdinalIgnoreCase)) isRelevant = true;
            }

            if (!isRelevant && !string.IsNullOrEmpty(text))
            {
                if (!string.IsNullOrEmpty(entry.Japanese) && text.Contains(entry.Japanese.Trim())) isRelevant = true;
                if (!string.IsNullOrEmpty(entry.English) && text.Contains(entry.English.Trim(), StringComparison.OrdinalIgnoreCase)) isRelevant = true;
            }

            if (isRelevant)
            {
                var src = !string.IsNullOrEmpty(entry.Japanese) ? entry.Japanese.Trim() : entry.English.Trim();
                if (!string.IsNullOrEmpty(entry.Japanese) && !string.IsNullOrEmpty(entry.English) && !entry.Japanese.Equals(entry.English, StringComparison.OrdinalIgnoreCase))
                {
                    src = $"{entry.Japanese.Trim()} / {entry.English.Trim()}";
                }
                relevantRules.Add($"- {src} => {targetTranslation.Trim()}");
            }
        }

        if (relevantRules.Count == 0) return string.Empty;
        return "\n【專有名詞與人物名稱強制對照表（請嚴格遵守）】：\n" + string.Join("\n", relevantRules) + "\n";
    }

    private void StartNewGlossaryEntry()
    {
        isEditingGlossary = true;
        editingEntryId = null;
        editJp = string.Empty;
        editEn = string.Empty;
        editCht = string.Empty;
        editChs = string.Empty;
        editMatchSpeaker = true;
        editMatchDialogue = true;
        editNotes = string.Empty;
        editorValidationMsg = string.Empty;
    }

    private void StartEditGlossaryEntry(CustomGlossaryEntry entry)
    {
        isEditingGlossary = true;
        editingEntryId = entry.Id;
        editJp = entry.Japanese;
        editEn = entry.English;
        editCht = entry.TraditionalChinese;
        editChs = entry.SimplifiedChinese;
        editMatchSpeaker = entry.MatchSpeaker;
        editMatchDialogue = entry.MatchDialogue;
        editNotes = entry.Notes;
        editorValidationMsg = string.Empty;
    }

    private void SaveCurrentEditEntry()
    {
        if (string.IsNullOrWhiteSpace(editJp) && string.IsNullOrWhiteSpace(editEn))
        {
            editorValidationMsg = "請至少填寫「日文」或「英文」其中一項原文！";
            return;
        }

        if (string.IsNullOrWhiteSpace(editCht) && string.IsNullOrWhiteSpace(editChs))
        {
            editorValidationMsg = "請至少填寫「繁體中文」或「簡體中文」其中一項譯名！";
            return;
        }

        // Auto fallback if one Chinese is empty
        if (string.IsNullOrWhiteSpace(editCht)) editCht = editChs;
        if (string.IsNullOrWhiteSpace(editChs)) editChs = editCht;

        if (string.IsNullOrEmpty(editingEntryId))
        {
            var entry = new CustomGlossaryEntry
            {
                Japanese = editJp.Trim(),
                English = editEn.Trim(),
                TraditionalChinese = editCht.Trim(),
                SimplifiedChinese = editChs.Trim(),
                MatchSpeaker = editMatchSpeaker,
                MatchDialogue = editMatchDialogue,
                Notes = editNotes.Trim()
            };
            workingEntries.Add(entry);
            isDirty = true;
        }
        else
        {
            var existing = workingEntries.Find(e => e.Id == editingEntryId);
            if (existing != null)
            {
                existing.Japanese = editJp.Trim();
                existing.English = editEn.Trim();
                existing.TraditionalChinese = editCht.Trim();
                existing.SimplifiedChinese = editChs.Trim();
                existing.MatchSpeaker = editMatchSpeaker;
                existing.MatchDialogue = editMatchDialogue;
                existing.Notes = editNotes.Trim();
                isDirty = true;
            }
        }

        isEditingGlossary = false;
        editorValidationMsg = string.Empty;
    }

    private void LoadPresetGlossaryEntries()
    {
        var presets = GlossaryManager.GetDefaultPresets();
        int added = 0;
        foreach (var p in presets)
        {
            bool exists = workingEntries.Any(e =>
                (!string.IsNullOrEmpty(p.Japanese) && string.Equals(e.Japanese, p.Japanese, StringComparison.OrdinalIgnoreCase)) ||
                (!string.IsNullOrEmpty(p.English) && string.Equals(e.English, p.English, StringComparison.OrdinalIgnoreCase)));

            if (!exists)
            {
                workingEntries.Add(p.Clone());
                added++;
            }
        }

        if (added > 0)
        {
            isDirty = true;
        }
    }

    private void ExecuteImport(bool overwrite)
    {
        if (string.IsNullOrWhiteSpace(importExportBuffer))
        {
            importExportStatus = "匯入失敗：文字框為空！";
            return;
        }

        try
        {
            var trimmed = importExportBuffer.Trim();
            List<CustomGlossaryEntry> imported = new();

            if (trimmed.StartsWith("["))
            {
                var list = JsonSerializer.Deserialize<List<CustomGlossaryEntry>>(trimmed);
                if (list != null) imported = list;
            }
            else
            {
                using var reader = new StringReader(trimmed);
                string? line;
                bool isHeader = true;
                while ((line = reader.ReadLine()) != null)
                {
                    if (string.IsNullOrWhiteSpace(line)) continue;
                    if (isHeader && (line.StartsWith("Japanese", StringComparison.OrdinalIgnoreCase) || line.StartsWith("\"Japanese", StringComparison.OrdinalIgnoreCase)))
                    {
                        isHeader = false;
                        continue;
                    }
                    isHeader = false;

                    var parts = ParseCsvLine(line);
                    if (parts.Count >= 4)
                    {
                        var entry = new CustomGlossaryEntry
                        {
                            Japanese = parts[0],
                            English = parts[1],
                            TraditionalChinese = parts[2],
                            SimplifiedChinese = parts[3],
                            MatchSpeaker = parts.Count > 4 ? bool.TryParse(parts[4], out var ms) && ms : true,
                            MatchDialogue = parts.Count > 5 ? bool.TryParse(parts[5], out var md) && md : true,
                            Notes = parts.Count > 6 ? parts[6] : string.Empty
                        };
                        imported.Add(entry);
                    }
                }
            }

            if (imported.Count == 0)
            {
                importExportStatus = "未能從輸入內容解析出任何對照項目，請檢查格式。";
                return;
            }

            if (overwrite)
            {
                workingEntries.Clear();
            }

            foreach (var item in imported)
            {
                if (overwrite || !workingEntries.Any(e => e.Id == item.Id))
                {
                    workingEntries.Add(item);
                }
            }

            isDirty = true;
            importExportStatus = $"已匯入 {imported.Count} 筆對照到暫存！請點擊上方「儲存並套用變更」按鈕寫入生效。";
        }
        catch (Exception ex)
        {
            importExportStatus = $"匯入發生錯誤：{ex.Message}";
        }
    }

    private static List<string> ParseCsvLine(string line)
    {
        var result = new List<string>();
        bool inQuotes = false;
        var current = new StringBuilder();

        for (int i = 0; i < line.Length; i++)
        {
            char c = line[i];
            if (c == '\"')
            {
                inQuotes = !inQuotes;
            }
            else if (c == ',' && !inQuotes)
            {
                result.Add(current.ToString().Trim());
                current.Clear();
            }
            else
            {
                current.Append(c);
            }
        }
        result.Add(current.ToString().Trim());
        return result;
    }
}
