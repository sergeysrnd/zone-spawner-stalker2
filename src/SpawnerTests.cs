using System;
using System.Diagnostics;
using System.Threading;
class Tests {
 static Process Child(){return Process.Start(new ProcessStartInfo(typeof(Tests).Assembly.Location,"--lifetime-child"){UseShellExecute=false,CreateNoWindow=true,WindowStyle=ProcessWindowStyle.Hidden});}
 [STAThread] static int Main(string[] args) {
  try {
   if(args.Length==1 && args[0]=="--lifetime-child"){Thread.Sleep(350);return 0;}
   if(args.Length>1 && args[1]=="--hotkey"){ZoneSpawner.SpawnForm.StartupSelfTest(args[0]);return 0;}
   ZoneSpawner.Domain.SelfTest(args[0]);
   ZoneSpawner.SpawnForm.ReviewSelfTest(args[0]);
   foreach(bool alreadyExited in new[]{false,true}){
    var child=Child();if(alreadyExited && !child.WaitForExit(5000))throw new Exception("Test child did not exit");
    using(var signal=new ManualResetEvent(false)){
     int notifications=0;
     using(var lifetime=new ZoneSpawner.GameLifetime(child,()=>{Interlocked.Increment(ref notifications);signal.Set();})){
      if(!signal.WaitOne(5000) || notifications!=1)throw new Exception("Exit event was missing or duplicated");
     }
    }
    child=Child();if(alreadyExited && !child.WaitForExit(5000))throw new Exception("Test child did not exit");
    ZoneSpawner.SpawnForm.LifetimeSelfTest(args[0],child);
   }
   return 0;
  } catch(Exception error) {
   Console.Error.WriteLine(error);
   return 1;
  }
 }
}
