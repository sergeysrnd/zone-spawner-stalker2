local M = {}
function M.parse(line, session, now, catalog)
    local id, token, stamp, item, quantity, advanced = line:match('^([a-f0-9]+)\t([A-Za-z0-9_-]+)\t(%d+)\t([A-Za-z0-9_]+)\t(%d+)\t([01])%s*$')
    if not id or #id ~= 32 or #item > 160 then return nil, 'invalid_request' end
    stamp, quantity = tonumber(stamp), tonumber(quantity)
    if token ~= session then return nil, 'wrong_session' end
    if stamp > now + 2 or now - stamp > 10 then return nil, 'expired' end
    if quantity < 1 or quantity > 999 then return nil, 'invalid_quantity' end
    if catalog[item] == nil then return nil, 'unknown_item' end
    if catalog[item] and advanced ~= '1' then return nil, 'restricted_item' end
    return {id=id, stamp=stamp, item=item, quantity=quantity, command='XCreateItemInInventoryByID '..item..' 0 '..quantity..' 1'}
end
function M.probe(line, now)
    local id, stamp=line:match('^([a-f0-9]+)\t(%d+)%s*$')
    if not id or #id~=32 then return false end
    stamp=tonumber(stamp)
    return stamp<=now+2 and now-stamp<=5
end
function M.parse_inventory(line,session,now)
    local id,token,stamp=line:match('^([a-f0-9]+)\t([A-Za-z0-9_-]+)\t(%d+)%s*$')
    if not id or #id~=32 then return nil,'invalid_request' end
    stamp=tonumber(stamp)
    if token~=session then return nil,'wrong_session' end
    if stamp>now+2 or now-stamp>10 then return nil,'expired' end
    return {id=id,stamp=stamp}
end
function M.parse_world(line,session,now,actors)
    local parts={};for value in (line:gsub('[\r\n]+$','')..'\t'):gmatch('(.-)\t')do parts[#parts+1]=value end
    local id,token,stamp,action=parts[1],parts[2],tonumber(parts[3]),parts[4]
    if not id or #id~=32 or not id:match('^[a-f0-9]+$')or not stamp then return nil,'invalid_request'end
    if token~=session then return nil,'wrong_session'end
    if stamp>now+2 or now-stamp>10 then return nil,'expired'end
    local req={id=id,stamp=stamp,action=action}
    if action=='position'and #parts==4 then return req end
    if action=='teleport'and #parts==7 then
        req.x,req.y,req.z=tonumber(parts[5]),tonumber(parts[6]),tonumber(parts[7])
        for _,key in ipairs({'x','y','z'})do local v=req[key];if not v or v~=v or math.abs(v)>10000000 then return nil,'invalid_position'end end
        return req
    end
    if action=='spawn_actor'and #parts==9 then
        req.actor,req.quantity,req.distance,req.rank=parts[5],tonumber(parts[6]),tonumber(parts[7]),tonumber(parts[8])
        if actors[req.actor]==nil then return nil,'unknown_actor'end
        if actors[req.actor]and parts[9]~='1'then return nil,'restricted_actor'end
        if not req.quantity or req.quantity%1~=0 or req.quantity<1 or req.quantity>5 then return nil,'invalid_quantity'end
        if not req.distance or req.distance~=req.distance or req.distance<5 or req.distance>100 then return nil,'invalid_distance'end
        if not req.rank or req.rank%1~=0 or req.rank<1 or req.rank>4 then return nil,'invalid_rank'end
        return req
    end
    return nil,'invalid_request'
end
return M
