import pathlib,sys,tempfile

from lupa.lua54 import LuaRuntime
scripts=(pathlib.Path(__file__).resolve().parents[1]/'mod/Scripts').resolve();lua=LuaRuntime(unpack_returned_tuples=True);g=lua.globals()
with tempfile.TemporaryDirectory(prefix='zone-jobs-')as name:
    r=pathlib.Path(name);g.root=r.as_posix();g.script_dir=scripts.as_posix()
    ids=['Bandage']+[f'LongItemIdentifier_{i:03}'for i in range(80)];(r/'inventory-ids.txt').write_text('\n'.join(ids)+'\n');(r/'status.txt').write_text('session_test\tready\t100\n')
    lua.execute('''
    package.path=script_dir..'/?.lua;'..package.path
    clock=100;os.time=function()return clock end;in_game=false;workers={};counts={};game_calls=0;finds=0
    local function valid()assert(in_game,'Object accessed outside game thread');return true end
    local function address()assert(in_game);return 123 end
    pawn={IsValid=valid,GetAddress=address,K2_GetActorLocation=function()assert(in_game);return {X=25000,Y=50000,Z=500}end}
    pc={IsValid=valid,Pawn=pawn};config={bridge_dir=root,catalog_path=root..'/items.tsv'}
    mediator={IsValid=valid,GetItemsCountInInventory=function(self,actor,id)assert(in_game and actor==pawn);counts[#counts+1]=id;return id=='Bandage'and 12 or 0 end,
      GetActiveMainQuests=function()assert(in_game);return {1}end,GetActiveSecondaryQuests=function()assert(in_game);return {1,2}end}
    function StaticFindObject(path)assert(in_game);finds=finds+1;return mediator end
    function ExecuteInGameThread(fn)game_calls=game_calls+1;in_game=true;fn();in_game=false end
    function LoopAsync(delay,fn)assert(delay==50);workers[#workers+1]=fn end
    function step()for i=#workers,1,-1 do if workers[i]()then table.remove(workers,i)end end end
    function capture_inventory()in_game=true;local r=require('inventory-job').capture(pc,config);in_game=false;return r end
    ''')
    first=g.capture_inventory();assert first['state']=='pending'
    while len(g.workers):g.step()
    assert list(g.counts.values())==ids,'Prototype file must be read by full lines, not single bytes'
    raw=(r/'inventory-result.txt').read_text();assert '\tready\t'in raw and raw.endswith('Bandage\t12\n')
    before=g.game_calls
    for _ in range(120):g.step()
    assert before==g.game_calls
    second=g.capture_inventory();(r/'inventory-cancel.txt').write_text(second['detail']+'\nsession_test')
    before=len(g.counts);g.step();assert len(g.counts)==before and 'cancelled'in(r/'inventory-result.txt').read_text()
    lua.execute('''
    local function resource(name,id)return {IsValid=function()assert(in_game);return true end,GetAddress=function()assert(in_game);return id end,GetFullName=function()assert(in_game);return name end}end
    local function marker(texture,id,x,y)return {IsValid=function()assert(in_game);return true end,IsVisible=function()assert(in_game);return true end,
      MarkerIcon={IsValid=function()assert(in_game);return true end,Brush={ResourceObject=resource(texture,id)}},Slot={GetPosition=function()assert(in_game);return {X=x,Y=y}end}}end
    markers={marker('location',1,5,5),marker('Texture_Quest_Active_General_Shadow',2,40,80),marker('Texture_Quest_NotActive_General_Shadow',3,80,100)}
    function markers:GetArrayNum()assert(in_game);return 3 end
    map={IsValid=function()assert(in_game);return true end,Markers=markers,PlayerPosition={RenderTransform={Translation={X=1000,Y=2000}}}}
    function FindAllOf(name)assert(in_game and name=='WorldMap');finds=finds+1;return {map}end
    pawn.CapsuleComponent={GetScaledCapsuleHalfHeight=function()assert(in_game);return 88 end}
    pawn.CharacterMovement={K2_ComputeFloorDist=function(self,position,line,sweep,radius,out)
      assert(in_game and line==10000 and sweep==10000 and radius==50)
      if position.X==1000 then out.bBlockingHit=true;out.bWalkableFloor=true;out.HitResult={ImpactPoint={Z=200},ImpactNormal={Z=1},PenetrationDepth=0}end
    end}
    function capture_quests()in_game=true;local state,id=require('quest-job').capture(pc,config,'25000\t50000\t500\t0');in_game=false;return state,id end
    ''')
    state,job=g.capture_quests();assert state=='pending'
    while len(g.workers):g.step()
    raw=(r/'world-result.txt').read_text();assert raw.startswith(job+'\tsession_test')and '\tcaptured\tquest_markers'in raw
    assert 'Q\t2\ttracked\t1000.000\t2000.000\t298.000\t'in raw
    assert 'Q\t3\tmap\t2000.000\t2500.000\t?\t'in raw
    assert 'QINFO\t1\t2'in raw
    state,job=g.capture_quests();(r/'world-cancel.txt').write_text(job+'\n');before=g.game_calls;g.step();assert g.game_calls==before and 'cancelled'in(r/'world-result.txt').read_text()
    before=g.game_calls
    for _ in range(120):g.step()
    assert g.game_calls==before
    lua.execute('map.PlayerPosition.RenderTransform.Translation.X=0')
    try:g.capture_quests();raise AssertionError('Uncalibrated map accepted')
    except Exception as e:assert 'map_coordinates_not_calibrated'in str(e)
print('PASS: full prototype IDs and exact counts, bounded/cancellable jobs, no idle calls, active/map marker separation, live coordinate calibration, missing height, surface height and quest cancellation')
