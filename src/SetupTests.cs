using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Web.Script.Serialization;

namespace ZoneSpawner {
 internal static class SetupTests {
  static void Check(bool yes,string reason){if(!yes)throw new Exception(reason);}
  static void Rejected(Action action,string message){try{action();}catch(IOException e){Check(e.Message.Contains(message),"Wrong setup failure: "+e.Message);return;}throw new Exception("Setup accepted "+message);}
  static string Sum(byte[] bytes){using(var sha=SHA256.Create())return BitConverter.ToString(sha.ComputeHash(bytes)).Replace("-","");}
  static void Write(string file,string text){Directory.CreateDirectory(Path.GetDirectoryName(file));File.WriteAllText(file,text,new UTF8Encoding(false));}
  static string Quote(string s){return "\""+s.Replace("\\","\\\\").Replace("\"","\\\"")+"\"";}
  static void Zip(string file,Dictionary<string,string> entries,bool manifest){var bytes=entries.ToDictionary(p=>p.Key,p=>Encoding.UTF8.GetBytes(p.Value));if(manifest)bytes.Add("SHA256SUMS.txt",Encoding.UTF8.GetBytes(String.Join("\n",bytes.Select(e=>Sum(e.Value)+"  "+e.Key))+"\n"));using(var z=ZipFile.Open(file,ZipArchiveMode.Create))foreach(var e in bytes)using(var stream=z.CreateEntry(e.Key).Open())stream.Write(e.Value,0,e.Value.Length);}
  internal static void Run(string source){
   string suite=Path.Combine(Path.GetTempPath(),"ZoneSpawnerSetupTests-"+Guid.NewGuid().ToString("N").Substring(0,8));Directory.CreateDirectory(suite);
   try{string steam=Path.Combine(suite,"Steam base"),library=Path.Combine(suite,"Library Зона"),game=Path.Combine(library,@"steamapps\common\S.T.A.L.K.E.R. 2"),binary=SetupCore.Binary(game),exe=Path.Combine(binary,"Stalker2-Win64-Shipping.exe"),package=Path.Combine(suite,"package ]=]");Write(exe,"fake game executable for isolated setup tests");
    Write(Path.Combine(steam,@"steamapps\libraryfolders.vdf"),"\"libraryfolders\" { // comment\n \"0\" { \"path\" "+Quote(steam)+" } \"1\" { \"path\" "+Quote(library)+" \"apps\" { \"1643320\" \"0\" } } }");
    Write(Path.Combine(library,@"steamapps\appmanifest_1643320.acf"),"\"AppState\" { \"appid\" \"1643320\" \"installdir\" \"S.T.A.L.K.E.R. 2\" \"buildid\" \"25382007\" }");var found=SteamDiscovery.Find(new[]{steam});Check(found.Count==1&&found[0].Root==game&&found[0].Build=="25382007","Secondary Steam library detection");
    Write(Path.Combine(steam,@"steamapps\libraryfolders.vdf"),"\"LibraryFolders\" { \"1\" "+Quote(library)+" }");Check(SteamDiscovery.Find(new[]{steam}).Count==1,"Legacy Steam library detection");
    Write(Path.Combine(library,@"steamapps\appmanifest_1643320.acf"),"\"AppState\" { \"appid\" \"1643320\" \"installdir\" \"..\\escape\" }");Check(SteamDiscovery.Find(new[]{steam}).Count==0,"Manifest escaped common directory");
    Directory.CreateDirectory(package);foreach(string name in new[]{"ZoneSpawner.exe","ZoneSpawnerLauncher.dll","items.json","actors.json","catalog-bridge.tsv","actors-bridge.tsv","inventory-ids.txt"})Write(Path.Combine(package,name),"fixture "+name);
    foreach(string file in Directory.GetFiles(Path.Combine(source,@"mod\Scripts"),"*.lua"))Write(Path.Combine(package,@"mod\Scripts",Path.GetFileName(file)),File.ReadAllText(file));
    string baseZip=Path.Combine(suite,"UE4SS-base.zip"),fixZip=Path.Combine(suite,"UE4SS-fix.zip");var baseFiles=new Dictionary<string,string>{{"dwmapi.dll","base proxy"},{"ue4ss/UE4SS.dll","base loader"},{"ue4ss/LICENSE","upstream license"},{"ue4ss/UE4SS-settings.ini",String.Join("\n",SetupCore.Settings.Select(k=>k.Key+" = initial"))}};Zip(baseZip,baseFiles,false);
    var fixedFiles=new Dictionary<string,string>{{"ue4ss/UE4SS.dll","fixed loader"},{"ue4ss/UE4SS_Signatures/FName_ToString.lua","signature"},{"licenses/test-license.txt","fix license"},{"SOURCE_AND_BUILD_INFO.txt","build info"},{"LICENSES_AND_ATTRIBUTION.txt","notices"},{"SETTINGS_REQUIRED.txt","settings"}};Zip(fixZip,fixedFiles,true);
    var profile=new SetupProfile{Package=package,GameHash=SetupCore.Hash(exe),BaseHash=SetupCore.Hash(baseZip),FixHash=SetupCore.Hash(fixZip),GameRunning=()=>false};
    Check(SetupCore.FindArchive(profile.FixHash,new[]{suite})==fixZip,"Pinned fix discovery");string wrong=Path.Combine(suite,"UE4SS-wrong.zip");Write(wrong,"wrong archive");Rejected(()=>SetupCore.Install(profile,game,wrong,fixZip,s=>{},CancellationToken.None),"build 1028");Check(!Directory.Exists(Path.Combine(binary,"ue4ss")),"Bad hash modified game");
    profile.GameRunning=()=>true;Rejected(()=>SetupCore.Install(profile,game,baseZip,fixZip,s=>{},CancellationToken.None),"Закрой");profile.GameRunning=()=>false;
    string expected=profile.GameHash;profile.GameHash=new string('0',64);Rejected(()=>SetupCore.Install(profile,game,baseZip,fixZip,s=>{},CancellationToken.None),"Сборка");profile.GameHash=expected;
    Write(Path.Combine(binary,"dwmapi.dll"),"foreign proxy");Rejected(()=>SetupCore.Install(profile,game,baseZip,fixZip,s=>{},CancellationToken.None),"уже установлен");Check(File.ReadAllText(Path.Combine(binary,"dwmapi.dll"))=="foreign proxy","Foreign loader overwritten");File.Delete(Path.Combine(binary,"dwmapi.dll"));
    using(var cancelled=new CancellationTokenSource()){cancelled.Cancel();try{SetupCore.Install(profile,game,baseZip,fixZip,s=>{},cancelled.Token);throw new Exception("Cancellation ignored");}catch(OperationCanceledException){}Check(!Directory.Exists(Path.Combine(binary,"ue4ss")),"Cancelled setup modified game");}
    string badZip=Path.Combine(suite,"escape.zip");Zip(badZip,new Dictionary<string,string>{{"../escaped.txt","escape"}},false);Rejected(()=>SetupCore.Extract(badZip,Path.Combine(suite,"extract")),"Некорректный путь");Check(!File.Exists(Path.Combine(suite,"escaped.txt")),"Zip escaped extraction directory");
    Directory.CreateDirectory(Path.Combine(package,"installed-manifest.json"));bool rolledBack=false;try{SetupCore.Install(profile,game,baseZip,fixZip,s=>{},CancellationToken.None);}catch(IOException){rolledBack=true;}catch(UnauthorizedAccessException){rolledBack=true;}Check(rolledBack&&!Directory.Exists(Path.Combine(binary,"ue4ss"))&&!File.Exists(Path.Combine(binary,"dwmapi.dll")),"Manifest-write failure failed rollback");Check(Directory.GetDirectories(binary,".zs-install-*").Length==1,"Rollback recovery missing");Directory.Delete(Path.Combine(package,"installed-manifest.json"),false);
    SetupCore.Install(profile,game,baseZip,fixZip,s=>{},CancellationToken.None);Check(File.ReadAllText(Path.Combine(binary,@"ue4ss\UE4SS.dll"))=="fixed loader","Fix not applied");Check(File.Exists(Path.Combine(binary,@"ue4ss\ZoneSpawnerNotices\licenses\test-license.txt")),"Licenses missing");string ini=File.ReadAllText(Path.Combine(binary,@"ue4ss\UE4SS-settings.ini"));Check(ini.Contains("HookEngineTick = 0")&&ini.Contains("EnableAutoReloadingLuaMods = 0"),"Compatibility settings changed");
    string config=File.ReadAllText(Path.Combine(binary,@"ue4ss\Mods\ZoneSpawner\Scripts\config.lua"));Check(config.Contains("[==[")&&config.Contains("reader_path=")&&config.Contains("actors_path="),"Lua paths or delimiter escaping");var manifest=new JavaScriptSerializer().Deserialize<Dictionary<string,object>>(File.ReadAllText(Path.Combine(package,"installed-manifest.json")));Check((string)manifest["GameRoot"]==game&&SetupCore.Matches(exe,expected),"Game executable changed or manifest missing");Rejected(()=>SetupCore.Install(profile,game,baseZip,fixZip,s=>{},CancellationToken.None),"уже установлен");
    Console.WriteLine("PASS: Steam modern/legacy libraries, Unicode/spaces, invalid manifest paths, pinned archives, running/unsupported game rejection, foreign loader preservation, cancellation, ZIP traversal, rollback, fix/licenses/settings and Lua paths");
   }finally{string full=Path.GetFullPath(suite),prefix=Path.GetFullPath(Path.GetTempPath()).TrimEnd('\\','/')+Path.DirectorySeparatorChar;if(!full.StartsWith(prefix,StringComparison.OrdinalIgnoreCase)||!Path.GetFileName(full).StartsWith("ZoneSpawnerSetupTests-",StringComparison.Ordinal))throw new Exception("Unsafe setup-test cleanup");Directory.Delete(full,true);}
  }
 }
}
