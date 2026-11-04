<div align="center">
  <img src="images/icon.png" width="128" height="128" alt="Tataru Native Translator Logo" />
  <h1>Tataru Native Translator (Dalamud-Tataru)</h1>
  <p><strong>一款為 Final Fantasy XIV (FFXIV) 玩家打造的原生遊戲內即時劇情翻譯插件</strong></p>
</div>

一款為 **Final Fantasy XIV (FFXIV)** 玩家打造的**原生遊戲內即時劇情翻譯插件**。  
整合 **[Tataru Assistant](https://github.com/winw1010/tataru-assistant)** 的專有名詞與人名辭典，並透過 **Google Gemini AI** / **本地 LM Studio 大模型** 進行非同步即時翻譯。

---

## 核心特色

- **純原生遊戲內運行**：直接作為 Dalamud Plugin 運行於遊戲內部，**不需要開啟任何外部 exe 程式或 Electron 視窗**。
- **極致精準的對話攔截**：透過 Dalamud 記憶體直接抓取遊戲 UI 文字（`AddonTalk` 主線支線對話、`TalkSubtitle` 過場 CG 字幕、`AddonBattleTalk` 副本首領喊話），零延遲且絕不漏字，再也不用依賴脆弱的 OCR 或外部指針掃描。
- **Tataru 繁簡專有名詞辭典整合**：內建 Tataru 原版 `jp-ch-name.json` 與 `after-translation-*.json` 譯後名詞校正，NPC 姓名與艾歐澤亞專有名詞精準對齊官方中文在地化。
- **Gemini AI 智能翻譯**：預設採用最新 `gemini-3.8-flash`（並支援一鍵從 Google API 同步最新模型清單），翻譯語氣自然流暢、反應迅速，且具備本地快取機制（重複對話秒出，省 API 額度）。
- **多語自訂對照表 (Custom Glossary)**：全新支援「日文 (JP) / 英文 (EN) / 繁體中文 (CHT) / 簡體中文 (CHS)」四語自訂名詞對照，獨立以 `CustomGlossary.json` 儲存，安全分享無資安顧慮，並自動注入 AI 提示詞與後處理雙重校正。
- **雙模式自由切換**：
  - **沉浸式原生對話框覆蓋模式 (Replace Mode)**：保留遊戲原生對話泡泡底圖與跳動金箭頭，直接在原對話框內覆蓋流暢中文譯文，宛如官方繁中版體驗。
  - **輕量化 ImGui 字幕懸浮窗**：獨立自訂懸浮視窗，支援說話者名稱顯示、中日雙語即時對照、滑鼠穿透（Click-Through）、自訂字體與透明度、對話結束自動淡出隱藏。

---

## 沉浸式原生對話框覆蓋模式 (Native Bubble Overlay)

<div align="center">
  <img src="image/OverlayToOriginalTextBubble.png" width="480" alt="原生對話框覆蓋模式效果展示" />
  <p><em>▲ 保留原版對話框底圖與跳動金箭頭，無縫覆蓋為流暢中文譯文</em></p>
</div>

### 模式特色
- **極致原生的沉浸體驗**：
  - **方案 B（保留遊戲原生泡泡底圖）**：精準隱藏原版日文文字節點，**完整保留遊戲原生繪製的羊皮紙對話框底圖與翻頁金色箭頭游標**，並在原框內以深色中文字體直接覆蓋渲染，視覺體驗完全無縫（如上圖所示）。
  - **方案 A（仿原版羊皮紙風格）**：高仿 FFXIV 羊皮紙底色與古典深棕外框，支援自由任意拉伸縮放視窗尺寸，適合長篇巨幅對話或雙語同時展示。
- **自動對齊與尺寸動態同步**：
  - 視窗可自動鎖定遊戲原生 NPC 對話框的螢幕座標，並 100% 動態貼合原版對話框實際尺寸（包含 HUD 縮放倍率）。
  - 支援 X / Y 座標與寬高尺寸的像素級微調偏移。
  - 設定視窗開啟時，覆蓋視窗會常駐顯示並允許直接以滑鼠拖曳擺放或拉伸邊框，設定值自動永久記憶。
- **彈性雙語對照設定**：
  - 支援「純中文譯文」模式（乾淨簡潔、防長句溢出）或「中日雙語對照」模式。
  - 支援獨立微調日文原文縮放比例（預設 0.75 倍），對照清晰又不搶版面。
- **一鍵快速切換**：
  - 遊戲聊天框輸入 `/tataru replace`（或 `/tataru replace on` / `off`）即可隨時在「獨立字幕視窗」與「原生對話框覆蓋」之間自由切換。

---

## 多語自訂名詞對照表 (Custom Glossary Editor)

提供玩家強大的自訂名詞對照功能，讓所有 NPC 譯名、特有名詞、技能與地名完全符合個人喜好或公會慣用語：

- **四語同步對照**：支援日文原文 (JP)、英文原文 (EN)、繁體中文譯名 (CHT)、簡體中文譯名 (CHS)。
- **獨立儲存檔案 `CustomGlossary.json`**：對照表儲存於獨立檔案（`%APPDATA%\XIVLauncher\pluginConfigs\DalamudTataru\CustomGlossary.json`），與包含 API Key 的插件總設定檔完全隔離，可安心將對照表檔案分享給好友或公會隊友。
- **獨立彈出視窗編輯**：支援一鍵開啟為獨立視窗（`/tataru glossary`），可自由拖曳至副螢幕或全螢幕縮放，多螢幕對照零負擔。
- **手動儲存與還原控制**：取消容易手滑誤觸的即時自動儲存，編輯期間提供記憶體暫存管理，點擊「💾 儲存至 CustomGlossary.json」才寫入生效，並提供「🔄 放棄修改 / 重新載入」一鍵復原。
- **雙重精準套用管線**：
  1. **說話者名稱優先取代**：無論日服（日語）或國際服（英語），優先以自訂對照表為準。
  2. **AI Prompt 動態注入**：當對話或說話者命中自訂術語時，自動將對照規則注入 Gemini / LM Studio 提示詞，讓 AI 翻譯時依據文脈自然譯出，而非事後粗暴字串硬轉。
  3. **終端譯後校正**：確保譯文絕不漏翻或殘留日文/英文未譯名詞。
- **經典範本一鍵載入**：內建 22+ 筆拂曉幹部（塔塔露、阿爾菲諾等）、7.0 核心角色（烏克拉瑪特、圖萊尤拉等）與世界觀專有名詞（光之戰士、拂曉血盟、乙太、蠻神、艾歐澤亞等）。
- **即時對照測試器 (Live Tester)**：在編輯器底部可即時輸入說話者與台詞進行規則測試，確認取代結果與注入規則。
- **JSON / CSV 匯入與匯出**：完整支援文字框複製貼上、備份與批次匯入（支援合併或覆蓋）。

---

## 專案目錄結構

```
Dalumud-Tataru/
├── Data/
│   ├── jp-ch-name.json             # 日中 NPC/人名對照表 (來自 Tataru 原版)
│   ├── en-ch-name.json             # 英中 NPC/專有名詞對照表
│   ├── after-translation-cht.json  # 繁體中文譯後名詞校正表
│   └── after-translation-chs.json  # 簡體中文譯後名詞校正表
├── Models/
│   ├── DialogueEntry.cs            # 對話資料模型與語言設定定義
│   ├── CustomGlossaryEntry.cs      # 自訂多語名詞對照表資料結構
│   ├── ChatChannelHelper.cs        # 聊天頻道與系統訊息分類/色彩對照定義
│   └── ChatHistoryEntry.cs         # 聊天翻譯歷史資料結構
├── Services/
│   ├── DialogueWatcher.cs          # 遊戲原生對話/字幕視窗監聽器
│   ├── ChatTranslationService.cs   # 對話欄與系統訊息即時監聽、非同步翻譯與輸出服務
│   ├── GeminiTranslator.cs         # Google Gemini REST API 非同步翻譯與快取服務
│   ├── LmStudioTranslator.cs       # LM Studio 本地大模型翻譯服務
│   └── GlossaryManager.cs          # 專有名詞讀取、自訂對照表持久化與文本校正服務
├── Windows/
│   ├── ConfigWindow.cs             # 插件設定介面 (AI、聊天頻道、自訂對照表、提示詞、外觀)
│   ├── ChatHistoryWindow.cs        # 獨立聊天與系統翻譯歷史紀錄懸浮視窗
│   ├── GlossaryEditorWindow.cs     # 獨立自訂名詞對照表編輯器視窗 (支援手動儲存與測試)
│   ├── SubtitleOverlayWindow.cs    # ImGui 原生字幕懸浮視窗
│   └── DialogueReplaceOverlayWindow.cs # 原生對話框覆蓋視窗 (羊皮紙/原生泡泡模式)
├── Configuration.cs                # 插件設定資料結構與持久化
├── Plugin.cs                       # Dalamud 插件主入口
├── DalamudTataru.csproj            # 專案建置檔 (基於 Dalamud.NET.Sdk)
└── DalamudTataru.json              # Dalamud 插件 Manifest
```

---

## 安裝方式 (Dalamud 自訂插件庫)

1. 在遊戲中輸入 `/xlsettings` 開啟 Dalamud 設定視窗。
2. 切換至 **「實驗性功能 (Experimental)」** 分頁。
3. 找到 **「自訂插件庫 (Custom Plugin Repositories)」** 區塊，新增以下網址並儲存：
   ```
   https://raw.githubusercontent.com/tcfshcrw/Dalumud-Tataru/main/pluginmaster.json
   ```
4. 開啟插件安裝器 (`/xlplugins`)，搜尋 **Tataru Native Translator** 即可一鍵安裝與自動更新！

---

## 快速上手

### 1. 取得免費的 Gemini API Key
1. 前往 [Google AI Studio](https://aistudio.google.com/)。
2. 登入 Google 帳號後點擊 **Get API key** -> **Create API key**。
3. 複製產生的 API Key。

### 2. 在遊戲內設定
1. 載入本插件後，於遊戲聊天框輸入指令：
   ```
   /tataru
   ```
2. 在彈出的設定視窗中貼上你的 **Gemini API Key**。
3. 點擊「**測試 API 連線與翻譯**」，確認顯示「測試成功」即可開始體驗！

---

## 遊戲內指令

| 指令 | 說明 |
| :--- | :--- |
| `/tataru on` / `/tataru off` | **開啟 / 關閉對話翻譯功能**（狀態將永久保存，下次啟動自動套用） |
| `/tataru status` | 查看當前翻譯開關、主翻譯引擎與視窗模式狀態 |
| `/tataru` 或 `/tataru config` | 開啟 / 關閉主設定視窗 |
| `/tataru chat` | 開啟 / 關閉**獨立對話與系統翻譯紀錄懸浮窗** |
| `/tataru glossary` 或 `/tataru dict` | 開啟 / 關閉**獨立自訂名詞對照表編輯器視窗** |
| `/tataru toggle` | 開啟 / 關閉字幕視窗（可用於手動調整視窗位置） |
| `/tataru replace` | 開關原生對話框覆蓋模式（直接在原生對話框覆蓋中文譯文，支援 `on`/`off`） |
| `/tataru gemini on` | 切換主翻譯引擎為 Gemini |
| `/tataru gemini off` | 切換主翻譯引擎為有道 (Youdao) |
| `/tataru LMS` | 開關或切換主翻譯引擎為 LM Studio 本地模型（支援 `/tataru LMS on` / `/tataru LMS off`） |
| `/tataru gemini` | 在 Gemini 與有道翻譯之間切換 |
| `/tataru clearcache` | 清除目前記憶體中所有的對話翻譯暫存快取 |
| `/tataru update` | 直接從 Tataru-Assistant 雲端更新最新專有名詞與譯後修飾文本庫 |

---

## 致敬與感謝

- 感謝 [winw1010/tataru-assistant](https://github.com/winw1010/tataru-assistant) 團隊為 FF14 中文社群長期整理的專有名詞庫與譯名對照表。
- 感謝 **Dalamud** 開發團隊提供優秀的 FFXIV 插件生態。
