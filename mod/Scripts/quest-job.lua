local M={}
local function valid(o)return o and o:IsValid()end
local function read(path)local f=io.open(path,'rb');if not f then return nil end;local s=f:read('*a');f:close();return s end
local function write(path,value)local f=assert(io.open(path..'.tmp','wb'));f:write(value);f:close();os.remove(path);assert(os.rename(path..'.tmp',path))end
local function number(v)return string.format('%.3f',v):gsub(',','.')end
local function finite(v)return type(v)=='number'and v==v and math.abs(v)<=10000000 end
function M.capture(controller,config,position_data)
    local map=_G.ZoneSpawnerQuestMap
    if not valid(map)or map.Markers:GetArrayNum()==0 then
        map=nil
        for _,candidate in ipairs(FindAllOf('WorldMap')or {})do
            if valid(candidate)and candidate.Markers:GetArrayNum()>0 then map=candidate;break end
        end
        _G.ZoneSpawnerQuestMap=map
    end
    assert(valid(map)and map.Markers:GetArrayNum()>0,'open_pda_map')
    local pawn=controller.Pawn;local pawn_position=pawn:K2_GetActorLocation()
    local player_map=map.PlayerPosition.RenderTransform.Translation
    -- Calibrate against the live player, rather than trusting the old mod's map scale.
    assert(finite(player_map.X)and finite(player_map.Y)and math.abs(player_map.X*25-pawn_position.X)<100 and math.abs(player_map.Y*25-pawn_position.Y)<100,'map_coordinates_not_calibrated')
    local uuid='';for i=1,32 do uuid=uuid..string.format('%x',math.random(0,15))end
    local session=assert(read(config.bridge_dir..'/status.txt')):match('^([A-Za-z0-9_-]+)\t')
    local total=map.Markers:GetArrayNum();local index=1;local rows={};local textures={};local queued,done=false,false;local started=os.time();local address=pawn:GetAddress();local chunks,max_chunk_ms=0,0
    local mediator=StaticFindObject('/Script/Stalker2.Default__CppMediator')
    local main_count,side_count=#mediator:GetActiveMainQuests(),#mediator:GetActiveSecondaryQuests()
    local function finish(state,detail)
        if done then return end;done=true
        write(config.bridge_dir..'/world-result.txt',uuid..'\t'..session..'\t'..os.time()..'\t'..state..'\t'..detail..'\n'..position_data..'\nQINFO\t'..main_count..'\t'..side_count..'\n'..table.concat(rows,'\n')..'\n')
        write(config.bridge_dir..'/quest-performance.txt','markers='..(index-1)..'\tchunks='..chunks..'\tchunks_max_ms='..number(max_chunk_ms)..'\telapsed_seconds='..(os.time()-started)..'\n')
    end
    LoopAsync(50,function()
        if done then return true end
        local cancel=read(config.bridge_dir..'/world-cancel.txt')
        if cancel and cancel:match('^([a-f0-9]+)')==uuid then finish('error','cancelled');return true end
        if os.time()-started>15 then finish('error','quest_timeout');return true end
        if queued then return false end;queued=true
        local scheduled,reason=pcall(ExecuteInGameThread,function()
            if done then queued=false;return end
            local ok,err=pcall(function()
                assert(valid(map)and map.Markers:GetArrayNum()==total,'open_pda_map')
                assert(valid(pawn)and valid(controller.Pawn)and controller.Pawn:GetAddress()==address,'load_changed')
                local begin=os.clock();local chunk=0
                repeat
                    local marker=map.Markers[index]
                    if valid(marker)and marker:IsVisible()and valid(marker.MarkerIcon)then
                        local resource=marker.MarkerIcon.Brush.ResourceObject
                        if valid(resource)then
                            local key=resource:GetAddress();local kind=textures[key]
                            if not kind then
                                local path=resource:GetFullName():lower()
                                kind=path:find('texture_quest_active_general_shadow',1,true)and 'tracked'or path:find('texture_quest_notactive_general_shadow',1,true)and 'map'or 'other'
                                textures[key]=kind
                            end
                            if kind~='other'then
                                local pos=marker.Slot:GetPosition();local x,y=tonumber(pos.X)*25,tonumber(pos.Y)*25
                                assert(finite(x)and finite(y),'invalid_quest_position')
                                local z='?';local floor={}
                                local floor_ok=pcall(function()pawn.CharacterMovement:K2_ComputeFloorDist({X=x,Y=y,Z=pawn_position.Z+3000},10000,10000,50,floor)end)
                                if floor_ok and floor.bBlockingHit and floor.bWalkableFloor and type(floor.HitResult)=='table'then
                                    local hit=floor.HitResult;local p=hit.ImpactPoint;local normal=hit.ImpactNormal
                                    if p and finite(p.Z)and normal and tonumber(normal.Z)and normal.Z>=0.6 and (tonumber(hit.PenetrationDepth)or 0)<=0.1 then z=number(p.Z+pawn.CapsuleComponent:GetScaledCapsuleHalfHeight()+10)end
                                end
                                local title=kind=='tracked'and 'Отслеживаемая цель'or 'Неотслеживаемая метка задания'
                                rows[#rows+1]='Q\t'..index..'\t'..kind..'\t'..number(x)..'\t'..number(y)..'\t'..z..'\t'..title
                                index=index+1;chunk=chunk+1;break
                            end
                        end
                    end
                    index=index+1;chunk=chunk+1
                until index>total or chunk>=16 or os.clock()-begin>=0.001
                chunks=chunks+1;max_chunk_ms=math.max(max_chunk_ms,(os.clock()-begin)*1000)
                if index>total then finish('captured','quest_markers')end
            end)
            queued=false;if not ok then finish('error',tostring(err):gsub('[\r\n\t]',' '))end
        end)
        if not scheduled then queued=false;finish('error',tostring(reason):gsub('[\r\n\t]',' '));return true end
        return false
    end)
    return 'pending',uuid
end
return M
