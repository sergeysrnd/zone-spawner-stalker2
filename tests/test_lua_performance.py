import pathlib, sys, time, tempfile, uuid

from lupa.lua54 import LuaRuntime

scripts = (pathlib.Path(__file__).resolve().parents[1]/'mod/Scripts').resolve()
lua = LuaRuntime(unpack_returned_tuples=True)
now = int(time.time())
with tempfile.TemporaryDirectory(prefix='zone-spawner-perf-') as temporary:
    bridge = pathlib.Path(temporary)
    catalog = bridge/'catalog.tsv'
    catalog.write_text('Medkit\t0\nFake\t1\n')
    lua.globals().test_bridge = bridge.as_posix()
    lua.globals().test_catalog = catalog.as_posix()
    lua.globals().clock = now
    lua.execute('package.path = '+repr(scripts.as_posix()+'/?.lua;')+' .. package.path')
    lua.execute('''
    package.preload.config=function()return {bridge_dir=test_bridge,catalog_path=test_catalog,launcher_path='mock-launcher.dll'}end
    launches=0
    package.preload.launcher=function()return {start=function()launches=launches+1;return true end}end
    os.time=function()return clock end
    callbacks=0; engine_scans=0; static_scans=0; player_checks=0
    in_game=false; defer=false; fail_dispatch=false; fail_command=false
    calls={}; ready=true
    local function valid(self)assert(in_game,'UObject touched off the game thread');return self.alive end
    pc={alive=true,IsValid=valid,IsLocalPlayerController=valid,Pawn={alive=true,IsValid=valid}}
    engine={alive=true,IsValid=valid,GameViewport={alive=true,IsValid=valid}}
    gameplay={alive=true,IsValid=valid,GetPlayerController=function()
      assert(in_game);player_checks=player_checks+1;return ready and pc or nil
    end}
    kismet={alive=true,IsValid=valid,ExecuteConsoleCommand=function(self,world,command,controller)
      assert(in_game and world==pc and controller==pc)
      if fail_command then error('mock command failure')end
      table.insert(calls,command)
    end}
    function FindFirstOf(name)assert(in_game and name=='Engine');engine_scans=engine_scans+1;return engine end
    function StaticFindObject(name)
      assert(in_game);static_scans=static_scans+1
      if name=='/Script/Engine.Default__GameplayStatics'then return gameplay end
      if name=='/Script/Engine.Default__KismetSystemLibrary'then return kismet end
      error('Unexpected lookup')
    end
    function LoopAsync(delay,callback)assert(delay==500);tick=callback end
    function run_game(callback)in_game=true;callback();in_game=false end
    function ExecuteInGameThread(callback)
      callbacks=callbacks+1
      if fail_dispatch then error('mock scheduling failure')end
      if defer then delayed=callback else run_game(callback)end
    end
    ''')
    lua.execute((scripts/'main.lua').read_text(encoding='utf8'))
    session = (bridge/'status.txt').read_text().split('\t')[0]
    def tick():
        assert lua.globals().tick() is False, 'Polling loop must remain alive'
    def request(item='Medkit', qty=1, stamp=None, token=None, advanced=0):
        text=f'{uuid.uuid4().hex}\t{token or session}\t{lua.globals().clock if stamp is None else stamp}\t{item}\t{qty}\t{advanced}\n'
        (bridge/'request.txt').write_text(text)
        tick()
        return text
    def probe(stamp=None, text=None):
        (bridge/'probe.txt').write_text(text or f'{uuid.uuid4().hex}\t{lua.globals().clock if stamp is None else stamp}\n')
        tick()
    def ack(): return (bridge/'response.txt').read_text()

    for _ in range(120): tick()
    assert lua.globals().launches == 1, 'Menu launcher must run exactly once, not in the polling loop'
    assert lua.globals().callbacks == 0
    assert lua.globals().engine_scans == lua.globals().static_scans == 0
    for text in ['bad', f'{uuid.uuid4().hex}\t{now-6}\n', f'{uuid.uuid4().hex}\t{now+3}\n']:
        probe(text=text)
    for kwargs in [{'qty':0}, {'qty':1000}, {'item':'Missing'}, {'item':'Fake'}, {'token':'old'}, {'stamp':now-11}, {'item':'Medkit;quit'}]:
        request(**kwargs)
        assert '\trejected\t' in ack()
    assert lua.globals().callbacks == 0, 'Invalid requests or probes scheduled game work'

    probe()
    assert '\tready\t' in (bridge/'status.txt').read_text()
    request(qty=1); request(qty=2)
    assert list(lua.globals().calls.values()) == ['XCreateItemInInventoryByID Medkit 0 1 1','XCreateItemInInventoryByID Medkit 0 2 1']
    for _ in range(20): probe()
    assert lua.globals().engine_scans == 1 and lua.globals().static_scans == 2
    assert lua.globals().player_checks == 23, 'Controller must be obtained afresh'
    callbacks=lua.globals().callbacks
    for _ in range(120): tick()
    assert lua.globals().callbacks == callbacks, 'Idle loop touched the game'

    lua.globals().ready=False
    request()
    assert '\trejected\tload_a_save' in ack()
    assert '\twaiting\t' in (bridge/'status.txt').read_text()
    lua.globals().ready=True
    # Cached objects that become invalid must be reacquired on the game thread.
    lua.execute('engine.alive=false;engine={alive=true,IsValid=engine.IsValid,GameViewport=engine.GameViewport}')
    probe()
    assert lua.globals().engine_scans == 2
    lua.execute('gameplay.alive=false;gameplay={alive=true,IsValid=gameplay.IsValid,GetPlayerController=gameplay.GetPlayerController}')
    probe()
    assert lua.globals().static_scans == 3
    lua.execute('kismet.alive=false;kismet={alive=true,IsValid=kismet.IsValid,ExecuteConsoleCommand=kismet.ExecuteConsoleCommand}')
    repeated=request(qty=5)
    assert lua.globals().static_scans == 4
    count=len(lua.globals().calls)
    callbacks=lua.globals().callbacks
    (bridge/'request.txt').write_text(repeated);tick()
    assert '\trejected\talready_processed' in ack()
    assert lua.globals().callbacks == callbacks and len(lua.globals().calls) == count

    lua.globals().defer=True
    request()
    for _ in range(10): tick()
    assert lua.globals().callbacks == callbacks+1, 'Queued work must not accumulate'
    lua.globals().clock=now+11
    lua.globals().run_game(lua.globals().delayed)
    assert '\trejected\texpired' in ack() and len(lua.globals().calls) == count
    lua.globals().defer=False
    lua.globals().fail_dispatch=True
    request()
    assert '\terror\t' in ack()
    lua.globals().fail_dispatch=False
    request()
    assert '\tsubmitted\t' in ack() and len(lua.globals().calls) == count+1
    lua.globals().fail_command=True
    request()
    assert '\terror\t' in ack() and len(lua.globals().calls) == count+1

print('PASS: 120 idle ticks -> zero game callbacks/scans; visible probes; cache reuse/invalidation; fresh controller; invalid requests; 1/2 quantity; replay; delayed expiry; recovery from scheduling/engine errors; all UObject calls on game thread')
