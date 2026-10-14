local M={}
local function read(path)local f=io.open(path,'rb');if not f then return nil end;local s=f:read('*a');f:close();return s end
local function write(path,value)local f=assert(io.open(path..'.tmp','wb'));f:write(value);f:close();os.remove(path);assert(os.rename(path..'.tmp',path))end
local function valid(o)return o and o:IsValid()end
function M.capture(controller,config)
    if _G.ZoneSpawnerInventoryJob and not _G.ZoneSpawnerInventoryJob.done then
        return {state='pending',detail=_G.ZoneSpawnerInventoryJob.id}
    end
    local mediator=_G.ZoneSpawnerInventoryMediator
    if not valid(mediator)then mediator=StaticFindObject('/Script/Stalker2.Default__CppMediator');_G.ZoneSpawnerInventoryMediator=mediator end
    assert(valid(mediator),'inventory_api_unavailable')
    local ids_path=config.catalog_path:gsub('[^/]+$','inventory-ids.txt')
    local ids={};for id in io.lines(ids_path)do
        if id:match('^[A-Za-z0-9_]+$')and #id<=160 then ids[#ids+1]=id end
    end
    assert(#ids>0,'inventory_catalog_empty')
    local session=assert(read(config.bridge_dir..'/status.txt')):match('^([A-Za-z0-9_-]+)\t')
    local uuid='';for i=1,32 do uuid=uuid..string.format('%x',math.random(0,15))end
    local pawn=controller.Pawn;local address=pawn:GetAddress()
    local job={id=uuid,session=session,index=1,rows={},started=os.time(),queued=false,done=false,max_chunk_ms=0,calls=0}
    _G.ZoneSpawnerInventoryJob=job
    local result=config.bridge_dir..'/inventory-result.txt'
    local function finish(state,detail)
        if job.done then return end;job.done=true
        local rows=state=='ready'and table.concat(job.rows,'\n')or ''
        write(result,job.id..'\t'..session..'\t'..os.time()..'\t'..state..'\t'..tostring(detail):gsub('[\r\n\t]',' ')..'\n'..rows..'\n')
        write(config.bridge_dir..'/inventory-performance.txt','calls='..job.calls..'\tchunks_max_ms='..string.format('%.3f',job.max_chunk_ms)..'\telapsed_seconds='..(os.time()-job.started)..'\n')
    end
    LoopAsync(50,function()
        if job.done then return true end
        local cancel=read(config.bridge_dir..'/inventory-cancel.txt')
        if cancel and cancel:match('^([a-f0-9]+)')==job.id then finish('error','cancelled');return true end
        if os.time()-job.started>15 then finish('error','inventory_timeout');return true end
        if job.queued then return false end;job.queued=true
        local scheduled,reason=pcall(ExecuteInGameThread,function()
            if job.done then job.queued=false;return end
            local success,err=xpcall(function()
                assert(valid(controller)and valid(controller.Pawn)and valid(pawn)and controller.Pawn:GetAddress()==address,'load_changed')
                local started=os.clock();local chunk=0
                repeat
                    local id=ids[job.index];local count=tonumber(mediator:GetItemsCountInInventory(pawn,id))
                    assert(count and count>=0 and count%1==0 and count<=100000000,'invalid_inventory_count')
                    if count>0 then job.rows[#job.rows+1]=id..'\t'..count end
                    job.index=job.index+1;job.calls=job.calls+1;chunk=chunk+1
                until job.index>#ids or chunk>=16 or os.clock()-started>=0.001
                job.max_chunk_ms=math.max(job.max_chunk_ms,(os.clock()-started)*1000)
                if job.index>#ids then finish('ready','prototype_snapshot')end
            end,debug.traceback)
            job.queued=false;if not success then finish('error',err)end
        end)
        if not scheduled then job.queued=false;finish('error',reason);return true end
        return false
    end)
    return {state='pending',detail=job.id}
end
return M
