using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using System.Web.Script.Serialization;
using Microsoft.Win32;

namespace ZoneSpawner {
 public sealed class SteamGame {
  public string Root,Build;
  public override string ToString(){return "Steam · "+(String.IsNullOrEmpty(Build)?"сборка неизвестна":"build "+Build)+" · "+Root;}
 }
 internal static class SteamDiscovery {
  static string[] Tokens(string text){return Regex.Matches(text,@"(?s)//[^\r\n]*|""(?:\\.|[^""\\])*""|[{}]|[^\s{}"" ]+").Cast<Match>().Select(m=>m.Value).Where(s=>!s.StartsWith("//")).ToArray();}
  static string Value(string token){if(!token.StartsWith("\""))return token;return Regex.Replace(token.Substring(1,token.Length-2),@"\\([\\""])","$1");}
  static Dictionary<string,object> ReadNode(string[] t,ref int n,bool nested,int depth){
   if(depth>32)throw new FormatException("VDF nesting");var result=new Dictionary<string,object>(StringComparer.OrdinalIgnoreCase);
   while(n<t.Length){if(t[n]=="}"){if(!nested)throw new FormatException("VDF closing brace");n++;return result;}string key=Value(t[n++]);if(key=="{"||n>=t.Length)throw new FormatException("VDF missing value");
    if(t[n]=="{"){n++;result[key]=ReadNode(t,ref n,true,depth+1);}else if(t[n]=="}")throw new FormatException("VDF missing value");else result[key]=Value(t[n++]);
   }if(nested)throw new FormatException("VDF missing closing brace");return result;
  }
  internal static Dictionary<string,object> Parse(string text){int n=0;return ReadNode(Tokens(text),ref n,false,0);}
  static string Text(Dictionary<string,object> d,string key){object v;return d.TryGetValue(key,out v)?v as string:null;}
  static Dictionary<string,object> Child(Dictionary<string,object>d,string key){object v;return d.TryGetValue(key,out v)?v as Dictionary<string,object>:null;}
  static void Add(ISet<string> roots,string path){if(String.IsNullOrEmpty(path))return;try{roots.Add(Path.GetFullPath(Environment.ExpandEnvironmentVariables(path)));}catch(ArgumentException){}catch(NotSupportedException){}}
  internal static List<SteamGame> Find(IEnumerable<string> steamRoots){
   var libraries=new HashSet<string>(StringComparer.OrdinalIgnoreCase);foreach(string root in steamRoots){Add(libraries,root);try{string file=Path.Combine(root,"steamapps","libraryfolders.vdf");if(!File.Exists(file)||new FileInfo(file).Length>1024*1024)continue;var parsed=Child(Parse(File.ReadAllText(file)),"libraryfolders");if(parsed==null)continue;
    foreach(var row in parsed){int index;if(!Int32.TryParse(row.Key,out index))continue;var node=row.Value as Dictionary<string,object>;Add(libraries,node!=null?Text(node,"path"):row.Value as string);}
   }catch(IOException){}catch(UnauthorizedAccessException){}catch(FormatException){}}
   var games=new List<SteamGame>();foreach(string library in libraries){try{string file=Path.Combine(library,"steamapps","appmanifest_1643320.acf");if(!File.Exists(file)||new FileInfo(file).Length>1024*1024)continue;var app=Child(Parse(File.ReadAllText(file)),"AppState");if(app==null||Text(app,"appid")!="1643320")continue;string dir=Text(app,"installdir");if(String.IsNullOrEmpty(dir)||Path.IsPathRooted(dir)||dir=="."||dir==".."||dir.IndexOfAny(new[]{'\\','/'})>=0)continue;
    string game=Path.Combine(library,"steamapps","common",dir);if(File.Exists(Path.Combine(game,@"Stalker2\Binaries\Win64\Stalker2-Win64-Shipping.exe")))games.Add(new SteamGame{Root=Path.GetFullPath(game),Build=Text(app,"buildid")});
   }catch(IOException){}catch(UnauthorizedAccessException){}catch(FormatException){}catch(ArgumentException){}}
   return games.GroupBy(g=>g.Root,StringComparer.OrdinalIgnoreCase).Select(g=>g.First()).ToList();
  }
  internal static List<SteamGame> Find(){
   var roots=new HashSet<string>(StringComparer.OrdinalIgnoreCase);
   foreach(RegistryHive hive in new[]{RegistryHive.CurrentUser,RegistryHive.LocalMachine})foreach(RegistryView view in new[]{RegistryView.Registry32,RegistryView.Registry64})try{
    using(var b=RegistryKey.OpenBaseKey(hive,view))using(var k=b.OpenSubKey(@"Software\Valve\Steam")){if(k==null)continue;Add(roots,k.GetValue("SteamPath") as string);Add(roots,k.GetValue("InstallPath")as string);string exe=k.GetValue("SteamExe")as string;if(!String.IsNullOrEmpty(exe))Add(roots,Path.GetDirectoryName(exe));}
   }catch(System.Security.SecurityException){}catch(UnauthorizedAccessException){}
   Add(roots,Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86),"Steam"));Add(roots,Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),"Steam"));return Find(roots);
  }
  internal static string Downloads(){try{using(var key=Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Explorer\User Shell Folders")){string value=key==null?null:key.GetValue("{374DE290-123F-4565-9164-39C4925E467B}")as string;if(!String.IsNullOrEmpty(value))return Environment.ExpandEnvironmentVariables(value);}}catch(System.Security.SecurityException){}return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),"Downloads");}
 }
 internal sealed class SetupProfile {
  internal const string BaseUrl="https://github.com/UE4SS-RE/RE-UE4SS/releases/download/experimental/UE4SS_v3.0.1-1028-gd7e7826d.zip";
  internal const string NexusUrl="https://www.nexusmods.com/stalker2heartofchornobyl/mods/2341?tab=files";
  internal const string PinnedBase="342D893C3F64CB36B88AC4D58CEFC1DD8571B9E37B03F86B793F63955CCC2C0B",PinnedFix="DC46D795F1DD134FE3E760BAE196ECFEA495BBD7D53A657A2CA5BA0D5CBA405A";
  internal string Package,GameHash,BaseHash=PinnedBase,FixHash=PinnedFix;
  internal Func<bool> GameRunning=()=>Process.GetProcessesByName("Stalker2-Win64-Shipping").Length!=0;
  internal static SetupProfile Load(string root){var meta=new JavaScriptSerializer().Deserialize<Dictionary<string,object>>(File.ReadAllText(Path.Combine(root,"catalog-info.json")));return new SetupProfile{Package=Path.GetFullPath(root),GameHash=(string)meta["gameExeSHA256"]};}
 }
 internal static class SetupCore {
  internal static readonly Dictionary<string,string> Settings=new Dictionary<string,string>{
   {"bUseUObjectArrayCache","false"},{"DefaultFNameToStringMethod","Scan"},{"DefaultExecuteInGameThreadMethod","ProcessEvent"},
   {"HookProcessLocalScriptFunction","0"},{"HookInitGameState","0"},{"HookLoadMap","0"},{"HookCallFunctionByNameWithArguments","0"},{"HookBeginPlay","0"},{"HookLocalPlayerExec","0"},{"HookAActorTick","0"},{"HookEngineTick","0"},{"HookGameViewportClientTick","0"},
   {"HookProcessInternal","1"},{"HookEndPlay","1"},{"HookUObjectProcessEvent","1"},{"HookProcessConsoleExec","1"},{"HookUStructLink","1"},
   {"ConsoleEnabled","0"},{"GuiConsoleEnabled","0"},{"GuiConsoleVisible","0"},{"EnableHotReloadSystem","0"},{"EnableAutoReloadingLuaMods","0"}
  };
  static readonly string[] Required={"ZoneSpawner.exe","ZoneSpawnerLauncher.dll","items.json","actors.json","locations.json","locations-info.json","catalog-bridge.tsv","actors-bridge.tsv","inventory-ids.txt","mod/Scripts/main.lua","mod/Scripts/protocol.lua","mod/Scripts/launcher.lua","mod/Scripts/inventory.lua","mod/Scripts/inventory-reader.lua","mod/Scripts/inventory-job.lua","mod/Scripts/world.lua","mod/Scripts/quest-job.lua"};
  internal static string Hash(string file){using(var stream=File.OpenRead(file))using(var sha=SHA256.Create())return BitConverter.ToString(sha.ComputeHash(stream)).Replace("-","");}
  internal static bool Matches(string file,string hash){return File.Exists(file)&&String.Equals(Hash(file),hash,StringComparison.OrdinalIgnoreCase);}
  internal static string Inside(string root,string relative){string full=Path.GetFullPath(Path.Combine(root,relative.Replace('/',Path.DirectorySeparatorChar)));string prefix=Path.GetFullPath(root).TrimEnd('\\','/')+Path.DirectorySeparatorChar;if(!full.StartsWith(prefix,StringComparison.OrdinalIgnoreCase)||relative.IndexOf(':')>=0)throw new IOException("Некорректный путь файла в пакете.");return full;}
  internal static string LuaString(string text){string equals="=";while(text.Contains("]"+equals+"]"))equals+="=";return "["+equals+"["+text.Replace('\\','/')+"]"+equals+"]";}
  internal static string Binary(string game){return Path.Combine(Path.GetFullPath(game),@"Stalker2\Binaries\Win64");}
  internal static void VerifyGame(SetupProfile p,string game,bool requireClean){
   if(p.GameRunning())throw new IOException("Закрой S.T.A.L.K.E.R. 2 перед установкой.");string binary=Binary(game),exe=Path.Combine(binary,"Stalker2-Win64-Shipping.exe");
   if(!File.Exists(exe))throw new IOException("В выбранной папке не найден исполняемый файл S.T.A.L.K.E.R. 2.");
   if(!Matches(exe,p.GameHash))throw new IOException("Сборка игры отличается от проверенной Steam build 25382007. Нужна новая проверка совместимости.");
   if(requireClean&&(File.Exists(Path.Combine(binary,"dwmapi.dll"))||Directory.Exists(Path.Combine(binary,"ue4ss"))))throw new IOException("UE4SS или другой загрузчик уже установлен. Удали прежний Zone Spawner его Uninstall.ps1; при других модах нужна ручная проверка.");
  }
  internal static string Cache{get{return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"ZoneSpawner","Downloads");}}
  internal static string FindArchive(string hash,IEnumerable<string> folders){
   foreach(string folder in folders.Distinct(StringComparer.OrdinalIgnoreCase)){try{if(!Directory.Exists(folder))continue;
    foreach(var file in new DirectoryInfo(folder).GetFiles("*.zip",SearchOption.TopDirectoryOnly).Where(f=>f.Name.IndexOf("UE4SS",StringComparison.OrdinalIgnoreCase)>=0&&f.Length>0&&f.Length<80*1024*1024).OrderByDescending(f=>f.LastWriteTimeUtc).Take(25))try{if(Matches(file.FullName,hash))return file.FullName;}catch(IOException){}
   }catch(IOException){}catch(UnauthorizedAccessException){}}return null;
  }
  internal static async Task<string> DownloadBase(string cache,Action<string> report,CancellationToken cancel){
   Directory.CreateDirectory(cache);string target=Path.Combine(cache,"UE4SS_v3.0.1-1028-gd7e7826d.zip");if(Matches(target,SetupProfile.PinnedBase))return target;
   string temporary=target+"."+Guid.NewGuid().ToString("N")+".part";ServicePointManager.SecurityProtocol|=SecurityProtocolType.Tls12;
   try{using(var client=new HttpClient()){client.Timeout=TimeSpan.FromMinutes(3);client.DefaultRequestHeaders.UserAgent.ParseAdd("ZoneSpawner-Setup/0.4.1");using(var response=await client.GetAsync(SetupProfile.BaseUrl,HttpCompletionOption.ResponseHeadersRead,cancel)){response.EnsureSuccessStatusCode();long? total=response.Content.Headers.ContentLength;long got=0;int last=-1;
    using(var input=await response.Content.ReadAsStreamAsync())using(var output=new FileStream(temporary,FileMode.CreateNew,FileAccess.Write,FileShare.None)){byte[] buffer=new byte[65536];int count;while((count=await input.ReadAsync(buffer,0,buffer.Length,cancel))>0){got+=count;if(got>80*1024*1024)throw new IOException("Архив больше ожидаемого размера.");await output.WriteAsync(buffer,0,count,cancel);int percent=total.HasValue&&total.Value>0?(int)(got*100/total.Value):-1;if(percent!=last){report(percent<0?"Скачивается UE4SS…":"Скачивается UE4SS: "+percent+"%");last=percent;}}}
   }}cancel.ThrowIfCancellationRequested();if(!Matches(temporary,SetupProfile.PinnedBase))throw new IOException("Хеш скачанного UE4SS не совпал с закреплённым build 1028. Установка остановлена.");if(File.Exists(target))File.Delete(target);File.Move(temporary,target);return target;
   }finally{if(File.Exists(temporary))File.Delete(temporary);}
  }
  internal static void Extract(string archive,string destination){Directory.CreateDirectory(destination);using(var zip=ZipFile.OpenRead(archive)){foreach(var entry in zip.Entries){string target=Inside(destination,entry.FullName);if(entry.FullName.EndsWith("/")||entry.FullName.EndsWith("\\")){Directory.CreateDirectory(target);continue;}Directory.CreateDirectory(Path.GetDirectoryName(target));entry.ExtractToFile(target,false);}}}
  static void CopyTree(string source,string destination,bool overwrite=false){Directory.CreateDirectory(destination);foreach(string file in Directory.GetFiles(source,"*",SearchOption.AllDirectories)){if((File.GetAttributes(file)&FileAttributes.ReparsePoint)!=0)throw new IOException("Ссылки на внешние файлы в пакете не поддерживаются.");string relative=file.Substring(source.TrimEnd('\\','/').Length+1);string target=Inside(destination,relative);Directory.CreateDirectory(Path.GetDirectoryName(target));File.Copy(file,target,overwrite);}}
  static void VerifyManifest(string root,string file){foreach(string line in File.ReadAllLines(file)){var m=Regex.Match(line,@"^([a-fA-F0-9]{64})\s+\*?(.+)$");if(!m.Success)throw new IOException("Некорректный список контрольных сумм.");if(!Matches(Inside(root,m.Groups[2].Value),m.Groups[1].Value))throw new IOException("Не совпала контрольная сумма: "+m.Groups[2].Value);}}
  static void VerifyPackage(SetupProfile p){foreach(string name in Required)if(!File.Exists(Inside(p.Package,name)))throw new IOException("Пакет распакован не полностью: "+name);if(p.Package.Length>210)throw new IOException("Распакуй пакет в папку с более коротким путём, например D:\\Mods\\ZoneSpawner.");string probe=Inside(p.Package,".setup-write-"+Guid.NewGuid().ToString("N"));File.WriteAllText(probe,"");File.Delete(probe);}
  internal static void Install(SetupProfile p,string game,string baseZip,string fixZip,Action<string> report,CancellationToken cancel){
   VerifyPackage(p);VerifyGame(p,game,true);if(!Matches(baseZip,p.BaseHash))throw new IOException("Выбранный ZIP UE4SS не совпадает с build 1028.");if(!Matches(fixZip,p.FixHash))throw new IOException("Выбранный ZIP фикса не совпадает с версией 1.2.");
   string scratch=Path.Combine(Path.GetTempPath(),"ZoneSpawner-setup-"+Guid.NewGuid().ToString("N")),binary=Binary(game),deployment;
   do{deployment=Inside(binary,".zs-install-"+Guid.NewGuid().ToString("N").Substring(0,12));}while(Directory.Exists(deployment)||File.Exists(deployment));bool loaderMoved=false,proxyMoved=false,complete=false;
   try{cancel.ThrowIfCancellationRequested();report("Готовлю UE4SS и фикс 1.2…");string original=Inside(scratch,"base"),fix=Inside(scratch,"fix"),payload=Inside(scratch,"payload");Extract(baseZip,original);Extract(fixZip,fix);VerifyManifest(fix,Inside(fix,"SHA256SUMS.txt"));Directory.CreateDirectory(payload);CopyTree(Inside(original,"ue4ss"),Inside(payload,"ue4ss"));File.Copy(Inside(original,"dwmapi.dll"),Inside(payload,"dwmapi.dll"));
    File.Copy(Inside(fix,"ue4ss/UE4SS.dll"),Inside(payload,"ue4ss/UE4SS.dll"),true);
    string signatures=Inside(payload,"ue4ss/UE4SS_Signatures");CopyTree(Inside(fix,"ue4ss/UE4SS_Signatures"),signatures,true);
    string notices=Inside(payload,"ue4ss/ZoneSpawnerNotices");Directory.CreateDirectory(notices);CopyTree(Inside(fix,"licenses"),Inside(notices,"licenses"));foreach(string name in new[]{"SOURCE_AND_BUILD_INFO.txt","LICENSES_AND_ATTRIBUTION.txt","SETTINGS_REQUIRED.txt"})File.Copy(Inside(fix,name),Inside(notices,name));
    string ini=Inside(payload,"ue4ss/UE4SS-settings.ini"),text=File.ReadAllText(ini);foreach(var value in Settings){string regex="(?m)^"+Regex.Escape(value.Key)+@"\s*=.*$";if(Regex.Matches(text,regex).Count!=1)throw new IOException("Неожиданная настройка UE4SS: "+value.Key);text=Regex.Replace(text,regex,value.Key+" = "+value.Value);}File.WriteAllText(ini,text,new UTF8Encoding(false));
    string mods=Inside(payload,"ue4ss/Mods"),scripts=Inside(mods,"ZoneSpawner/Scripts");CopyTree(Path.Combine(p.Package,"mod"),Inside(mods,"ZoneSpawner"));File.WriteAllText(Inside(mods,"ZoneSpawner/enabled.txt"),"");File.WriteAllText(Inside(mods,"mods.txt"),"ZoneSpawner : 1\n");File.WriteAllText(Inside(mods,"mods.json"),"[{\"mod_name\":\"ZoneSpawner\",\"mod_enabled\":true}]");
    Directory.CreateDirectory(Path.Combine(p.Package,"bridge"));string config="return {bridge_dir="+LuaString(Path.Combine(p.Package,"bridge"))+",catalog_path="+LuaString(Path.Combine(p.Package,"catalog-bridge.tsv"))+",launcher_path="+LuaString(Path.Combine(p.Package,"ZoneSpawnerLauncher.dll"))+",reader_path="+LuaString(Path.Combine(binary,@"ue4ss\Mods\ZoneSpawner\Scripts\inventory-reader.lua"))+",world_path="+LuaString(Path.Combine(binary,@"ue4ss\Mods\ZoneSpawner\Scripts\world.lua"))+",actors_path="+LuaString(Path.Combine(p.Package,"actors-bridge.tsv"))+"}";File.WriteAllText(Inside(scripts,"config.lua"),config,new UTF8Encoding(false));
    var records=Directory.GetFiles(payload,"*",SearchOption.AllDirectories).Select(f=>new Dictionary<string,string>{{"Path",f.Substring(payload.Length+1)},{"SHA256",Hash(f)}}).ToArray();
    cancel.ThrowIfCancellationRequested();VerifyGame(p,game,true);report("Устанавливаю файлы и проверяю результат…");CopyTree(payload,deployment);cancel.ThrowIfCancellationRequested();VerifyGame(p,game,true);
    Directory.Move(Inside(deployment,"ue4ss"),Inside(binary,"ue4ss"));loaderMoved=true;File.Move(Inside(deployment,"dwmapi.dll"),Inside(binary,"dwmapi.dll"));proxyMoved=true;
    foreach(var row in records)if(!Matches(Inside(binary,row["Path"]),row["SHA256"]))throw new IOException("Не совпали контрольные суммы после копирования.");
    var manifest=new Dictionary<string,object>{{"GameRoot",Path.GetFullPath(game)},{"GameExeSHA256",p.GameHash},{"InstalledAt",DateTimeOffset.Now.ToString("o")},{"Files",records},{"BaseSHA256",p.BaseHash},{"FixSHA256",p.FixHash}};
    Domain.Atomic(Path.Combine(p.Package,"installed-manifest.json"),new JavaScriptSerializer().Serialize(manifest));complete=true;report("Готово. Запусти игру через Steam, загрузи сохранение и нажми Ctrl + Alt + S.");
   }catch{if(proxyMoved&&File.Exists(Inside(binary,"dwmapi.dll")))File.Move(Inside(binary,"dwmapi.dll"),Inside(deployment,"dwmapi.dll"));if(loaderMoved&&Directory.Exists(Inside(binary,"ue4ss")))Directory.Move(Inside(binary,"ue4ss"),Inside(deployment,"ue4ss"));throw;}
   finally{string resolved=Path.GetFullPath(scratch),temp=Path.GetFullPath(Path.GetTempPath()).TrimEnd('\\','/')+Path.DirectorySeparatorChar;try{if(resolved.StartsWith(temp,StringComparison.OrdinalIgnoreCase)&&Path.GetFileName(resolved).StartsWith("ZoneSpawner-setup-",StringComparison.Ordinal)&&Directory.Exists(resolved))Directory.Delete(resolved,true);if(complete&&Directory.Exists(deployment))Directory.Delete(deployment,false);}catch(IOException){}catch(UnauthorizedAccessException){}}
  }
 }
}
