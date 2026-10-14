local M = {}
function M.start(config)
    local launch,reason=package.loadlib(config.launcher_path,'launch_menu')
    if not launch then return false,reason end
    local ok,err=pcall(launch)
    if not ok then return false,err end
    local f=io.open(config.bridge_dir..'/launcher-status.txt','rb')
    if not f then return false,'launcher status unavailable' end
    local result=f:read('*a');f:close()
    if not result:match('^started\t%d+\t%d+\t0%s*$') then return false,result end
    return true
end
return M
