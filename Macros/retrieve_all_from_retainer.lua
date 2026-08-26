--[[
快速取回目前開啟中雇員身上的所有道具（含水晶），全部進玩家自己的背包，不會放進兵裝庫。

前提：
  - 使用 AutoRetainer 新增的 IPC 方法 RetrieveNextRetainerItemSlot()：每呼叫一次只觸發一格取回，
    不等遊戲確認道具真的離開雇員庫存就馬上回傳，讓這支腳本自己控制節奏。比 AutoRetainer 內建
    「每格都等確認、最多等 5 秒」的機制快很多，代價是偶爾可能漏接（指令送出但沒生效），所以
    分成多輪掃描，每輪結束後再檢查一次還有沒有漏掉的東西。
  - 執行前雇員的「物品儲存」視窗必須已經開啟（跟手動取回一樣，先跟雇員互動）。IPC 方法本身
    會檢查視窗是否開啟，沒開啟就直接回傳 false，不會報錯，但這支腳本一次道具都取不到。
  - 停止條件：這一輪完全沒有取回任何東西（代表雇員身上已經空了，或玩家背包已經接近滿了——
    沿用 AutoRetainer 自己的「背包快滿」門檻，跟它內建功能一致）。
]]

local INTERVAL = 0.1  -- 每次觸發之間的間隔秒數，越小越快但漏接風險越高，不建議壓太低
local MAX_PASSES = 3  -- 最多掃幾輪，每輪結束後停頓一下再檢查是否還有漏接的

if not IPC.IsInstalled("AutoRetainer") then
    yield("/echo AutoRetainer 未安裝，無法執行。")
    return
end

local totalRetrieved = 0

for pass = 1, MAX_PASSES do
    local retrievedThisPass = 0
    while IPC.AutoRetainer.RetrieveNextRetainerItemSlot() do
        retrievedThisPass = retrievedThisPass + 1
        totalRetrieved = totalRetrieved + 1
        yield("/wait " .. INTERVAL)
    end

    Dalamud.Log(string.format("第 %d 輪完成，本輪取回 %d 格。", pass, retrievedThisPass))

    if retrievedThisPass == 0 then
        break -- 這輪什麼都沒拿到：雇員已經空了，或背包已經滿了，或視窗根本沒開
    end

    yield("/wait 1") -- 讓庫存資料穩定，再檢查一次有沒有漏接的
end

if totalRetrieved == 0 then
    yield("/echo 沒有取回任何道具，請確認雇員的物品儲存視窗是否已開啟，或背包是否已經滿了。")
else
    local report = string.format("取回完成，共取回 %d 格道具。", totalRetrieved)
    Dalamud.Log(report)
    yield("/echo " .. report)
end
