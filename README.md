# Something Need Doing (SND)

擴充遊戲原生巨集系統的插件：不限行數、不限視窗數，並內建 Lua 腳本引擎與大量自動化模組。

原作者：[Jaksuhn/SomethingNeedDoing](https://github.com/Jaksuhn/SomethingNeedDoing)

## 功能

- 巨集管理與排程：可同時執行多個巨集，支援暫停／恢復／停止（含「執行到下一個迴圈點再停」）、指定迴圈次數執行
- 相容原生巨集語法，並擴充更多指令與修飾詞
- Lua 腳本引擎，API 涵蓋：技能施放、遊戲介面（Addon）操作、聊天、插件設定讀寫、Dalamud 服務、戰鬥單位、Excel 表查詢、FATE、副本狀態、任務、物品欄、跨插件 IPC、玩家狀態、系統資訊等
- 社群腳本庫：可搭配 [SNDScripts](https://github.com/WigglyMuffin/SNDScripts/)、[The Dumpster Fire](https://github.com/McVaxius/dhogsbreakfeast) 等社群維護的腳本集使用

## 指令

- `/somethingneeddoing`、`/snd`、`/pcraft`：開啟主視窗
- `run "<巨集名>"`／`run loop <次數> "<巨集名>"`：執行巨集／迴圈執行
- `pause "<巨集名>"`／`pause loop "<巨集名>"`／`pause all`：暫停
- `resume "<巨集名>"`／`resume all`：恢復
- `stop "<巨集名>"`／`stop loop "<巨集名>"`／`stop all`：停止
- `status`：切換執行中巨集清單視窗
- `changelog`：切換更新記錄視窗
- `help`：列出指令說明
