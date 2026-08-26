--[[
無條件護理附近所有園圃/花盆（不管狀態，每個都選「護理」）

沿用 tend_garden.lua/harvest_and_replant.lua 已驗證過的機制：
  - 物件名稱規則（園圃 / XX花盆 / XX花圃）
  - Talk 對話框要先點掉才會進到 SelectString
  - 選單裡的標題行（含冒號）要濾掉，不然選項 index 會偏移
  - 「護理」在不需要時遊戲只會顯示提示訊息，不會出錯，所以不用判斷狀態，直接選就好
]]

local function isPlantableName(name)
    return name == "園圃" or name:match("花盆$") ~= nil or name:match("花圃$") ~= nil
end

local INTERACT_RANGE = 3.0
local MAX_OBJECT_INDEX = 599
local CARE_LABEL = "護理"
local CANCEL_LABEL = "取消"

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

local function careOnePlot(entity)
    Chat.ClearLastMessage()
    entity:SetAsTarget()
    yield("/wait 0.15")
    entity:Interact()

    if not waitForSelectStringAfterInteract(3000) then
        Dalamud.Log(string.format("互動失敗，跳過：%s", tostring(Chat.GetLastMessage())))
        return
    end

    local options = readSelectStringOptions()
    local careIdx = optionIndex(options, CARE_LABEL)
    if careIdx then
        Dalamud.LogDebug("選擇護理")
        selectOption(careIdx)
        Dalamud.Log(string.format("護理結果：%s", tostring(Chat.GetLastMessage())))
    else
        Dalamud.LogDebug("沒有護理選項，取消跳過：選項=[" .. table.concat(options, ",") .. "]")
        cancelIfPossible(options)
    end
end

local plots = findNearbyPlots()
Dalamud.Log(string.format("找到 %d 個附近的種植物件", #plots))

for i, plot in ipairs(plots) do
    Dalamud.Log(string.format("正在護理第 %d/%d 個", i, #plots))
    careOnePlot(plot)
    yield("/wait 0.15")
end

Dalamud.Log("所有附近種植物件護理完畢。")
