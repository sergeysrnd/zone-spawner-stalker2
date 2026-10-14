local M={}
local function finite(v,limit)return type(v)=='number' and v==v and math.abs(v)<=limit end
local function vector(v)
    local x,y,z=tonumber(v.X),tonumber(v.Y),tonumber(v.Z)
    assert(finite(x,10000000)and finite(y,10000000)and finite(z,10000000),'invalid_position')
    return {x=x,y=y,z=z}
end
local function number(v)return string.format('%.3f',v):gsub(',','.')end
function M.execute(controller,req,kismet)
    -- Only explicit requests reach this code. No actor enumeration or Tick hooks.
    local pawn=controller.Pawn
    local position=vector(pawn:K2_GetActorLocation())
    local rotation=pawn:K2_GetActorRotation()
    local yaw=tonumber(rotation.Yaw)
    assert(finite(yaw,36000),'invalid_rotation')
    if req.action=='position'then
        local data=number(position.x)..'\t'..number(position.y)..'\t'..number(position.z)..'\t'..number(yaw)
        local config=require('config');local flag=config.bridge_dir..'/quest-request.txt';local file=io.open(flag,'rb')
        if file then
            local id=file:read('*l');file:close()
            if id==req.id then
                os.remove(flag);local path=config.world_path:gsub('[^/]+$','quest-job.lua')
                return assert(loadfile(path))().capture(controller,config,data)
            end
        end
        return 'captured',data
    elseif req.action=='teleport'then
        local command='XTeleportTo '..number(req.x)..' '..number(req.y)..' '..number(req.z)
        kismet:ExecuteConsoleCommand(controller,command,controller)
        return 'submitted',number(position.x)..'\t'..number(position.y)..'\t'..number(position.z)..'\t'..number(yaw)
    elseif req.action=='spawn_actor'then
        local angle=yaw*math.pi/180
        -- A bounded burst is handled in this one explicit game-thread callback.
        local x=position.x+math.cos(angle)*req.distance*100
        local y=position.y+math.sin(angle)*req.distance*100
        -- One actor per command; quantity is bounded to keep a burst below five commands.
        for n=1,req.quantity do
            local lateral=(n-(req.quantity+1)/2)*250
            local sx=x-math.sin(angle)*lateral;local sy=y+math.cos(angle)*lateral
            local command='XSpawnObjBySID '..req.actor..' '..req.rank..' true '..number(sx)..' '..number(sy)..' '..number(position.z+30)..' 0 '..number(yaw+180)..' 0'
            kismet:ExecuteConsoleCommand(controller,command,controller)
        end
        return 'submitted',req.actor
    end
    error('unknown_action')
end
return M
