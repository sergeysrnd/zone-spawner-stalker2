local config = require('config')
local protocol = require('protocol')
local session = tostring(os.time())..'_'..tostring(math.random(100000,999999))
local queued, seen, last_state = false, {}, ''
local engine, gameplay, kismet
local catalog,actors = {},{}
for line in io.lines(config.catalog_path) do
    local id, restricted = line:match('^([A-Za-z0-9_]+)\t([01])$')
    if id then catalog[id] = restricted == '1' end
end
if config.actors_path then
    for line in io.lines(config.actors_path)do
        local id,restricted=line:match('^([A-Za-z0-9_]+)\t([01])$')
        if id then actors[id]=restricted=='1'end
    end
end
local function log(message) print('[ZoneSpawner] '..message..'\n') end
local function read(name)
    local f=io.open(config.bridge_dir..'/'..name,'rb')
    if not f then return nil end
    local value=f:read('*a');f:close();return value
end
local function write(name,value)
    local path=config.bridge_dir..'/'..name
    local f=io.open(path..'.tmp','wb')
    if not f then error('Cannot write bridge file: '..name) end
    f:write(value);f:close();os.remove(path);assert(os.rename(path..'.tmp',path))
end
local function valid(object) return object and object:IsValid() end
local function player()
    if not valid(engine) then engine=FindFirstOf('Engine') end
    if not valid(engine) or not valid(engine.GameViewport) then return nil end
    if not valid(gameplay) then gameplay=StaticFindObject('/Script/Engine.Default__GameplayStatics') end
    if not valid(gameplay) then return nil end
    local controller=gameplay:GetPlayerController(engine.GameViewport,0)
    if not valid(controller) or not controller:IsLocalPlayerController() or not valid(controller.Pawn) then return nil end
    return controller
end
local function status(controller)
    local state=controller and 'ready' or 'waiting'
    write('status.txt',session..'\t'..state..'\t'..os.time()..'\n')
    if state~=last_state then log('state='..state);last_state=state end
end
local function response(req,state,detail)
    write('response.txt',req.id..'\t'..state..'\t'..tostring(detail):gsub('[\r\n\t]',' ')..'\n')
end
local function inventory_response(req,state,detail,data)
    write('inventory-response.txt',req.id..'\t'..session..'\t'..os.time()..'\t'..state..'\t'..tostring(detail):gsub('[\r\n\t]',' ')..'\n'..(data or ''))
end
local function world_response(req,state,detail,data)
    write('world-response.txt',req.id..'\t'..session..'\t'..os.time()..'\t'..state..'\t'..tostring(detail):gsub('[\r\n\t]',' ')..'\n'..(data or ''))
end
local function console_library()
    if not valid(kismet)then kismet=StaticFindObject('/Script/Engine.Default__KismetSystemLibrary')end
    assert(valid(kismet),'KismetSystemLibrary unavailable');return kismet
end
local function dispatch(req,inventory_req,world_req)
    -- UObject access stays on the game thread, including cached-object validity checks.
    if req and os.time()-req.stamp>10 then response(req,'rejected','expired');req=nil end
    local controller=player()
    status(controller)
    if inventory_req then
        local state,detail='error','load_a_save'
        if os.time()-inventory_req.stamp>10 then detail='expired'
        elseif controller then
            local ok,raw=pcall(function()return require('inventory').capture(controller)end)
            if ok and type(raw)=='table'then
                local rows={}
                for _,row in ipairs(raw.rows or {})do
                    assert(type(row.id)=='string'and row.id:match('^[A-Za-z0-9_]+$')and #row.id<=160,'invalid_inventory_id')
                    assert(type(row.count)=='number'and row.count>0 and row.count%1==0 and row.count<=100000000,'invalid_inventory_count')
                    rows[#rows+1]=row.id..'\t'..row.count
                end
                inventory_response(inventory_req,raw.state or 'ready',raw.detail or 'snapshot',table.concat(rows,'\n')..'\n');inventory_req=nil
            elseif ok then write('inventory-raw.txt',raw);state=raw~='' and 'captured' or 'error';detail=raw~='' and 'diagnostic_capture' or 'inventory_output_empty'
            else detail=tostring(raw):gsub('[\r\n\t]',' ') end
        end
        if inventory_req then inventory_response(inventory_req,state,detail)end
    end
    if world_req then
        if os.time()-world_req.stamp>10 then world_response(world_req,'error','expired')
        elseif not controller then world_response(world_req,'error','load_a_save')
        else
            local ok,state,data=pcall(function()
                local chunk,reason=loadfile(config.world_path);assert(chunk,reason)
                return chunk().execute(controller,world_req,console_library())
            end)
            if ok then world_response(world_req,state,world_req.action,data)else world_response(world_req,'error',state)end
        end
    end
    if not req then return end
    if not controller then response(req,'rejected','load_a_save');return end
    local ok,err=pcall(function()
        console_library():ExecuteConsoleCommand(controller,req.command,controller)
    end)
    response(req,ok and 'submitted' or 'error',ok and req.item or err)
    log(ok and ('Submitted '..req.item..' x'..req.quantity) or tostring(err))
end
status(nil)
log('Loaded in on-demand mode. No idle UObject scans or game-thread polling.')
if config.launcher_path then
    local ok,started,reason=pcall(function()return require('launcher').start(config)end)
    if ok and started then log('Hidden menu startup requested for this game process.')
    else log('Menu startup failed: '..tostring(ok and reason or started)) end
end
local function poll()
    if queued then return false end
    local req
    local line=read('request.txt')
    if line then
        os.remove(config.bridge_dir..'/request.txt')
        local reason
        req,reason=protocol.parse(line,session,os.time(),catalog)
        local request_id=line:match('^([a-f0-9]+)')
        if not req then
            if request_id and #request_id==32 then response({id=request_id},'rejected',reason) end
            log('Request rejected: '..reason)
        elseif seen[req.id] then
            response(req,'rejected','already_processed');req=nil
        else
            seen[req.id]=true
        end
    end
    local probe=read('probe.txt')
    if probe then os.remove(config.bridge_dir..'/probe.txt') end
    local inventory_req
    local inventory_line=read('inventory-request.txt')
    if inventory_line then
        os.remove(config.bridge_dir..'/inventory-request.txt')
        local reason
        inventory_req,reason=protocol.parse_inventory(inventory_line,session,os.time())
        if inventory_req and seen[inventory_req.id] then inventory_req=nil;reason='already_processed' end
        if inventory_req then seen[inventory_req.id]=true
        else
            local id=inventory_line:match('^([a-f0-9]+)')
            if id and #id==32 then write('inventory-response.txt',id..'\t'..session..'\t'..os.time()..'\terror\t'..reason..'\n') end
        end
    end
    local world_req
    local world_line=read('world-request.txt')
    if world_line then
        os.remove(config.bridge_dir..'/world-request.txt')
        local reason;world_req,reason=protocol.parse_world(world_line,session,os.time(),actors)
        if world_req and seen[world_req.id]then world_req=nil;reason='already_processed'end
        if world_req then seen[world_req.id]=true else
            local id=world_line:match('^([a-f0-9]+)');if id and #id==32 then world_response({id=id},'error',reason)end
        end
    end
    -- Polling these small files requires no Unreal calls while the menu is hidden.
    if not req and not inventory_req and not world_req and not (probe and protocol.probe(probe,os.time())) then return false end
    queued=true
    local ok,err=pcall(ExecuteInGameThread,function()
        local success,failure=xpcall(function()dispatch(req,inventory_req,world_req)end,debug.traceback)
        if not success then
            log(failure)
            if req then pcall(response,req,'error',failure) end
            if inventory_req then pcall(inventory_response,inventory_req,'error',failure)end
            if world_req then pcall(world_response,world_req,'error',failure)end
        end
        queued=false
    end)
    if not ok then
        queued=false;log(err)
        if req then pcall(response,req,'error',err) end
        if inventory_req then pcall(inventory_response,inventory_req,'error',err)end
        if world_req then pcall(world_response,world_req,'error',err)end
    end
    return false
end
LoopAsync(500,function()
    local ok,err=xpcall(poll,debug.traceback)
    if not ok then log(err) end
    return false
end)
