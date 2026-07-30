--[[
自動照料庭院種植物件（園圃/花盆），依實際狀態判斷 護理/施肥/收穫+重新種植/跳過

前提（已透過實機截圖 + 玩家回報 + 實測驗證）：
  - 可互動物件的顯示名稱：庭院「園圃」，或各種「XX花盆」（海濱花盆/林間花盆/綠洲花盆...）
  - 狀態文字不是來自 SelectString 的選項內容，是互動當下跳出的聊天/系統訊息
    （例如「風茄正茁壯成長。」「黃麻正...狀態不太好...」），格式是"植物名+狀態片語"，
    所以用「包含」比對而不是完全相等。
  - 依訊息內容判斷狀態：
      * 含「狀態不太好」：需要護理，選項 施肥/護理/處理/取消 -> 先護理再施肥
      * 含「茁壯成長」：健康，選項 施肥/護理/處理/取消 -> 直接施肥,不用護理
        （目標作物「虛無界風茄」種植後不施肥，取消跳過；純靠訊息裡的植物名稱文字比對判斷，
        訊息可能只顯示簡稱，比對不一定準，但目前沒有更可靠的方式）
      * 含「已經成熟了」：可收穫 -> 只有目標作物（虛無界風茄）會自動收穫並重新種植，
        其他作物一律跳過不動，留給玩家自己手動收穫
      * 含「已經枯萎了」：需要人工處理 -> 選取消,不自動處理
      * 含「沒有種」：空花盆/空園圃 -> 自動播種目標作物（虛無界風茄 + 園藝土壤）
        （空盆本身不視為錯誤，播種失敗也不會重試同一個點）
  - SelectString 選項在畫面上的顯示順序，就是 /callback 用的 0-based index
  - 選「播種」後跳出的是 HousingGardening addon（土壤與種子兩個拖曳格 + 確定/取消按鈕），
    土壤格/種子格可以程式化模擬右鍵（DragDropClick 事件，which=1 是土壤格、which=2 是
    種子格），跳出 ContextIconMenu 後依道具名稱選取，確定按鈕是 node id=8，最後跳標準
    SelectYesno 二次確認，選「是」(index 0) 完成種植。
  - 收穫後會跳出 HousingHarvest 收穫結果提示視窗（沒有按鈕，會自動消失），實測不影響後續
    互動流程，不用特地等它消失。
  - 如果有其他外掛（例如自動園藝類）搶先攔截「播種」互動、直接用固定的土壤+種子瞬間種下，
    不會讓 HousingGardening 視窗跳出來——這種情況要重新互動確認目前狀態，不能直接判定失敗。

單一物件處理中途卡住（互動失敗、逾時等）會整個重試一次，而不是直接放棄跳下一個。

【與 TC Toolbox 的關係】（2026-07-30 更新）
先前因為「TC Toolbox 只認庭院地壟、改過去會失去花盆支援」而維持純原生實作；
TC Toolbox 已於 2026-07-30 補上室內園藝花盆支援，涵蓋範圍不再落後，因此本腳本
改為「有裝就委派、沒裝就原生」的雙路徑：
  * 偵測到 TC Toolbox 且其「自動園圃作業」模組已啟用 → 互動與選單操作全部委派，
    本腳本只負責決策（要不要收、要不要施肥、是不是目標作物）。
  * 偵測不到（沒裝／模組沒開／版本不符）→ 原封不動走下面的原生實作，
    行為與過去完全一致。偵測全程包在 pcall 裡，不會讓巨集中斷。
涵蓋範圍差異（已查證）：TC Toolbox 的花盆清單由 HousingFurniture 表推導
（UsageType 11／CustomTalk 721227＝海濱／林間／綠洲花盆），比本腳本的
「花盆結尾」名稱比對更精準——南瓜花盆之類純裝飾的花盆本來就不能種東西。
另外台服並不存在名為「花圃」的道具，下面的花圃比對是無效分支（留著無害）。
]]

local TARGET_PLANT_NAME = "虛無界風茄" -- 只收穫並重新種植這個作物，其他成熟作物一律跳過不動
local SEED_ITEM_NAME = "虛無界風茄"    -- 種子道具名稱跟作物同名
local SOIL_ITEM_NAME = "園藝土壤"
local CONFIRM_BUTTON_NODE_ID = 8 -- HousingGardening 的「確定」按鈕
local SOIL_SLOT_WHICH = 1
local SEED_SLOT_WHICH = 2

local RETRY_COUNT = 1 -- 單一物件處理失敗時，額外重試的次數（不含第一次嘗試）

-- 曾經嘗試過用座標記錄「這個位置種了什麼」跨次執行查表（省去每次都用訊息文字猜），也試過
-- 用座標做冷卻機制跳過短期內不需要再檢查的地壟。兩者都在同一個「園圃」底下的多個地壟座標
-- 完全相同（已實測確認）這件事上失敗——地壟身分沒辦法在互動前確認，用座標記錄或冷卻都會
-- 把不同地壟的紀錄搞混。所以最後放棄跨次執行的持久化記錄，單純依賴每次互動當下的訊息文字
-- 比對，雖然多花一點互動次數，但不會有記錄互相污染的風險。

-- 可種植物件的名稱規則：庭院的「園圃」，以及各種「XX花盆」（海濱花盆/林間花盆/綠洲花盆...）
local function isPlantableName(name)
    return name == "園圃" or name:match("花盆$") ~= nil or name:match("花圃$") ~= nil
end

local INTERACT_RANGE = 5.0   -- 互動距離（碼）。太大會選到超出實際互動範圍的物件，導致「距離太遠」
local MAX_OBJECT_INDEX = 599 -- 物件表掃描上限

local NEEDS_CARE_TEXT = "狀態不太好"
local HEALTHY_TEXT = "茁壯成長"
local MATURE_TEXT = "已經成熟了"
local WITHERED_TEXT = "已經枯萎了"
local EMPTY_POT_TEXT = "沒有種" -- 涵蓋「花盆裡沒有種任何東西」「園圃...沒有種...」等各種容器的空盆訊息
local HARVEST_LABEL = "收穫"
local CARE_LABEL = "護理"
local FERTILIZE_LABEL = "施肥"
local SOW_LABEL = "播種"
local CANCEL_LABEL = "取消"
local FERTILIZER_ITEM_LABEL = "魚粉" -- 選施肥後，還會跳一層選擇肥料道具的選單
local ALREADY_FERTILIZED_TEXT = "已經施加了足夠的肥料了"

-- 掃描附近所有名稱符合、距離內的種植物件
local function findNearbyPlots()
    local plots = {}
    for i = 0, MAX_OBJECT_INDEX do
        local ok, entity = pcall(function() return Entity[i] end)
        if ok and entity ~= nil and isPlantableName(entity.Name) and entity.DistanceTo <= INTERACT_RANGE then
            table.insert(plots, entity)
        end
    end
    return plots
end

-- 讀取目前 SelectString 視窗的選項文字，過濾掉純數字的內部值，以及帶冒號的標題行
-- （例如「園圃2：地壟8」是標題不是選項，不然 index 會整個偏移一位）
local function readSelectStringOptions()
    local addon = Addons.GetAddon("SelectString")
    local raw = addon:GetValueTexts()
    local options = {}
    for i = 0, raw.Count - 1 do
        local s = raw[i]
        if s ~= nil and s ~= "" and not tostring(s):match("^%d+$")
            and not s:find("：", 1, true) and not s:find(":", 1, true) then
            table.insert(options, s)
        end
    end
    return options
end

-- 判斷互動時捕捉到的狀態訊息屬於哪一種狀態（用包含比對，因為訊息前面會帶植物名稱）
local function classifyStatus(message)
    if message == nil or message == "" then return nil end
    if message:find(WITHERED_TEXT, 1, true) then return WITHERED_TEXT end
    if message:find(NEEDS_CARE_TEXT, 1, true) then return NEEDS_CARE_TEXT end
    if message:find(HEALTHY_TEXT, 1, true) then return HEALTHY_TEXT end
    if message:find(MATURE_TEXT, 1, true) then return MATURE_TEXT end
    if message:find(EMPTY_POT_TEXT, 1, true) then return EMPTY_POT_TEXT end
    return nil
end

-- 找選項在清單中的 0-based index（對應 /callback 用的 index）
local function optionIndex(options, label)
    for i, t in ipairs(options) do
        if t == label then return i - 1 end
    end
    return nil
end

local function selectOption(index)
    yield('/callback "SelectString" true ' .. index)
    yield("/wait 0.15")
end

local function cancelIfPossible(options)
    local idx = optionIndex(options, CANCEL_LABEL)
    if idx then selectOption(idx) end
end

-- 輪詢等待某個 addon 準備好，比固定 wait 更快也更穩
-- 注意：每次 yield 都是一次完整的 native 指令派送，實際耗時比 interval 數字大，
-- 所以 timeoutMs 需要抓寬鬆一點，避免明明互動成功卻被判定逾時而誤跳過
local function waitForAddonReady(name, timeoutMs)
    local waited = 0
    local interval = 150
    while waited < timeoutMs do
        if Addons.GetAddon(name).Ready then return true end
        yield("/wait " .. (interval / 1000))
        waited = waited + interval
    end
    return Addons.GetAddon(name).Ready
end

-- 等待 SelectString，但有些物件互動後會先跳出「對話」（Talk addon，NPC/物件的過場文字），
-- 要先把它點掉才會進到 SelectString，不然會一直卡住直到逾時
local function waitForSelectStringAfterInteract(timeoutMs)
    local waited = 0
    local interval = 150
    while waited < timeoutMs do
        if Addons.GetAddon("SelectString").Ready then return true end
        if Addons.GetAddon("Talk").Exists then
            yield("/click Talk Click")
        end
        yield("/wait " .. (interval / 1000))
        waited = waited + interval
    end
    return Addons.GetAddon("SelectString").Ready
end

local function logVisibleAddons(prefix)
    local names = Addons.GetVisibleAddonNames()
    local parts = {}
    for i = 0, names.Count - 1 do table.insert(parts, names[i]) end
    Dalamud.Log(string.format("%s 可見視窗=[%s]", prefix, table.concat(parts, ",")))
end

local function findItemByName(name)
    local containers = { "Inventory1", "Inventory2", "Inventory3", "Inventory4" }
    for _, cname in ipairs(containers) do
        local container = Inventory[cname]
        for i = 0, container.Count - 1 do
            local item = container[i]
            if item ~= nil and item.ItemId ~= 0 then
                local row = Excel.GetRow("Item", item.ItemId)
                if row ~= nil and row["Name"] == name then
                    return item
                end
            end
        end
    end
    return nil
end

-- 選完「施肥」後：
--   1. 如果肥料已經足夠，遊戲只會跳訊息「已經施加了足夠的肥料了」，不會開背包 -> 直接跳過
--   2. 否則會開背包等你「右鍵道具 -> 施肥」，這裡改用 AgentInventoryContext 直接開啟該
--      道具的右鍵選單（OpenContextMenu），再從 ContextMenu 選「施肥」（SelectContextMenuEntry）
local function tryChooseFertilizerItem()
    yield("/wait 0.15")
    local msg = Chat.GetLastMessage()
    if msg ~= nil and msg:find(ALREADY_FERTILIZED_TEXT, 1, true) then
        Dalamud.LogDebug("肥料已經足夠，不用開背包")
        return
    end

    local item = findItemByName(FERTILIZER_ITEM_LABEL)
    if not item then
        Dalamud.Log("背包裡找不到魚粉，跳過，請人工處理施肥道具選擇")
        return
    end

    Dalamud.LogDebug("找到魚粉，開啟右鍵選單")
    item:OpenContextMenu()
    waitForAddonReady("ContextMenu", 1000)

    if Addons.SelectContextMenuEntry(FERTILIZE_LABEL) then
        Dalamud.LogDebug("已從右鍵選單選擇施肥")
    else
        Dalamud.Log("右鍵選單裡找不到「施肥」選項，跳過，請截圖回報")
    end
    yield("/wait 0.1")
end

-- 對 HousingGardening 的土壤/種子拖曳格模擬右鍵，跳出 ContextIconMenu 後依道具名稱選取
-- （用 SelectContextIconMenuEntryByText 依名稱比對，背包裡同時有多種土壤/種子時也能選對）
local function fillDragDropSlot(which, itemLabel)
    local gardening = Addons.GetAddon("HousingGardening")
    gardening:RightClickDragDropSlot(which)

    if not waitForAddonReady("ContextIconMenu", 1500) then
        Dalamud.Log(string.format("右鍵 %s 格沒有跳出選擇道具選單，中止種植，請人工處理", itemLabel))
        return false
    end

    if not Addons.SelectContextIconMenuEntryByText(itemLabel) then
        Dalamud.Log(string.format("選單裡找不到「%s」，中止種植，請人工處理", itemLabel))
        return false
    end
    yield("/wait 0.2")
    return true
end

-- 另一個外掛（自動園藝類）可能會搶先攔截「播種」互動，直接用固定的土壤+種子瞬間種下，
-- 不會讓 HousingGardening 視窗跳出來，導致我們自己等視窗的邏輯永遠等不到。
-- 重新互動確認目前狀態，如果已經變成「健康 + 目標作物」，代表已經被瞬間種下成功。
local function checkAlreadyAutoSown(entity)
    yield("/wait 0.3")
    Chat.ClearLastMessage()
    entity:Interact()
    if not waitForSelectStringAfterInteract(3000) then
        return false
    end
    local msg = Chat.GetLastMessage()
    local status = classifyStatus(msg)
    local isAutoSown = status == HEALTHY_TEXT and msg ~= nil and msg:find(TARGET_PLANT_NAME, 1, true) ~= nil
    if isAutoSown then
        Dalamud.Log(string.format("偵測到已被瞬間種下（可能是其他外掛搶先處理），訊息=[%s]，視為完成", msg))
    end
    cancelIfPossible(readSelectStringOptions())
    return isAutoSown
end

-- 選「播種」之後：HousingGardening 開啟 -> 填土壤 -> 填種子 -> 點確定 -> SelectYesno 選是
local function trySowSeed(entity)
    if not waitForAddonReady("HousingGardening", 200) then
        -- 沒開起來：可能是其他外掛（自動園藝類）搶先攔截、直接瞬間種下，改確認目前實際狀態
        logVisibleAddons("播種後沒看到 HousingGardening，改確認是否已被其他外掛瞬間種下")
        return checkAlreadyAutoSown(entity)
    end

    if not fillDragDropSlot(SOIL_SLOT_WHICH, SOIL_ITEM_NAME) then return false end
    if not fillDragDropSlot(SEED_SLOT_WHICH, SEED_ITEM_NAME) then return false end

    local gardening = Addons.GetAddon("HousingGardening")
    if not gardening:ClickButton(CONFIRM_BUTTON_NODE_ID) then
        Dalamud.Log("點擊確定按鈕失敗（可能還沒填滿或按鈕被擋），中止種植，請人工處理")
        return false
    end

    if waitForAddonReady("SelectYesno", 1500) then
        yield('/callback "SelectYesno" true 0') -- 是
        yield("/wait 0.3")
        Dalamud.Log(string.format("已確認種植 %s，結果：%s", SEED_ITEM_NAME, tostring(Chat.GetLastMessage())))
        return true
    end

    Dalamud.Log("點確定後沒看到 SelectYesno 二次確認，請自行確認種植狀態")
    return false
end

-- 收穫後理論上會立刻變空，重新互動一次進入播種流程
local function harvestThenReplant(entity, options)
    Dalamud.Log("收穫中")
    local harvestIdx = optionIndex(options, HARVEST_LABEL)
    if not harvestIdx then
        Dalamud.Log("找不到收穫選項，取消跳過")
        cancelIfPossible(options)
        return false
    end
    Chat.ClearLastMessage()
    selectOption(harvestIdx)
    Dalamud.Log(string.format("收穫結果：%s", tostring(Chat.GetLastMessage())))

    -- HousingHarvest 收穫結果提示視窗實測不影響後續操作（不會擋住播種視窗），不用特地等它
    -- 消失。trySowSeed 內仍保留了它擋住播種視窗時的備援處理，真的遇到再說。
    yield("/wait 0.4") -- 收穫動畫/狀態更新需要一點緩衝時間，太快重新互動會抓不到
    Chat.ClearLastMessage()
    entity:Interact()
    if not waitForSelectStringAfterInteract(4000) then
        logVisibleAddons("收穫後重新互動逾時")
        Dalamud.Log(string.format("收穫後重新互動失敗：%s", tostring(Chat.GetLastMessage())))
        return false
    end

    local msg = Chat.GetLastMessage()
    if msg == nil or not msg:find(EMPTY_POT_TEXT, 1, true) then
        Dalamud.Log(string.format("收穫後預期是空盆，但訊息是 [%s]，中止自動種植，請人工確認", tostring(msg)))
        cancelIfPossible(readSelectStringOptions())
        return false
    end

    local options2 = readSelectStringOptions()
    local sowIdx = optionIndex(options2, SOW_LABEL)
    if not sowIdx then
        Dalamud.Log("找不到播種選項，取消跳過")
        cancelIfPossible(options2)
        return false
    end
    selectOption(sowIdx)
    return trySowSeed(entity)
end

-- 對單一種植物件執行一次：互動 -> 讀狀態訊息 -> 依狀態決定動作。
-- 回傳 (ok, category)：ok 代表這次有沒有順利跑完，category 是這次處理結果的分類，用於最後彙總回報。
local function tendOnePlotOnce(entity)
    Chat.ClearLastMessage()
    entity:SetAsTarget()
    yield("/wait 0.15")
    entity:Interact()

    if not waitForSelectStringAfterInteract(3000) then
        local addon = Addons.GetAddon("SelectString")
        local names = Addons.GetVisibleAddonNames()
        local parts = {}
        for i = 0, names.Count - 1 do table.insert(parts, names[i]) end
        Dalamud.Log(string.format("互動失敗（可能距離太遠或已被其他效果擋住）：%s [Exists=%s Ready=%s] 可見視窗=[%s]",
            tostring(Chat.GetLastMessage()), tostring(addon.Exists), tostring(addon.Ready), table.concat(parts, ",")))
        return false, "failed"
    end

    local statusMsg = Chat.GetLastMessage()
    local status = classifyStatus(statusMsg)
    local options = readSelectStringOptions()
    Dalamud.LogDebug(string.format("訊息=[%s] 狀態=[%s] 選項=[%s]", tostring(statusMsg), tostring(status), table.concat(options, ",")))

    if statusMsg == nil then statusMsg = "" end

    -- 沒抓到已知狀態文字，但選單只有 收穫/取消 兩項，也視為已成熟
    if status == nil and #options == 2 and optionIndex(options, HARVEST_LABEL) ~= nil then
        status = MATURE_TEXT
    end

    if status == EMPTY_POT_TEXT then
        local sowIdx = optionIndex(options, SOW_LABEL)
        if sowIdx then
            Dalamud.Log("空盆，嘗試播種 " .. SEED_ITEM_NAME)
            selectOption(sowIdx)
            local ok = trySowSeed(entity)
            -- 空盆本身不是錯誤（可能只是暫時沒種子/土壤），不觸發同一個點的重試
            return true, (ok and "sown" or "skipped")
        end
        cancelIfPossible(options)
        return true, "skipped" -- 沒有播種選項就是正常跳過，不算失敗
    end

    if status == MATURE_TEXT then
        if statusMsg:find(TARGET_PLANT_NAME, 1, true) then
            local ok = harvestThenReplant(entity, options)
            return ok, (ok and "harvested" or "failed")
        end
        Dalamud.LogDebug("已成熟但不是目標作物，跳過：" .. statusMsg)
        cancelIfPossible(options)
        return true, "skipped"
    end

    if status == WITHERED_TEXT then
        Dalamud.Log("已經枯萎了，跳過，需要人工處理")
        cancelIfPossible(options)
        return true, "withered"
    end

    if status == NEEDS_CARE_TEXT then
        local careIdx = optionIndex(options, CARE_LABEL)
        if careIdx then
            Dalamud.LogDebug("狀態不太好，先護理")
            selectOption(careIdx)
            yield("/wait 0.2") -- 護理動畫/狀態更新需要緩衝時間，太快重新互動會抓不到

            -- 護理後選單關閉，重新互動才能選施肥
            entity:Interact()
            if not waitForSelectStringAfterInteract(500) then
                Dalamud.Log("護理後重新互動失敗")
                return false, "failed"
            end
            local options2 = readSelectStringOptions()
            local fertIdx = optionIndex(options2, FERTILIZE_LABEL)
            if fertIdx then
                Dalamud.LogDebug("護理完成，施肥")
                Chat.ClearLastMessage()
                selectOption(fertIdx)
                tryChooseFertilizerItem()
            end
        else
            Dalamud.Log("狀態不太好，但找不到護理選項，取消跳過")
            cancelIfPossible(options)
        end
        return true, "cared"
    end

    if status == HEALTHY_TEXT then
        -- 訊息可能只顯示簡稱（例如「風茄」而不是完整的「虛無界風茄」），比對不一定能對到，
        -- 但目前沒有更可靠的方式能在不互動的情況下先確認這是不是目標作物
        local isTargetPlot = statusMsg:find(TARGET_PLANT_NAME, 1, true)
        if isTargetPlot then
            Dalamud.LogDebug(TARGET_PLANT_NAME .. " 種植後不施肥，取消跳過")
            cancelIfPossible(options)
            return true, "skipped"
        end
        local fertIdx = optionIndex(options, FERTILIZE_LABEL)
        if fertIdx then
            Dalamud.LogDebug("茁壯成長，直接施肥")
            Chat.ClearLastMessage()
            selectOption(fertIdx)
            tryChooseFertilizerItem()
        else
            cancelIfPossible(options)
        end
        return true, "fertilized"
    end

    -- 不認得的狀態，安全起見取消跳過
    Dalamud.Log(string.format("未知狀態訊息 [%s]，取消跳過，請回報這則訊息內容", tostring(statusMsg)))
    cancelIfPossible(options)
    return true, "unknown"
end

-- 包一層重試：失敗就整個重來，最多嘗試 RETRY_COUNT+1 次，還是失敗才真的放棄跳下一個。
-- 回傳最終處理結果分類，供最後彙總回報使用。
local function tendOnePlot(entity)
    for attempt = 1, RETRY_COUNT + 1 do
        local ok, category = tendOnePlotOnce(entity)
        if ok then return category end
        if attempt <= RETRY_COUNT then
            Dalamud.Log(string.format("這個物件處理失敗，重試第 %d 次", attempt))
            yield("/wait 0.5")
        else
            Dalamud.Log("重試後仍然失敗，放棄這個物件，繼續下一個")
        end
    end
    return "failed"
end

--============================================================================
-- TC Toolbox 委派路徑（有裝且已啟用「自動園圃作業」模組時自動採用）
--
-- 2026-07-30 起 TC Toolbox 也支援室內園藝花盆（海濱／林間／綠洲花盆），涵蓋範圍
-- 已不輸本腳本的名稱比對，所以可以整段委派出去，互動與選單全部交給它。
-- 上面的原生實作原封不動保留，偵測不到就自動退回，行為與過去完全一致。
--
-- 決策仍在本腳本：TC Toolbox 只知道「這格現在能做什麼」，不知道種的是哪種作物，
-- 所以「只收虛無界風茄」還是靠系統訊息判斷——它的 Scan 會觸發同一則訊息，
-- 兩邊資訊是同一次互動取得的，不會多花互動次數。
--============================================================================
local USE_TCTOOLBOX = true -- 設 false 可強制走原生實作

local function tcbCall(fn, ...)
    local ok, result = pcall(fn, ...)
    if not ok then
        Dalamud.LogDebug("TCToolbox IPC 呼叫失敗：" .. tostring(result))
        return false, nil
    end
    return true, result
end

local function detectTCToolbox()
    if not USE_TCTOOLBOX then return false end
    local ok, installed = tcbCall(function() return IPC.IsInstalled("TCToolbox") end)
    if not ok or not installed then
        Dalamud.Log("未偵測到 TC Toolbox，走原生實作")
        return false
    end
    local okAvail, available = tcbCall(function() return IPC.TCToolbox.IsAvailable() end)
    if not okAvail then
        Dalamud.Log("TC Toolbox 已安裝但 IPC 無法呼叫（版本不符？），走原生實作")
        return false
    end
    if not available then
        local _, reason = tcbCall(function() return IPC.TCToolbox.GetUnavailableReason() end)
        Dalamud.Log("TC Toolbox 目前不可用（" .. tostring(reason) .. "），走原生實作")
        return false
    end
    Dalamud.Log("已接上 TC Toolbox 園圃 IPC，改由它執行互動")
    return true
end

-- 由背包裡的道具反查 ItemId（TC Toolbox 的 IPC 吃 ItemId，本腳本的設定是名稱）
local function itemIdByName(name)
    local item = findItemByName(name)
    return item ~= nil and item.ItemId or nil
end

local function tcbWaitIdle(timeoutMs)
    local waited = 0
    while waited < timeoutMs do
        local ok, busy = tcbCall(function() return IPC.TCToolbox.IsBusy() end)
        if not ok then return false end
        if not busy then return true end
        yield("/wait 0.15")
        waited = waited + 150
    end
    Dalamud.Log("等待 TC Toolbox 動作逾時，主動要求停止")
    tcbCall(function() IPC.TCToolbox.Stop() end)
    return false
end

-- 排入一個動作並等它跑完。回傳 ok, 失敗原因
local function tcbRun(fn, ...)
    local ok, reason = tcbCall(fn, ...)
    if not ok then return false, "IPC 呼叫失敗" end
    if reason ~= nil and reason ~= "" then return false, tostring(reason) end
    if not tcbWaitIdle(20000) then return false, "逾時" end
    return true, nil
end

local function tendOnePlotTCB(patchId, seedId, soilId, fertId)
    Chat.ClearLastMessage()
    local ok, why = tcbRun(IPC.TCToolbox.Scan, patchId)
    if not ok then
        Dalamud.Log("掃描失敗（" .. tostring(why) .. "）")
        return "failed"
    end

    local statusMsg = Chat.GetLastSystemMessage() or ""
    local _, state = tcbCall(function() return IPC.TCToolbox.GetPatchState(patchId) end)
    state = state or "unknown"
    local status = classifyStatus(statusMsg)
    Dalamud.LogDebug(string.format("TCB 狀態=[%s] 訊息=[%s] 判讀=[%s]", tostring(state), statusMsg, tostring(status)))

    if status == WITHERED_TEXT then
        Dalamud.Log("已經枯萎了，跳過，需要人工處理")
        return "withered"
    end

    if state == "empty" then
        Dalamud.Log("空盆，嘗試播種 " .. SEED_ITEM_NAME)
        local okPlant, why2 = tcbRun(IPC.TCToolbox.Plant, patchId, seedId, soilId)
        if not okPlant then
            Dalamud.Log("播種失敗：" .. tostring(why2))
            return "skipped" -- 空盆播種失敗不重試（可能單純缺料）
        end
        return "sown"
    end

    if state == "mature" then
        if not statusMsg:find(TARGET_PLANT_NAME, 1, true) then
            Dalamud.LogDebug("已成熟但不是目標作物，跳過：" .. statusMsg)
            return "skipped"
        end
        local okHarvest, why2 = tcbRun(IPC.TCToolbox.Harvest, patchId)
        if not okHarvest then
            Dalamud.Log("收穫失敗：" .. tostring(why2))
            return "failed"
        end
        yield("/wait 0.4")
        local okPlant, why3 = tcbRun(IPC.TCToolbox.Plant, patchId, seedId, soilId)
        if not okPlant then
            Dalamud.Log("收穫成功但重種失敗：" .. tostring(why3))
        end
        return "harvested"
    end

    if state == "growing" then
        local isTarget = statusMsg:find(TARGET_PLANT_NAME, 1, true) ~= nil

        if status == NEEDS_CARE_TEXT then
            Dalamud.LogDebug("狀態不太好，先護理")
            local okTend = tcbRun(IPC.TCToolbox.Tend, patchId)
            if not okTend then return "failed" end
            if fertId ~= nil and not isTarget then
                tcbRun(IPC.TCToolbox.Fertilize, patchId, fertId)
            end
            return "cared"
        end

        -- 目標作物種植後不施肥（與原生實作同一套策略）
        if isTarget then
            Dalamud.LogDebug(TARGET_PLANT_NAME .. " 種植後不施肥，跳過")
            return "skipped"
        end
        if fertId == nil then return "skipped" end

        local okFert = tcbRun(IPC.TCToolbox.Fertilize, patchId, fertId)
        return okFert and "fertilized" or "skipped"
    end

    Dalamud.Log(string.format("未知狀態（TCB=%s 訊息=[%s]），跳過，請回報這則訊息內容", tostring(state), statusMsg))
    return "unknown"
end

local function tendOnePlotTCBWithRetry(patchId, seedId, soilId, fertId)
    for attempt = 1, RETRY_COUNT + 1 do
        local category = tendOnePlotTCB(patchId, seedId, soilId, fertId)
        if category ~= "failed" then return category end
        if attempt <= RETRY_COUNT then
            Dalamud.Log(string.format("這個物件處理失敗，重試第 %d 次", attempt))
            yield("/wait 0.5")
        else
            Dalamud.Log("重試後仍然失敗，放棄這個物件，繼續下一個")
        end
    end
    return "failed"
end

--============================================================================
-- 主流程
--============================================================================
local summary = {
    harvested = 0, fertilized = 0, cared = 0, sown = 0,
    skipped = 0, withered = 0, unknown = 0, failed = 0,
}
local total = 0
local useTcb = detectTCToolbox()

if useTcb then
    -- TC Toolbox 的 IPC 吃 ItemId，先由背包反查（種子與土壤缺一不可）
    local seedId = itemIdByName(SEED_ITEM_NAME)
    local soilId = itemIdByName(SOIL_ITEM_NAME)
    local fertId = itemIdByName(FERTILIZER_ITEM_LABEL) -- 沒有就不施肥，不算錯誤
    if seedId == nil or soilId == nil then
        Dalamud.Log("背包裡找不到種子或土壤，改走原生實作（它會逐格提示缺料）")
        useTcb = false
    else
        local okList, patches = tcbCall(function() return IPC.TCToolbox.GetNearbyPatches() end)
        if not okList or patches == nil or patches.Count == 0 then
            Dalamud.Log("TC Toolbox 找不到附近的地壟或花盆，改走原生實作")
            useTcb = false
        else
            total = patches.Count
            Dalamud.Log(string.format("找到 %d 個種植物件（TC Toolbox 路徑）", total))
            for i = 0, total - 1 do
                local patchId = patches[i]
                local _, kind = tcbCall(function() return IPC.TCToolbox.GetPatchKind(patchId) end)
                Dalamud.Log(string.format("正在照料第 %d/%d 個種植物件（%s）", i + 1, total,
                    kind == "pot" and "花盆" or "地壟"))
                local category = tendOnePlotTCBWithRetry(patchId, seedId, soilId, fertId)
                Dalamud.Log(string.format("第 %d/%d 個種植物件處理結果：%s", i + 1, total, category))
                summary[category] = (summary[category] or 0) + 1
                local nextDelay = (category == "sown" or category == "harvested") and 0.65 or 0.15
                yield("/wait " .. nextDelay)
            end
        end
    end
end

if not useTcb then
    local plots = findNearbyPlots()
    total = #plots
    Dalamud.Log(string.format("找到 %d 個附近的種植物件（原生路徑）", total))

    for i, plot in ipairs(plots) do
        Dalamud.Log(string.format("正在照料第 %d/%d 個種植物件", i, total))
        local category = tendOnePlot(plot)
        Dalamud.Log(string.format("第 %d/%d 個種植物件處理結果：%s", i, total, category))
        summary[category] = (summary[category] or 0) + 1
        -- 種植後（播種/收穫重種）跟下一個點互動前多留 500ms，避免動畫/狀態還沒更新完就互動
        local nextDelay = (category == "sown" or category == "harvested") and 0.65 or 0.15
        yield("/wait " .. nextDelay)
    end
end

Dalamud.Log("所有附近種植物件處理完畢。")

local report = string.format(
    "庭院整理完畢（%s），共 %d 個物件：收穫重種 %d、施肥 %d、護理 %d、播種 %d、跳過 %d、枯萎待處理 %d、未知狀態 %d、失敗 %d",
    useTcb and "TC Toolbox" or "原生",
    total, summary.harvested, summary.fertilized, summary.cared, summary.sown,
    summary.skipped, summary.withered, summary.unknown, summary.failed)
Dalamud.Log(report)
yield("/echo " .. report)
