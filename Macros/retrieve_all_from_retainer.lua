--[[
快速取回目前開啟中雇員身上的道具，全部進玩家自己的背包，不會放進兵裝庫。
預設「不取水晶」（見 INCLUDE_CRYSTALS）。

前提：
  - 使用 AutoRetainer 的 IPC 方法 RetrieveNextRetainerItemSlot()：每呼叫一次只觸發一格取回，
    不等遊戲確認道具真的離開雇員庫存就馬上回傳，讓這支腳本自己控制節奏。
  - 執行前雇員的「物品儲存」視窗必須已經開啟（跟手動取回一樣，先跟雇員互動）。IPC 方法本身
    會檢查視窗是否開啟，沒開啟就直接回傳 false，不會報錯，但這支腳本一次道具都取不到。

⚠️ 這支腳本必須自己判斷「有沒有真的取到」，因為 IPC 的回傳值不是這個意思：
    RetrieveNextRetainerItemSlot() 回傳 true 只代表「送出了一個取回指令」，
    不代表「那一格真的離開了雇員」。舊版把 true 當成進度，於是產生兩個實測到的問題
    （2026-08-02 dalamud.log，四次執行）：

    1. 指令放大 3.1～3.9 倍：同一格會被連送 3～5 次。實測單格從送出到真的離開雇員
       中位數 0.382 秒，而舊版每 0.1 秒就再送一次，所以每格平均多送 2～3 個無效指令
       （317 次指令只對應 95 個實際格子）。
    2. 取不走的格子會無限重試：實測「水晶第 3 格」被連續送了 50 次指令、卡住 15.94 秒
       才脫離——這就是使用者回報的「取到水晶造成視窗卡住」。水晶取回不佔背包格，
       所以 AutoRetainer 內建的「背包快滿就停」門檻永遠攔不住它。

    修法：改成等「雇員身上真的少一格」才算數（輪詢雇員庫存），並且預設在道具頁清空時
    就停手、不碰水晶。
]]

local INCLUDE_CRYSTALS = false -- true = 連水晶一起取回。⚠️ 水晶不佔背包格，若你的水晶已達上限
                               -- 會取不走，屆時本腳本會在 SLOT_TIMEOUT 後自行停止而不是卡死。
local POLL = 0.05              -- 輪詢「雇員是否真的少一格」的間隔秒數
local SLOT_TIMEOUT = 1.5       -- 單一格等待確認的上限秒數；超過就視為這格取不走，停止本輪
local MAX_PASSES = 3           -- 最多掃幾輪，每輪結束後停頓一下再檢查是否還有漏接的

local ITEM_PAGES = {
    "RetainerPage1", "RetainerPage2", "RetainerPage3", "RetainerPage4",
    "RetainerPage5", "RetainerPage6", "RetainerPage7",
}

-- 掃描範圍：預設只看道具頁；開啟 INCLUDE_CRYSTALS 才把水晶納入進度判斷。
local SCAN_PAGES = {}
for _, name in ipairs(ITEM_PAGES) do table.insert(SCAN_PAGES, name) end
if INCLUDE_CRYSTALS then table.insert(SCAN_PAGES, "RetainerCrystals") end

-- 回傳指定容器目前「已佔用」的格數。每次都重新取得 wrapper，不跨幀保存原生指標。
--
-- ⚠️ 只有在 RetrieveNextRetainerItemSlot() 至少成功回傳過一次之後才准呼叫這個函式：
--    那個 IPC 內部會先驗 IsRetainerInventoryLoaded()，回 true 就代表雇員容器確實載入了。
--    SND 的 InventoryContainerWrapper 讀 Count/FreeSlots 時「不做 null 檢查」就解參考容器指標，
--    而原生存取違規（AccessViolation）在 .NET Core 屬於 corrupted-state exception，
--    pcall / try-catch 都攔不住，會直接把遊戲帶走。下面的 pcall 只擋得住受管理層的錯誤
--    （例如型別不符），擋不住解參考未載入的容器——所以真正的防護是「呼叫時機」。
local function usedSlots(pages)
    local used = 0
    for _, name in ipairs(pages) do
        local ok, n = pcall(function()
            local c = Inventory[name]
            return c.Count - c.FreeSlots
        end)
        if ok and type(n) == "number" then used = used + n end
    end
    return used
end

if not IPC.IsInstalled("AutoRetainer") then
    yield("/echo AutoRetainer 未安裝，無法執行。")
    return
end

local totalRetrieved = 0
local totalCommands = 0
local stopReason = nil
-- 只要 IPC 成功回傳過一次，就代表雇員容器已載入，之後才可以安全讀取容器（見 usedSlots 註解）。
local inventoryKnownLoaded = false

for pass = 1, MAX_PASSES do
    local retrievedThisPass = 0

    while true do
        -- 道具頁已經空了：預設就停在這裡，不要讓 IPC 往下掃到水晶。
        if inventoryKnownLoaded and not INCLUDE_CRYSTALS and usedSlots(ITEM_PAGES) == 0 then
            stopReason = stopReason or "道具頁已清空（未取水晶）"
            break
        end

        local before = inventoryKnownLoaded and usedSlots(SCAN_PAGES) or nil

        -- IPC 自己會擋掉「視窗沒開」與「背包快滿」，回 false 就是該收工了。
        if not IPC.AutoRetainer.RetrieveNextRetainerItemSlot() then
            stopReason = stopReason or "AutoRetainer 回報沒有可取回的道具（或背包已快滿）"
            break
        end
        totalCommands = totalCommands + 1

        if before == nil then
            -- 本次執行的第一次呼叫：在這之前還不確定容器能不能安全讀取，所以這一格
            -- 沿用舊版的固定等待，不做進度判定。只有這一格會這樣，之後全部走進度判定。
            inventoryKnownLoaded = true
            yield("/wait " .. SLOT_TIMEOUT)
        else
            -- 等到雇員身上真的少一格才算數，而不是固定睡 0.1 秒就當作成功。
            local waited = 0
            local moved = false
            while waited < SLOT_TIMEOUT do
                yield("/wait " .. POLL)
                waited = waited + POLL
                if usedSlots(SCAN_PAGES) < before then
                    moved = true
                    break
                end
            end

            if not moved then
                -- 指令送出去了但那一格沒動：水晶已達上限、或這格因為其他原因取不走。
                -- 再送幾次也不會有效果（實測會連送 50 次卡 16 秒），直接結束本輪。
                stopReason = string.format("有一格取不走（等了 %.1f 秒沒有變化），已停止避免空轉", SLOT_TIMEOUT)
                break
            end
        end

        retrievedThisPass = retrievedThisPass + 1
        totalRetrieved = totalRetrieved + 1
    end

    Dalamud.Log(string.format("第 %d 輪完成，本輪取回 %d 格（累計送出指令 %d 次）。",
        pass, retrievedThisPass, totalCommands))

    if retrievedThisPass == 0 then
        break -- 這輪什麼都沒拿到：雇員已經空了，或背包已經滿了，或視窗根本沒開
    end

    yield("/wait 1") -- 讓庫存資料穩定，再檢查一次有沒有漏接的
end

if totalRetrieved == 0 then
    yield("/echo 沒有取回任何道具，請確認雇員的物品儲存視窗是否已開啟，或背包是否已經滿了。")
    if stopReason then Dalamud.Log("停止原因：" .. stopReason) end
else
    -- 指令放大倍率：健康值應該接近 1.00。舊版實測是 3.1～3.9。
    local amplification = totalCommands / totalRetrieved
    local report = string.format("取回完成，共取回 %d 格道具。", totalRetrieved)
    Dalamud.Log(string.format("%s（送出指令 %d 次，放大倍率 %.2fx；停止原因：%s）",
        report, totalCommands, amplification, stopReason or "正常結束"))
    yield("/echo " .. report)
end
