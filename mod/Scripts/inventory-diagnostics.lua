local M={}
local function valid(o)return o and o:IsValid()end
function M.capture(controller,config)
    local lines={};local function line(v)lines[#lines+1]=tostring(v)end
    local function attempt(name,fn)line(name);local ok,err=pcall(fn);if not ok then line('error: '..tostring(err))end end
    local mediator=StaticFindObject('/Script/Stalker2.Default__CppMediator')
    attempt('CppMediator counts',function()
        assert(valid(mediator),'mediator unavailable');line(mediator:GetFullName())
        for _,id in ipairs({'Medkit','Bandage','A545D','A545A','A762D','A762NATOS'})do line(id..'='..tostring(mediator:GetItemsCountInInventory(controller.Pawn,id)))end
    end)
    local function describe(o)
        if not valid(o)then line('unavailable');return end
        line(o:GetFullName());o:ForEachProperty(function(p)line(p:GetFullName())end)
    end
    attempt('Mediator functions',function()mediator:GetClass():ForEachFunction(function(fn)local n=fn:GetFullName();if n:find('Quest')or n:find('Marker')or n:find('Ground')or n:find('SafeZone')then describe(fn)end end)end)
    attempt('JournalQuest structure',function()describe(StaticFindObject('/Script/Stalker2.JournalQuest'))end)
    for _,class in ipairs({'WorldMapManager','WorldMap','WorldMapMarker'})do attempt(class..' schema',function()describe(StaticFindObject('/Script/Stalker2.'..class))end)end
    attempt('Active quests',function()
        local quests=mediator:GetActiveMainQuests();line('Main type='..type(quests)..' count='..#quests)
        for i,q in ipairs(quests)do line('Main '..i..' type='..type(q));if type(q)=='table'then for k,v in pairs(q)do line(tostring(k)..'='..tostring(v))end end end
        local secondary=mediator:GetActiveSecondaryQuests();line('Secondary count='..#secondary)
    end)
    for _,name in ipairs({'GetQuestInfo','GetMatchedQuestSID','GetNavMeshSpawnLocation','IsVisibleMarkerState'})do attempt(name,function()describe(FindObject('Function',name))end)end
    attempt('Floor API',function()
        local floor={};local real=controller.Pawn:K2_GetActorLocation()
        controller.Pawn.CharacterMovement:K2_ComputeFloorDist({X=422485.498046875,Y=414696.58203125,Z=real.Z+3000},10000,10000,50,floor)
        for k,v in pairs(floor)do line('Floor '..tostring(k)..'='..tostring(v));if type(v)=='table'then for k2,v2 in pairs(v)do line('  '..tostring(k2)..'='..tostring(v2))end end end
    end)
    attempt('World map instance',function()
        local maps=FindAllOf('WorldMap')or {};line('Map candidates='..#maps)
        local map
        for _,candidate in ipairs(maps)do
            line('Candidate '..candidate:GetFullName()..' class '..candidate:GetClass():GetFullName())
            local ok,num=pcall(function()return candidate.Markers:GetArrayNum()end)
            if ok then line('count='..num);if num>0 then map=candidate end end
        end
        if not valid(map)then line('not_open');return end
        local class=map:GetClass();describe(class);class:ForEachFunction(describe)
        line('Markers:'..tostring(map.Markers)..' num='..map.Markers:GetArrayNum())
        map.Markers:ForEach(function(i,entry)
            if i>3 then return true end
            local marker=entry:get();line('Index '..i..' marker '..tostring(marker));if valid(marker)then describe(marker:GetClass());marker:GetClass():ForEachFunction(describe)end
        end)
        line('Landmarks:'..map.Landmarks:GetArrayNum())
        local player=map.PlayerPosition;line('Player widget '..player:GetFullName())
        local pos=player.Slot.LayoutData.Offsets;line('Player canvas '..pos.Left..' '..pos.Top)
        local real=controller.Pawn:K2_GetActorLocation();line('Player world '..real.X..' '..real.Y..' '..real.Z)
        local transform=player.RenderTransform.Translation;line('Player render '..transform.X..' '..transform.Y)
        local seen={}
        map.Markers:ForEach(function(i,entry)
            local marker=entry:get()
            if valid(marker)then
                local ok,err=pcall(function()
                    local icon=marker.MarkerIcon;local resource=icon.Brush.ResourceObject
                    local texture=valid(resource)and resource:GetFullName()or 'none'
                    local pos=marker.Slot:GetPosition();local render=marker.RenderTransform.Translation
                    local kind=texture:lower();local quest=kind:find('quest')or kind:find('mission')
                    if not seen[texture]or quest then
                        seen[texture]=true;line('Marker '..i..' visible='..tostring(marker:IsVisible())..' icon='..texture..' canvas='..pos.X..','..pos.Y..' render='..render.X..','..render.Y)
                        if kind:find('/active/')and quest then
                            local floor={};local movement=controller.Pawn.CharacterMovement
                            movement:K2_ComputeFloorDist({X=pos.X*25,Y=pos.Y*25,Z=real.Z+3000},10000,10000,50,floor)
                            for k,v in pairs(floor)do line('Floor '..tostring(k)..'='..tostring(v));if type(v)=='table'then for k2,v2 in pairs(v)do line('  '..tostring(k2)..'='..tostring(v2))end end end
                        end
                    end
                end)
                if not ok then line('marker error '..i..':'..tostring(err))end
            end
        end)
    end)
    return table.concat(lines,'\n')
end
return M
