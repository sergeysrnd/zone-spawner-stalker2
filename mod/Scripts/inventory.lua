local M={}
function M.capture(controller)
    local config=require('config')
    local chunk,reason=loadfile(config.reader_path)
    if not chunk then error(reason) end
    return chunk().capture(controller,config)
end
return M
