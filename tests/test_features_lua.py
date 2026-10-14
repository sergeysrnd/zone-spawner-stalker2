import pathlib,sys,tempfile,time,uuid

from lupa.lua54 import LuaRuntime
scripts=(pathlib.Path(__file__).resolve().parents[1]/'mod/Scripts').resolve()
lua=LuaRuntime(unpack_returned_tuples=True);g=lua.globals();g.clock=int(time.time())
with tempfile.TemporaryDirectory(prefix='zone-features-')as name:
    bridge=pathlib.Path(name);(bridge/'items.tsv').write_text('Medkit\t0\n');(bridge/'actors.tsv').write_text('Bloodsucker\t0\nRichter\t1\n')
    g.test_bridge=bridge.as_posix();g.test_scripts=scripts.as_posix()
    lua.execute('''
    package.path=test_scripts..'/?.lua;'..package.path
    package.preload.config=function()return {bridge_dir=test_bridge,catalog_path=test_bridge..'/items.tsv',actors_path=test_bridge..'/actors.tsv',world_path=test_scripts..'/world.lua'}end
    in_game=false;callbacks=0;scans=0;calls={};inventory_reads=0;fail_inventory=false;fail_schedule=false;defer=false
    os.time=function()return clock end
    local function valid()assert(in_game);return true end
    pawn={IsValid=valid,K2_GetActorLocation=function()assert(in_game);return {X=100,Y=200,Z=300}end,K2_GetActorRotation=function()assert(in_game);return {Yaw=90}end}
    pc={IsValid=valid,IsLocalPlayerController=valid,Pawn=pawn}
    engine={IsValid=valid,GameViewport={IsValid=valid}}
    gameplay={IsValid=valid,GetPlayerController=function()assert(in_game);return pc end}
    kismet={IsValid=valid,ExecuteConsoleCommand=function(self,world,command,controller)assert(in_game and world==pc and controller==pc);calls[#calls+1]=command end}
    function FindFirstOf(name)assert(in_game and name=='Engine');scans=scans+1;return engine end
    function StaticFindObject(name)assert(in_game);scans=scans+1;return name:find('GameplayStatics')and gameplay or kismet end
    function ExecuteInGameThread(callback)callbacks=callbacks+1;if fail_schedule then error('schedule failure')end;if defer then delayed=callback;return end;in_game=true;callback();in_game=false end
    function LoopAsync(ms,callback)assert(ms==500);tick=callback end
    package.preload.inventory=function()return {capture=function()assert(in_game);inventory_reads=inventory_reads+1;if fail_inventory then error('inventory failure')end;return {rows={{id='Medkit',count=3},{id='UnknownSID',count=1}}}end}end
    ''')
    lua.execute((scripts/'main.lua').read_text(encoding='utf8'))
    session=(bridge/'status.txt').read_text().split('\t')[0]
    def request(file,payload=''):
        request_id=uuid.uuid4().hex
        (bridge/file).write_text(f'{request_id}\t{session}\t{g.clock}{payload}\n');g.tick();return request_id
    for _ in range(120):g.tick()
    assert g.callbacks==g.scans==g.inventory_reads==0
    inv_id=request('inventory-request.txt')
    response=(bridge/'inventory-response.txt').read_text()
    assert response.startswith(inv_id+'\t'+session)and '\tready\t'in response and 'Medkit\t3\n'in response
    reads=g.inventory_reads
    for _ in range(120):g.tick()
    assert g.inventory_reads==reads
    pos_id=request('world-request.txt','\tposition')
    assert (bridge/'world-response.txt').read_text().endswith('100.000\t200.000\t300.000\t90.000')
    assert len(g.calls)==0,'Read-only position changed game state'
    request('world-request.txt','\tteleport\t-1.5\t2\t3')
    assert list(g.calls.values())==['XTeleportTo -1.500 2.000 3.000']
    request('world-request.txt','\tspawn_actor\tBloodsucker\t2\t10\t1\t0')
    assert len(g.calls)==3
    assert g.calls[2]=='XSpawnObjBySID Bloodsucker 1 true 225.000 1200.000 330.000 0 270.000 0'
    assert g.calls[3]=='XSpawnObjBySID Bloodsucker 1 true -25.000 1200.000 330.000 0 270.000 0'
    callbacks=g.callbacks
    for payload in ['\tspawn_actor\tRichter\t1\t10\t1\t0','\tspawn_actor\tMissing\t1\t10\t1\t0','\tspawn_actor\tBloodsucker\t6\t10\t1\t0','\tspawn_actor\tBloodsucker\t1\t101\t1\t0','\tteleport\tnan\t1\t2','\tteleport\t10000001\t1\t2','\tquit']:
        request('world-request.txt',payload);assert '\terror\t'in(bridge/'world-response.txt').read_text()
    assert callbacks==g.callbacks,'Rejected request performed Unreal calls'
    g.fail_inventory=True;request('inventory-request.txt');assert 'inventory failure'in(bridge/'inventory-response.txt').read_text();g.fail_inventory=False
    g.fail_schedule=True;request('inventory-request.txt');assert 'schedule failure'in(bridge/'inventory-response.txt').read_text();g.fail_schedule=False
    # An expired spawn must not discard a simultaneous valid inventory request.
    g.defer=True
    (bridge/'request.txt').write_text(f'{uuid.uuid4().hex}\t{session}\t{g.clock-9}\tMedkit\t1\t0\n')
    current=request('inventory-request.txt');g.clock+=2;g.in_game=True;g.delayed();g.in_game=False
    assert '\trejected\texpired'in(bridge/'response.txt').read_text()
    assert (bridge/'inventory-response.txt').read_text().startswith(current+'\t')and '\tready\t'in(bridge/'inventory-response.txt').read_text()
    before=g.callbacks
    for _ in range(120):g.tick()
    assert g.callbacks==before
print('PASS: inventory and position read-only requests, independent expiry, scheduling failures, actor distance/grid/count/whitelist, teleport validation and zero idle Unreal work')
