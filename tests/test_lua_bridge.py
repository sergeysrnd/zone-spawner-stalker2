import pathlib, sys, time, tempfile, uuid

from lupa.lua54 import LuaRuntime

scripts=(pathlib.Path(__file__).resolve().parents[1]/'mod/Scripts').resolve()
lua=LuaRuntime(unpack_returned_tuples=True)
protocol=lua.execute((scripts/'protocol.lua').read_text(encoding='utf8'))
catalog=lua.table_from({'Medkit':False,'Fake':True})
now=int(time.time());session='test_session'
def line(item='Medkit',qty=5,stamp=now,token=session,advanced=0):
 return f'{uuid.uuid4().hex}\t{token}\t{stamp}\t{item}\t{qty}\t{advanced}\n'
valid=protocol.parse(line(),session,now,catalog)
assert valid['command']=='XCreateItemInInventoryByID Medkit 0 5 1'
for request,reason in [(line(token='old'),'wrong_session'),(line(stamp=now-11),'expired'),(line(stamp=now+3),'expired'),(line(qty=0),'invalid_quantity'),(line(qty=1000),'invalid_quantity'),(line(item='Missing'),'unknown_item'),(line(item='Fake'),'restricted_item'),(line(item='Medkit;quit'),'invalid_request')]:
 result=protocol.parse(request,session,now,catalog)
 assert result==(None,reason),(result,reason)
assert protocol.parse(line(item='Fake',advanced=1),session,now,catalog)['item']=='Fake'

with tempfile.TemporaryDirectory(prefix='zone-spawner-test-') as temporary:
 bridge=pathlib.Path(temporary)
 tsv=bridge/'catalog.tsv';tsv.write_text('Medkit\t0\nFake\t1\n')
 lua.execute('package.path = '+repr(scripts.as_posix()+'/'+'?.lua;')+' .. package.path')
 lua.globals().test_bridge=bridge.as_posix();lua.globals().test_catalog=tsv.as_posix()
 lua.execute('''
 package.preload.config=function()return {bridge_dir=test_bridge,catalog_path=test_catalog}end
 ready=false; calls={};fail_command=false
 local function valid(self)return true end
 pc={IsValid=valid,IsLocalPlayerController=valid,Pawn={IsValid=valid}}
 engine={IsValid=valid,GameViewport={IsValid=valid}}
 gameplay={IsValid=valid,GetPlayerController=function()return pc end}
 kismet={IsValid=valid,ExecuteConsoleCommand=function(self,world,command,controller)
   assert(world==pc and controller==pc)
   if fail_command then error('mock engine error')end
   table.insert(calls,command)
 end}
 function FindFirstOf(name)assert(name=='Engine');return ready and engine or nil end
 function StaticFindObject(name)
  if name=='/Script/Engine.Default__GameplayStatics'then return gameplay end
  if name=='/Script/Engine.Default__KismetSystemLibrary'then return kismet end
  error('Unexpected object '..name)
 end
 function LoopAsync(delay,callback)assert(delay==500);tick=callback end
 function ExecuteInGameThread(callback)callback()end
 ''')
 lua.execute((scripts/'main.lua').read_text(encoding='utf8'))
 lua.globals().tick()
 session,state,stamp=(bridge/'status.txt').read_text().strip().split('\t')
 assert state=='waiting'
 request=line(token=session)
 (bridge/'request.txt').write_text(request)
 lua.globals().tick()
 assert '\trejected\tload_a_save' in (bridge/'response.txt').read_text()
 assert len(lua.globals().calls)==0
 lua.globals().ready=True
 (bridge/'probe.txt').write_text(f'{uuid.uuid4().hex}\t{now}\n')
 lua.globals().tick()
 assert '\tready\t' in (bridge/'status.txt').read_text()
 request=line(token=session)
 (bridge/'request.txt').write_text(request);lua.globals().tick()
 assert '\tsubmitted\tMedkit' in (bridge/'response.txt').read_text()
 assert lua.globals().calls[1]=='XCreateItemInInventoryByID Medkit 0 5 1'
 assert not (bridge/'request.txt').exists()
 (bridge/'request.txt').write_text(request);lua.globals().tick()
 assert '\trejected\talready_processed' in (bridge/'response.txt').read_text()
 assert len(lua.globals().calls)==1
 lua.globals().fail_command=True
 (bridge/'request.txt').write_text(line(token=session));lua.globals().tick()
 assert '\terror\t' in (bridge/'response.txt').read_text()
 assert len(lua.globals().calls)==1
print('PASS: Lua protocol/session/expiry/whitelist/quantity, waiting and ready states, game-thread dispatch, duplicate suppression and engine errors')
