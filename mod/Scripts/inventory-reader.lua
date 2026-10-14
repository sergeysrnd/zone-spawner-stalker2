local M={}
function M.capture(controller,config)
    local base=config.reader_path:gsub('[^/]+$','')
    local flag=config.bridge_dir..'/inventory-diagnostic-request.txt'
    local f=io.open(flag,'rb')
    local module='inventory-job.lua'
    if f then f:close();os.remove(flag);module='inventory-diagnostics.lua' end
    local chunk,reason=loadfile(base..module);assert(chunk,reason)
    return chunk().capture(controller,config)
end
return M
