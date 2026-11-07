<div align="center">
  <img src="images/icon.png" width="128" height="128" alt="Tataru Native Translator Logo" />
  <h1>Tataru Native Translator (Dalamud-Tataru)</h1>
  <p><strong>一款為 Final Fantasy XIV (FFXIV) 玩家打造的原生遊戲內即時劇情、任務與聊天翻譯插件</strong></p>
</div>

一款為 **Final Fantasy XIV (FFXIV)** 玩家打造的**原生遊戲內即時全方位翻譯插件**。  
整合 **[Tataru Assistant](https://github.com/winw1010/tataru-assistant)** 的專有名詞與人名辭典，並透過 **Google Gemini AI** / **本地 LM Studio 大模型** / **有道翻譯** / **Google 翻譯** 進行非同步即時翻譯。

---

## 核心特色

- **純原生遊戲內運行**：直接作為 Dalamud Plugin 運行於遊戲內部，**不需要開啟任何外部 exe 程式或 Electron 視窗**。
- **極致精準的對話攔截**：透過 Dalamud 記憶體直接抓取遊戲 UI 文字（`AddonTalk` 主線支線對話、`TalkSubtitle` 過場 CG 字幕、`AddonBattleTalk` 副本首領喊話），零延遲且絕不漏字，再也不用依賴脆弱的 OCR 或外部指針掃描。
- **任務內容滑鼠懸浮即時翻譯 (Quest Mouse Hover Overlay)**：
  - **滑鼠懸停直覺顯現**：將滑鼠移至任務詳細說明、接取委託書或回報結算內容文字上方時，自動在游標旁彈出深色半透明圓角 ImGui 懸浮翻譯視窗；滑鼠移開立即隱藏。
  - **智慧排除任務標題**：刻意保留任務原有名稱（不翻譯任務標題），只翻譯背景故事與目標內容，確保玩家能無縫對照外部 Wiki 攻略與遊戲原地圖導航。
  - **Gemini API 額度保護防線**：任務說明文字繁多，為避免瞬間耗盡寶貴的 Gemini 每分鐘免費請求額度（Rate Limit），任務翻譯自動限定由 **LM Studio 本地大模型**、**有道翻譯 (Youdao)** 或 **Google 翻譯** 執行，無額度負擔。
  - **支援三大任務視窗與歷程回顧**：全面支援 `JournalDetail`（日誌詳細說明欄與前回冒險錄 / 歷程回顧長文）、`JournalAccept`（接取任務委託書）、`JournalResult`（回報任務結算）。
- **劇情與 NPC 對話選項懸浮翻譯 (SelectString / SelectIconString)**：
  - 遊戲中出現分支對話選項（如「你要怎麼回答？」）或圖示選項時，自動於原生選項框旁邊以 ImGui 懸浮視窗展示即時中文翻譯。
  - **智慧自適應定位**：優先吸附於原生選項框右側，若超出螢幕邊界則智慧自動切換為靠左側吸附，支援位置微調與字體縮放。
  - **清晰標號與原文對照**：以清楚的 1、2、3 數字標號對應原生選項，支援可選的日文/英文原文對照，完全不影響遊戲原生點擊。
- **內建任務節點深層診斷除錯器 (Journal Inspector)**：
  - 提供專屬「任務除錯 (Inspector)」面板，具備遊戲原生 UI 樹、組件節點（Component Node >= 1000）、JournalCanvas 畫布項目與按鈕文字節點深層掃描能力。
  - 支援在遊戲螢幕上以彩色邊界框 (Bounding Box) 視覺化標記節點位置、Node ID、尺寸與偵測狀態，方便排查與確認文字覆蓋範圍。
- **全聊天頻道即時翻譯 (Chat Translation)**：
  - 支援小隊、部隊、大喊、密語、說話、團隊、通訊貝 (LS)、跨服通訊貝 (CWLS)、系統訊息等細緻頻道分類與過濾。
  - **攔截並隱藏原生遊戲訊息**：可選擇直接以譯文替換原文（`ChatSuppressOriginal`），避免聊天框同時出現日文與中文重複兩行；翻譯失敗或無需翻譯時自動安全還原原生訊息。
  - 輸出格式自由切換：支援「僅輸出翻譯內容」或「原文 [譯] 譯文」雙語對照。
  - 可選擇「直接輸出至聊天視窗（支援原味色彩標籤與自訂前綴）」或「開啟獨立聊天翻譯歷史視窗 (`/tataru chat`)」。
  - 內建巨集過濾、網址忽略、自己發言過濾與副本自動啟用開關。
- **遊戲字型缺字保護與自訂字形映射表 (Custom Glyph Mappings)**：
  - 自動比對遊戲原裝字型（`axis_12.fdt`）字元集，將日語客戶端未收錄之中文漢字無損替換為同形字或相似字（如「薫」->「薫」），徹底杜絕介面出現「?」或缺字方塊。
  - **自訂字形映射表編輯器**：支援自由新增與刪除字元替換對照（如將「卡」轉為「カ」避免在日服聊天框變成「＝」等號）。
  - 提供常用字庫一鍵填入與即時輸入預覽測試器 (Live Preview)。
- **Tataru 專有名詞辭典與四語自訂名詞對照表 (Custom Glossary)**：
  - 內建 Tataru 原版 `jp-ch-name.json` 與 `after-translation-*.json` 譯後校正，擴充收錄住宅區 (Housing Districts) 地名、最新 7.0 關鍵角色與世界觀術語。
  - **四語同步對照**：支援日文原文 (JP)、英文原文 (EN)、繁體中文 (CHT)、簡體中文 (CHS)。
  - **譯前預先替換 (Pre-Translation Replacement)**：AI 翻譯前先標記與鎖定專有名詞，大幅提升長句翻譯的術語一致性。
  - **獨立儲存檔案 `CustomGlossary.json`**：安全隔離 API Key，可安心分享對照表；編輯儲存後自動即時清除翻譯快取。
- **多引擎無縫切換與備援防線 (Multi-Engine & Fallback Pipeline)**：
  - **主引擎**：預設採用最新 `gemini-3.8-flash` 或本機部署的 LM Studio 大模型。
  - **智慧備援**：當 Gemini 或 LM Studio 額度受限或網路逾時，自動無縫切換至有道翻譯與 100% 免費的 Google Translate 備用防線。
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

## 任務內容滑鼠懸浮翻譯 (Quest Mouse Hover Overlay)

為了給玩家帶來最穩定且直覺的任務閱讀體驗，插件採用了 **滑鼠懸停 ImGui 懸浮窗 (Hover Overlay)** 機制：

1. **直覺的滑鼠懸停閱讀**：
   - 當滑鼠移至任務說明或目標文字上方時，自動在滑鼠游標右下方彈出 50% 半透明深色圓角懸浮翻譯視窗。
   - 滑鼠移開即自動淡出隱藏，完全不遮擋遊戲介面。
2. **智慧排除任務標題**：
   - 刻意保留原生任務名稱，只翻譯詳細任務內容、委託背景與目標要求。
   - 避免翻譯標題後無法在外部 Wiki、攻略站或地圖上快速搜尋對應任務。
3. **繞過 Gemini 額度保護防線**：
   - 任務日誌文字量大且查閱頻繁，為守護玩家寶貴的 Gemini API 每分鐘免費額度（Rate Limit），任務翻譯會自動改由 **LM Studio 本地模型**、**有道翻譯** 或 **Google 翻譯** 執行，無後顧之憂。
4. **完整涵蓋各類任務介面**：
   - **`JournalDetail`（任務詳細說明欄）**：日誌面板與地圖任務點擊彈出的任務詳細設定、步驟目標，以及「前回冒險錄 / 歷程回顧 (JournalRecap)」長文。
   - **`JournalAccept`（接取任務確認視窗）**：NPC 接取任務時的羊皮紙委託書說明內容。
   - **`JournalResult`（回報任務結算視窗）**：任務完成交差時的獎勵與結算劇情總結。
5. **任務節點除錯診斷器 (Journal Inspector)**：
   - 設定視窗提供獨立「任務除錯 (Inspector)」分頁。
   - 深入檢測遊戲原生 UI 樹、組件節點（Component Node >= 1000）、JournalCanvas 畫布項目與按鈕文字節點。
   - 支援即時在螢幕上以彩色邊界框標記節點座標、寬高、Node ID 與提取文字，診斷覆蓋狀況一目了然。

---

## 全聊天頻道即時翻譯 (Chat Translation)

- **細緻頻道過濾**：自由勾選說話 (Say)、小隊 (Party)、部隊 (FC)、大喊 (Yell/Shout)、密語 (Tell)、團隊 (Alliance)、所有通訊貝 (LS 1~8)、跨服通訊貝 (CWLS 1~8) 以及各類系統提示訊息。
- **攔截並隱藏原生遊戲訊息 (ChatSuppressOriginal)**：
  - 勾選後，符合條件的日/英文聊天訊息會直接被攔截，等翻譯完成後只輸出中文譯文，徹底告別「日文一行、中文一行」的冗長洗版。
  - 內建安全還原機制：若訊息無需翻譯或翻譯發生異常，自動還原印出原始訊息，不漏任何重要通知。
- **雙重視窗模式**：
  - 輸出至遊戲聊天視窗：支援原色前綴標籤高亮與 Echo 獨立頻道分流。
  - 獨立歷史視窗：輸入 `/tataru chat` 隨時喚出懸浮歷史清單，支援全文搜尋與即時過濾。

---

## 遊戲字型缺字保護與自訂字形映射表 (Custom Glyph Mappings)

- **AXIS 字型缺字預防 (GlyphFolder)**：比對客戶端字元庫，將未收錄漢字轉為同形字或相似字（如「薫」->「薫」），防止遊戲畫面出現「?」或缺字方塊。
- **自訂字形映射表編輯器**：
  - 針對日語客戶端容易缺字的情況（如「卡」字在日版聊天框會變成「＝」等號），玩家可自訂將「卡」替換為片假名「カ」等相似字。
  - 內建常用推薦字庫一鍵填入。
  - 附帶即時測試預覽框 (Live Preview)，輸入任何字詞立即檢驗替換成果。

---

## 多語自訂名詞對照表 (Custom Glossary Editor)

提供玩家強大的自訂名詞對照功能，讓所有 NPC 譯名、特有名詞、技能與地名完全符合個人喜好或公會慣用語：

- **四語同步對照**：支援日文原文 (JP)、英文原文 (EN)、繁體中文譯名 (CHT)、簡體中文譯名 (CHS)。
- **獨立儲存檔案 `CustomGlossary.json`**：對照表儲存於獨立檔案（`%APPDATA%\XIVLauncher\pluginConfigs\DalamudTataru\CustomGlossary.json`），與包含 API Key 的插件總設定檔完全隔離，可安心將對照表檔案分享給好友或公會隊友。
- **獨立彈出視窗編輯**：支援一鍵開啟為獨立視窗（`/tataru glossary`），可自由拖曳至副螢幕或全螢幕縮放，多螢幕對照零負擔。
- **手動儲存與還原控制**：取消容易手滑誤觸的即時自動儲存，編輯期間提供記憶體暫存管理，點擊「💾 儲存至 CustomGlossary.json」才寫入生效，並提供「🔄 放棄修改 / 重新載入」一鍵復原。
- **多重精準套用管線**：
  1. **說話者名稱優先取代**：無論日服（日語）或國際服（英語），優先以自訂對照表為準。
  2. **譯前預先替換 (Pre-Translation Replacement)**：AI 翻譯前先對目標術語進行結構保護，防止 AI 隨意發揮。
  3. **AI Prompt 動態注入**：當對話或說話者命中自訂術語時，自動將對照規則注入 Gemini / LM Studio 提示詞，讓 AI 依據文脈自然譯出。
  4. **終端譯後校正**：確保譯文絕不漏翻或殘留未譯名詞。
- **經典範本一鍵載入**：內建 22+ 筆拂曉幹部（塔塔露、阿爾菲諾等）、7.0 核心角色（烏克拉瑪特、圖萊尤拉等）與世界觀專有名詞（光之戰士、拂曉血盟、乙太、蠻神、艾歐澤亞等）。
- **即時對照測試器 (Live Tester)**：在編輯器底部可即時輸入說話者與台詞進行規則測試，確認取代結果與注入規則。
- **JSON / CSV 匯入與匯出**：完整支援文字框複製貼上、備份與批次匯入（支援合併或覆蓋）。

---

## 專案目錄結構

```
dalamud-tataru/
├── Data/
│   ├── jp-ch-name.json             # 日中 NPC/人名/地名對照表 (來自 Tataru 原版，含房區擴充)
│   ├── en-ch-name.json             # 英中 NPC/專有名詞對照表
│   ├── after-translation-cht.json  # 繁體中文譯後名詞校正表
│   └── after-translation-chs.json  # 簡體中文譯後名詞校正表
├── Models/
│   ├── DialogueEntry.cs            # 對話資料模型與語言設定定義
│   ├── CustomGlossaryEntry.cs      # 自訂多語名詞對照表資料結構
│   ├── CustomGlyphEntry.cs         # 自訂缺字摺疊替換資料結構
│   ├── ChatChannelHelper.cs        # 聊天頻道與系統訊息分類/色彩對照定義
│   ├── ChatHistoryEntry.cs         # 聊天翻譯歷史資料結構
│   └── QuestHoverInfo.cs           # 任務說明文字懸浮翻譯資料結構
├── Services/
│   ├── DialogueWatcher.cs          # 遊戲原生對話/字幕視窗監聽器 (Talk / Subtitle / BattleTalk)
│   ├── QuestJournalWatcher.cs      # 任務詳細說明欄與接取/回報視窗滑鼠懸浮監聽服務
│   ├── JournalInspector.cs         # 任務日誌 UI 節點深層掃描與除錯診斷器
│   ├── ChatTranslationService.cs   # 對話欄與系統訊息即時監聽、原生攔截、非同步翻譯與輸出服務
│   ├── GlyphFolder.cs              # 遊戲原生 AXIS 字型缺字檢測與相似字摺疊服務
│   ├── GlyphFolder.FoldTable.cs    # 靜態字元映射字典檔
│   ├── GeminiTranslator.cs         # Google Gemini REST API 非同步翻譯、配額分流與快取服務
│   ├── LmStudioTranslator.cs       # LM Studio 本地大模型翻譯服務
│   ├── YoudaoTranslator.cs         # 有道線上翻譯與備用防線服務
│   ├── GoogleTranslator.cs         # Google 免費公共線上翻譯服務
│   ├── GlossaryManager.cs          # 專有名詞讀取、譯前替換、自訂對照表持久化與文本校正服務
│   └── PlayerNameHelper.cs         # 玩家自身角色名稱防洩漏保護工具
├── Windows/
│   ├── ConfigWindow.cs             # 插件設定介面 (AI、聊天頻道、任務懸浮、自訂對照表、外觀、除錯)
│   ├── ChatHistoryWindow.cs        # 獨立聊天與系統翻譯歷史紀錄懸浮視窗
│   ├── GlossaryEditorWindow.cs     # 獨立自訂名詞對照表編輯器視窗 (支援手動儲存與測試)
│   ├── SubtitleOverlayWindow.cs    # ImGui 原生字幕懸浮視窗
│   ├── DialogueReplaceOverlayWindow.cs # 原生對話框覆蓋視窗 (羊皮紙/原生泡泡模式)
│   └── QuestHoverOverlayWindow.cs  # 任務說明內容滑鼠懸停 ImGui 翻譯懸浮視窗
├── Configuration.cs                # 插件設定資料結構與持久化
├── Plugin.cs                       # Dalamud 插件主入口與指令註冊
├── DalamudTataru.csproj            # 專案建置檔 (基於 Dalamud.NET.Sdk)
└── DalamudTataru.json              # Dalamud 插件 Manifest
```

---

## 安裝方式 (Dalamud 自訂插件庫)

1. 在遊戲中輸入 `/xlsettings` 開啟 Dalamud 設定視窗。
2. 切換至 **「實驗性功能 (Experimental)」** 分頁。
3. 找到 **「自訂插件庫 (Custom Plugin Repositories)」** 區塊，新增以下網址並儲存：
   ```
   https://raw.githubusercontent.com/tcfshcrw/dalamud-tataru/main/pluginmaster.json
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
| `/tataru` 或 `/tataru config` / `/tataru settings` | 開啟 / 關閉主設定視窗 |
| `/tataru toggle` | 開啟 / 關閉字幕視窗（可用於手動調整視窗位置） |
| `/tataru choices` 或 `/tataru select` | 開啟 / 關閉**對話選項翻譯視窗預覽**（用於調整位置與縮放比例） |
| `/tataru replace [on\|off]` | 開關原生對話框覆蓋模式（直接在原生對話框覆蓋中文譯文） |
| `/tataru gemini [on\|off]` | 切換主翻譯引擎為 Gemini 或在 Gemini 與有道之間切換 |
| `/tataru LMS [on\|off]` | 切換主翻譯引擎為本機 LM Studio 大模型 |
| `/tataru youdao` | 切換主翻譯引擎為有道 (Youdao) |
| `/tataru google` | 切換主翻譯引擎為 Google Translate |
| `/tataru chat` | 開啟 / 關閉**獨立對話與系統翻譯紀錄懸浮窗** |
| `/tataru glossary` 或 `/tataru dict` / `/tataru custom` | 開啟 / 關閉**獨立自訂名詞對照表編輯器視窗** |
| `/tataru clearcache` 或 `/tataru clear` | 清除目前記憶體中所有的對話翻譯暫存快取 |
| `/tataru update` | 直接從 Tataru-Assistant 雲端更新最新專有名詞與譯後修飾文本庫 |
| `/tataru help` | 於遊戲聊天框列出常用指令說明 |

---

## 致敬與感謝

- **[winw1010/tataru-assistant](https://github.com/winw1010/tataru-assistant)**：感謝 Tataru Assistant 團隊為 FF14 中文社群長期整理與維護的專有名詞庫與譯名對照表。
- **[lokinmodar/Echoglossian](https://github.com/lokinmodar/Echoglossian)**：感謝 Echoglossian 開發團隊，本插件的任務詳細說明欄與接取/回報視窗監聽機制（`JournalDetail` / `JournalAccept` / `JournalResult`）深獲其架構啟發與技術借鏡。
- **[kelvin124124/ChatTranslated](https://github.com/kelvin124124/ChatTranslated)**：感謝 ChatTranslated 開發團隊，為本插件的聊天頻道即時監聽、色彩標籤重現與訊息分流過濾提供了極具價值的設計參考。
- **[Dalamud](https://github.com/goatcorp/Dalamud)**：感謝 Goatcorp 與 Dalamud 團隊為 Final Fantasy XIV 打造的卓越插件生態與開發框架。
