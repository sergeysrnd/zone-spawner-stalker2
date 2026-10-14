using System;
class SetupEntry {
 [STAThread] static int Main(string[] args){return ZoneSpawner.SetupApplication.Run(AppDomain.CurrentDomain.BaseDirectory,args);}
}
