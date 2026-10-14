using System;
using System.Collections.Generic;
using System.Drawing;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Web.Script.Serialization;
using System.Windows.Forms;

namespace ZoneSpawner {
 public sealed class ActorEntry {
  public string Id {get;set;}public string Name {get;set;}public string Category {get;set;}public string Faction {get;set;}public string Note {get;set;}public bool Restricted {get;set;}public bool Quest {get;set;}
 }
 public sealed class InventoryEntry {public string Id;public int Count;public Item Item;public string Name {get{return Item==null?Id:Item.Name;}}}
 public sealed class ZonePoint {public string Name {get;set;}public double X {get;set;}public double Y {get;set;}public double Z {get;set;}}
 public sealed class QuestPoint {public string Id,Name;public bool Tracked;public double X,Y;public double? Z;}
 public static class FeatureData {
  public static bool Coordinate(double v){return !Double.IsNaN(v)&&!Double.IsInfinity(v)&&Math.Abs(v)<=10000000;}
  public static string Number(double v){if(!Coordinate(v))throw new ArgumentException("Некорректная координата");return v.ToString("0.###",CultureInfo.InvariantCulture);}
  public static string Teleport(ZonePoint p){if(p==null)throw new ArgumentNullException("p");return "teleport\t"+Number(p.X)+"\t"+Number(p.Y)+"\t"+Number(p.Z);}
  public static string ActorRequest(ActorEntry a,int qty,int distance,int rank,bool advanced){
   if(a==null || !Regex.IsMatch(a.Id??"",@"\A[A-Za-z0-9_]{1,160}\z"))throw new ArgumentException("Некорректный ID существа");
   if(a.Restricted&&!advanced)throw new ArgumentException("Сюжетный или служебный прототип скрыт");
   if(qty<1||qty>5||distance<5||distance>100||rank<1||rank>4)throw new ArgumentException("Параметры спауна вне диапазона");
   return "spawn_actor\t"+a.Id+"\t"+qty+"\t"+distance+"\t"+rank+"\t"+(advanced?"1":"0");
  }
  public static string[] Header(string text,string id,string session,long now){
   var h=(text??"").Split('\n')[0].TrimEnd('\r').Split('\t');long stamp;
   if(h.Length!=5||h[0]!=id||h[1]!=session||!Int64.TryParse(h[2],out stamp)||stamp>now+2||now-stamp>30)throw new FormatException("Устаревший ответ или другая игровая сессия");return h;
  }
  public static List<InventoryEntry> Inventory(string text,string id,string session,long now,List<Item> catalog){
   var h=Header(text,id,session,now);if(h[3]!="ready")throw new FormatException(h[4]);
   var byId=catalog.ToDictionary(i=>i.Id,StringComparer.Ordinal);var counts=new Dictionary<string,int>(StringComparer.Ordinal);
   foreach(var line in text.Split('\n').Skip(1).Where(s=>!String.IsNullOrWhiteSpace(s))){
    var p=line.TrimEnd('\r').Split('\t');int count;
    if(p.Length!=2||!Regex.IsMatch(p[0],@"\A[A-Za-z0-9_]{1,160}\z")||!Int32.TryParse(p[1],out count)||count<=0||count>100000000)throw new FormatException("Некорректный снимок рюкзака");
    int previous;counts.TryGetValue(p[0],out previous);counts[p[0]]=checked(previous+count);
   }
   return counts.Select(p=>{Item item;byId.TryGetValue(p.Key,out item);return new InventoryEntry{Id=p.Key,Count=p.Value,Item=item};}).OrderBy(x=>x.Name,StringComparer.Create(CultureInfo.GetCultureInfo("ru-RU"),true)).ToList();
  }
  public static ZonePoint Position(string text){
   var parts=text.Split('\n');if(parts.Length<2)throw new FormatException("Нет координат в ответе");var p=parts[1].Trim().Split('\t');double x,y,z;
   if(p.Length!=4||!Double.TryParse(p[0],NumberStyles.Float,CultureInfo.InvariantCulture,out x)||!Double.TryParse(p[1],NumberStyles.Float,CultureInfo.InvariantCulture,out y)||!Double.TryParse(p[2],NumberStyles.Float,CultureInfo.InvariantCulture,out z)||!Coordinate(x)||!Coordinate(y)||!Coordinate(z))throw new FormatException("Некорректная позиция игрока");
   return new ZonePoint{Name="Перед последним телепортом",X=x,Y=y,Z=z};
  }
  public static bool CanAdd(InventoryEntry e){return e!=null&&e.Item!=null&&!e.Item.Restricted;}
  public static bool Ammo(InventoryEntry e){return CanAdd(e)&&e.Item.Category=="Патроны";}
  public static List<QuestPoint> QuestPoints(string text){
   var result=new List<QuestPoint>();foreach(string line in text.Split('\n').Where(s=>s.StartsWith("Q\t",StringComparison.Ordinal))){var p=line.TrimEnd('\r').Split('\t');double x,y,z;
    if(p.Length!=7||!Regex.IsMatch(p[1],@"\A[0-9]{1,8}\z")||(p[2]!="tracked"&&p[2]!="map")||!Double.TryParse(p[3],NumberStyles.Float,CultureInfo.InvariantCulture,out x)||!Double.TryParse(p[4],NumberStyles.Float,CultureInfo.InvariantCulture,out y)||!Coordinate(x)||!Coordinate(y))throw new FormatException("Некорректная метка задания");
    double? height=null;if(p[5]!="?"){if(!Double.TryParse(p[5],NumberStyles.Float,CultureInfo.InvariantCulture,out z)||!Coordinate(z))throw new FormatException("Некорректная высота цели");height=z;}
    result.Add(new QuestPoint{Id=p[1],Name=p[6],Tracked=p[2]=="tracked",X=x,Y=y,Z=height});
   }return result.OrderByDescending(q=>q.Tracked).ToList();
  }
  public static void SelfTest(string root){
   var all=new JavaScriptSerializer().Deserialize<List<Item>>(File.ReadAllText(Path.Combine(root,"items.json"),Encoding.UTF8));
   string id=new string('a',32),session="session_1";long now=Domain.Now();string h=id+"\t"+session+"\t"+now+"\tready\tsnapshot\n";
   var rows=Inventory(h+"Medkit\t2\nMedkit\t3\nUnknownSID\t7\n",id,session,now,all);
   if(rows.Single(x=>x.Id=="Medkit").Count!=5||CanAdd(rows.Single(x=>x.Id=="UnknownSID")))throw new Exception("Inventory grouping or unknown-item actions");
   var ammo=new InventoryEntry{Item=all.First(i=>i.Category=="Патроны"&&!i.Restricted)};if(!Ammo(ammo)||Ammo(rows.Single(x=>x.Id=="Medkit")))throw new Exception("Ammo category isolation");
   foreach(string bad in new[]{h+"Medkit\t-1\n",h+"Medkit;quit\t1\n",h+"Medkit\t1.5\n",h.Replace(session,"old_session"),h.Replace(now.ToString(),(now-31).ToString())}){bool rejected=false;try{Inventory(bad,id,session,now,all);}catch{rejected=true;}if(!rejected)throw new Exception("Invalid inventory accepted");}
   if(Teleport(new ZonePoint{X=1.25,Y=-23,Z=10})!="teleport\t1.25\t-23\t10")throw new Exception("Teleport decimal formatting");
   foreach(double value in new[]{Double.NaN,Double.PositiveInfinity,10000001}){bool rejected=false;try{Number(value);}catch{rejected=true;}if(!rejected)throw new Exception("Invalid coordinate accepted");}
   var goals=QuestPoints("Q\t1\ttracked\t1.25\t2\t?\tЦель\nQ\t2\tmap\t3\t4\t88\tДругая метка\n");if(goals.Count!=2||!goals[0].Tracked||goals[0].Z.HasValue||goals[1].Z!=88)throw new Exception("Quest marker scope or missing height");
   var actors=new JavaScriptSerializer().Deserialize<List<ActorEntry>>(File.ReadAllText(Path.Combine(root,"actors.json"),Encoding.UTF8));
   if(actors.Select(a=>a.Id).Distinct().Count()!=actors.Count||!actors.Any(a=>a.Id=="Bloodsucker"&&!a.Restricted)||!actors.Any(a=>a.Id=="Richter"&&a.Quest&&a.Restricted))throw new Exception("Actor catalog or quest isolation");
   var actor=actors.First(a=>!a.Restricted);ActorRequest(actor,2,50,1,false);
   foreach(int quantity in new[]{0,6}){bool rejected=false;try{ActorRequest(actor,quantity,10,1,false);}catch{rejected=true;}if(!rejected)throw new Exception("Actor burst limit");}
   Console.WriteLine("PASS: inventory quantities, exact IDs, unknown/restricted actions, ammo scope, stale sessions, teleport numbers and actor catalog/burst limits");
  }
 }
 public sealed partial class SpawnForm {
  readonly TabControl mainTabs=new TabControl();readonly TabPage catalogPage=new TabPage("Каталог"),bagPage=new TabPage("Рюкзак"),teleportPage=new TabPage("Телепорт"),actorPage=new TabPage("Персонажи и мутанты"),journalPage=new TabPage("Журнал");
  readonly DataGridView bagGrid=new DataGridView(),actorGrid=new DataGridView();readonly TextBox bagSearch=new TextBox(),actorSearch=new TextBox(),pointName=new TextBox();
  readonly Label bagStatus=new Label(),worldStatus=new Label(),actorStatus=new Label(),actorInfo=new Label();
  readonly Button bagRefresh=new Button(),bagAdd=new Button(),readPosition=new Button(),savePoint=new Button(),teleportGo=new Button(),teleportBack=new Button(),actorSpawn=new Button();
  readonly NumericUpDown bagQuantity=new NumericUpDown(),actorQuantity=new NumericUpDown(),actorDistance=new NumericUpDown(),coordinateX=new NumericUpDown(),coordinateY=new NumericUpDown(),coordinateZ=new NumericUpDown();
  readonly CheckBox bagAmmoOnly=new CheckBox(),actorAdvanced=new CheckBox();readonly ComboBox actorCategory=new ComboBox(),actorFaction=new ComboBox(),actorRank=new ComboBox();
  readonly ListBox pointList=new ListBox(),journal=new ListBox();readonly List<Button> ammoButtons=new List<Button>();
  readonly TabControl teleportTargets=new TabControl();readonly TabPage savedTargets=new TabPage("Мои точки"),questTargets=new TabPage("Цели заданий");readonly DataGridView questGrid=new DataGridView();readonly Button questRefresh=new Button();readonly CheckBox showOtherQuestMarkers=new CheckBox();readonly Label questHint=new Label();List<QuestPoint> questPoints=new List<QuestPoint>();
  List<InventoryEntry> bagItems=new List<InventoryEntry>();List<ActorEntry> actors=new List<ActorEntry>();List<ZonePoint> points=new List<ZonePoint>();
  string inventoryPending="",inventoryJobId="",inventorySession="",worldPending="",worldJobId="",worldAction="",positionPurpose="";long inventorySentAt=0,worldSentAt=0,inventoryStamp=0,refreshAfter=0;bool wantInventory=false,spawnFromBag=false,coordinatesChosen=false,requiresManualHeight=false,cancelInventoryWhenAttached=false,cancelQuestWhenAttached=false;ZonePoint previousPosition;
  bool FeaturePending{get{return inventoryPending!=""||worldPending!="";}}
  bool Interactive{get{return Visible&&WindowState!=FormWindowState.Minimized;}}
  InventoryEntry BagSelected{get{return bagGrid.CurrentRow==null?null:bagGrid.CurrentRow.Tag as InventoryEntry;}}
  ActorEntry ActorSelected{get{return actorGrid.CurrentRow==null?null:actorGrid.CurrentRow.Tag as ActorEntry;}}
  TextBox FeatureSearch(){return mainTabs.SelectedTab==bagPage?bagSearch:mainTabs.SelectedTab==actorPage?actorSearch:mainTabs.SelectedTab==teleportPage?(teleportTargets.SelectedTab==locationTargets?locationSearch:pointName):search;}
  TableLayoutPanel FeatureLayout(TabPage page,int rows){page.BackColor=Bg;page.ForeColor=TextColor;var table=new TableLayoutPanel{Dock=DockStyle.Fill,Padding=new Padding(24),ColumnCount=1,RowCount=rows};page.Controls.Add(table);return table;}
  void FeatureLabel(Label label,string text){label.Text=text;label.Dock=DockStyle.Fill;label.ForeColor=TextColor;label.AutoEllipsis=true;}
  void FeatureText(TextBox box,string accessible){box.Width=320;box.BackColor=PanelColor;box.ForeColor=TextColor;box.AccessibleName=accessible;box.BorderStyle=BorderStyle.FixedSingle;}
  void FeatureNumber(NumericUpDown box,int min,int max,int value){box.Minimum=min;box.Maximum=max;box.Value=value;box.Width=90;box.BackColor=PanelColor;box.ForeColor=TextColor;}
  void FeatureButton(Button b,string text,int width){b.Text=text;Style(b);b.Width=width;b.Height=36;}
  FlowLayoutPanel Flow(){return new FlowLayoutPanel{Dock=DockStyle.Fill,WrapContents=true,AutoScroll=true};}
  Label Caption(string text){return new Label{Text=text,AutoSize=true,Padding=new Padding(0,8,10,0),ForeColor=Color.Silver};}
  void FeatureGrid(DataGridView view){
   view.Dock=DockStyle.Fill;view.BackgroundColor=PanelColor;view.BorderStyle=BorderStyle.None;view.ReadOnly=true;view.AllowUserToAddRows=false;view.AllowUserToDeleteRows=false;view.AllowUserToResizeRows=false;view.RowHeadersVisible=false;view.MultiSelect=false;view.SelectionMode=DataGridViewSelectionMode.FullRowSelect;view.AutoSizeColumnsMode=DataGridViewAutoSizeColumnsMode.Fill;view.RowTemplate.Height=32;view.EnableHeadersVisualStyles=false;
   view.DefaultCellStyle.BackColor=PanelColor;view.DefaultCellStyle.ForeColor=TextColor;view.DefaultCellStyle.SelectionBackColor=Color.FromArgb(60,76,50);view.DefaultCellStyle.SelectionForeColor=TextColor;view.ColumnHeadersDefaultCellStyle.BackColor=Bg;view.ColumnHeadersDefaultCellStyle.ForeColor=Accent;
  }
  void InitializeFeatures(){
   Controls.Remove(layout);mainTabs.Dock=DockStyle.Fill;mainTabs.Font=Font;catalogPage.BackColor=Bg;catalogPage.Controls.Add(layout);mainTabs.TabPages.AddRange(new[]{catalogPage,bagPage,teleportPage,actorPage,journalPage});Controls.Add(mainTabs);
   actors=new JavaScriptSerializer().Deserialize<List<ActorEntry>>(File.ReadAllText(Path.Combine(root,"actors.json"),Encoding.UTF8));
   BuildBag();BuildTeleport();BuildActors();var jt=FeatureLayout(journalPage,2);jt.RowStyles.Add(new RowStyle(SizeType.Absolute,45));jt.RowStyles.Add(new RowStyle(SizeType.Percent,100));jt.Controls.Add(Caption("Последние 100 действий этой сессии. Отправленная команда ещё не подтверждает результат в игре."),0,0);journal.Dock=DockStyle.Fill;journal.BackColor=PanelColor;journal.ForeColor=TextColor;journal.HorizontalScrollbar=true;jt.Controls.Add(journal,0,1);
   mainTabs.SelectedIndexChanged+=(s,e)=>{if(mainTabs.SelectedTab==bagPage){wantInventory=true;refreshAfter=Domain.Now();}else CancelInventory();if(mainTabs.SelectedTab!=teleportPage)CancelQuestRead();else ApplyTeleportTarget();FeatureControls();};
   VisibleChanged+=(s,e)=>{if(Interactive&&mainTabs.SelectedTab==bagPage){wantInventory=true;refreshAfter=Domain.Now();}else if(!Interactive){CancelInventory();CancelQuestRead();}};
  }
  void BuildBag(){
   var t=FeatureLayout(bagPage,5);foreach(int height in new[]{65,55})t.RowStyles.Add(new RowStyle(SizeType.Absolute,height));t.RowStyles.Add(new RowStyle(SizeType.Percent,100));t.RowStyles.Add(new RowStyle(SizeType.Absolute,70));t.RowStyles.Add(new RowStyle(SizeType.Absolute,70));
   FeatureLabel(bagStatus,"Загрузи сохранение и нажми «Обновить». Рюкзак читается только по запросу.");t.Controls.Add(bagStatus,0,0);
   var top=Flow();top.Controls.Add(Caption("Поиск"));FeatureText(bagSearch,"Поиск в рюкзаке");top.Controls.Add(bagSearch);bagAmmoOnly.Text="Только патроны";bagAmmoOnly.AutoSize=true;bagAmmoOnly.Padding=new Padding(12,6,10,0);top.Controls.Add(bagAmmoOnly);FeatureButton(bagRefresh,"Обновить",140);top.Controls.Add(bagRefresh);t.Controls.Add(top,0,1);
   FeatureGrid(bagGrid);bagGrid.Columns.Add("Name","Предмет");bagGrid.Columns.Add("Count","Количество");bagGrid.Columns.Add("Category","Категория");bagGrid.Columns.Add("ID","ID");bagGrid.Columns[0].FillWeight=160;bagGrid.Columns[1].FillWeight=55;t.Controls.Add(bagGrid,0,2);
   var actions=Flow();actions.Controls.Add(Caption("Добавить"));FeatureNumber(bagQuantity,1,999,1);bagQuantity.AccessibleName="Количество таких же предметов";actions.Controls.Add(bagQuantity);FeatureButton(bagAdd,"Добавить такие же",210);actions.Controls.Add(bagAdd);actions.Controls.Add(Caption("Патроны:"));foreach(int count in new[]{30,60,100}){int amount=count;var b=Button("+"+count);b.Size=new Size(70,36);b.Click+=(s,e)=>AddFromBag(amount);ammoButtons.Add(b);actions.Controls.Add(b);}t.Controls.Add(actions,0,3);
   t.Controls.Add(Caption("Создаётся новый предмет с тем же ID. Износ и улучшения оружия не копируются.\nСнимок не обновляется в фоне; после торговли нажми «Обновить»."),0,4);
   bagSearch.TextChanged+=(s,e)=>FilterBag();bagAmmoOnly.CheckedChanged+=(s,e)=>FilterBag();bagGrid.SelectionChanged+=(s,e)=>FeatureControls();bagRefresh.Click+=(s,e)=>{wantInventory=true;refreshAfter=Domain.Now();Pulse();};bagAdd.Click+=(s,e)=>AddFromBag((int)bagQuantity.Value);
  }
  void BuildTeleport(){
   var t=FeatureLayout(teleportPage,6);t.RowStyles.Add(new RowStyle(SizeType.Absolute,65));t.RowStyles.Add(new RowStyle(SizeType.Absolute,60));t.RowStyles.Add(new RowStyle(SizeType.Absolute,60));t.RowStyles.Add(new RowStyle(SizeType.Percent,100));t.RowStyles.Add(new RowStyle(SizeType.Absolute,60));t.RowStyles.Add(new RowStyle(SizeType.Absolute,70));
   FeatureLabel(worldStatus,"Сохраняй точки, стоя на земле. Координаты игры указаны в сантиметрах.");t.Controls.Add(worldStatus,0,0);
   var row=Flow();foreach(var pair in new[]{Tuple.Create("X",coordinateX),Tuple.Create("Y",coordinateY),Tuple.Create("Z",coordinateZ)}){row.Controls.Add(Caption(pair.Item1));var box=pair.Item2;box.Minimum=-10000000;box.Maximum=10000000;box.DecimalPlaces=3;box.Width=180;box.BackColor=PanelColor;box.ForeColor=TextColor;box.AccessibleName="Координата "+pair.Item1;row.Controls.Add(box);}FeatureButton(readPosition,"Моя позиция",170);row.Controls.Add(readPosition);t.Controls.Add(row,0,1);
   var save=Flow();save.Controls.Add(Caption("Название точки"));FeatureText(pointName,"Название сохранённой точки");pointName.Text="Моя точка";save.Controls.Add(pointName);FeatureButton(savePoint,"Сохранить мою позицию",250);save.Controls.Add(savePoint);t.Controls.Add(save,0,2);
   pointList.Dock=DockStyle.Fill;pointList.BackColor=PanelColor;pointList.ForeColor=TextColor;pointList.DisplayMember="Name";pointList.IntegralHeight=false;savedTargets.Controls.Add(pointList);teleportTargets.Dock=DockStyle.Fill;teleportTargets.TabPages.AddRange(new[]{locationTargets,savedTargets,questTargets});t.Controls.Add(teleportTargets,0,3);BuildQuestTargets();BuildLocationTargets();
   var action=Flow();FeatureButton(teleportGo,"Телепорт по координатам",270);FeatureButton(teleportBack,"Вернуться перед прыжком",270);action.Controls.Add(teleportGo);action.Controls.Add(teleportBack);var remove=Button("Удалить точку");remove.Size=new Size(160,36);action.Controls.Add(remove);t.Controls.Add(action,0,4);
   t.Controls.Add(Caption("Для неизвестной высоты Z возможны падение и попадание в геометрию. Сделай сохранение перед прыжком.\nКнопка возврата хранит исходную позицию текущей сессии."),0,5);
   foreach(var box in new[]{coordinateX,coordinateY})box.ValueChanged+=(s,e)=>{coordinatesChosen=!requiresManualHeight;FeatureControls();};
   coordinateZ.ValueChanged+=(s,e)=>{requiresManualHeight=false;coordinatesChosen=true;FeatureControls();};
   try{string path=Path.Combine(root,"teleport-points.json");if(File.Exists(path))points=new JavaScriptSerializer().Deserialize<List<ZonePoint>>(File.ReadAllText(path,Encoding.UTF8)).Where(p=>p!=null&&FeatureData.Coordinate(p.X)&&FeatureData.Coordinate(p.Y)&&FeatureData.Coordinate(p.Z)&&!String.IsNullOrWhiteSpace(p.Name)).ToList();}catch{worldStatus.Text="Не удалось прочитать сохранённые точки.";}RenderPoints();
   pointList.SelectedIndexChanged+=(s,e)=>{var p=pointList.SelectedItem as ZonePoint;if(p!=null)SetCoordinates(p);};readPosition.Click+=(s,e)=>RequestPosition("read");savePoint.Click+=(s,e)=>RequestPosition("save");teleportGo.Click+=(s,e)=>SendWorld(FeatureData.Teleport(CurrentPoint()),"teleport");teleportBack.Click+=(s,e)=>{if(previousPosition!=null)SendWorld(FeatureData.Teleport(previousPosition),"teleport");};remove.Click+=(s,e)=>{var p=pointList.SelectedItem as ZonePoint;if(p==null)return;points.Remove(p);SavePoints();RenderPoints();};
   teleportTargets.SelectedIndexChanged+=(s,e)=>{if(teleportTargets.SelectedTab!=questTargets)CancelQuestRead();ApplyTeleportTarget();};ApplyTeleportTarget();
  }
  void BuildActors(){
   var t=FeatureLayout(actorPage,6);t.RowStyles.Add(new RowStyle(SizeType.Absolute,65));t.RowStyles.Add(new RowStyle(SizeType.Absolute,55));t.RowStyles.Add(new RowStyle(SizeType.Absolute,45));t.RowStyles.Add(new RowStyle(SizeType.Percent,100));t.RowStyles.Add(new RowStyle(SizeType.Absolute,75));t.RowStyles.Add(new RowStyle(SizeType.Absolute,85));
   FeatureLabel(actorStatus,"Выбери существо. ИИ созданных NPC и мутантов расходует ресурсы игры.");t.Controls.Add(actorStatus,0,0);var filters=Flow();filters.Controls.Add(Caption("Поиск"));FeatureText(actorSearch,"Поиск персонажей и мутантов");filters.Controls.Add(actorSearch);
   foreach(var combo in new[]{actorCategory,actorFaction,actorRank}){combo.DropDownStyle=ComboBoxStyle.DropDownList;combo.Width=185;combo.BackColor=PanelColor;combo.ForeColor=TextColor;}
   actorCategory.Items.AddRange(new[]{"Все существа","Персонажи","Мутанты"});actorCategory.SelectedIndex=0;filters.Controls.Add(actorCategory);actorFaction.Items.Add("Любая группировка");actorFaction.Items.AddRange(actors.Select(a=>a.Faction).Distinct().OrderBy(f=>f).ToArray());actorFaction.SelectedIndex=0;filters.Controls.Add(actorFaction);t.Controls.Add(filters,0,1);
   actorAdvanced.Text="Показывать сюжетные и служебные прототипы";actorAdvanced.AutoSize=true;actorAdvanced.ForeColor=Color.FromArgb(243,157,130);t.Controls.Add(actorAdvanced,0,2);
   FeatureGrid(actorGrid);actorGrid.Columns.Add("Name","Имя / вид");actorGrid.Columns.Add("Category","Категория");actorGrid.Columns.Add("Faction","Группировка");actorGrid.Columns.Add("ID","ID");actorGrid.Columns[0].FillWeight=160;t.Controls.Add(actorGrid,0,3);
   var action=Flow();action.Controls.Add(Caption("Количество"));FeatureNumber(actorQuantity,1,5,1);action.Controls.Add(actorQuantity);action.Controls.Add(Caption("Расстояние, м"));FeatureNumber(actorDistance,5,100,10);action.Controls.Add(actorDistance);action.Controls.Add(Caption("Ранг NPC"));actorRank.Items.AddRange(new[]{"Новичок","Опытный","Ветеран","Мастер"});actorRank.SelectedIndex=0;action.Controls.Add(actorRank);FeatureButton(actorSpawn,"Создать передо мной",235);action.Controls.Add(actorSpawn);t.Controls.Add(action,0,4);FeatureLabel(actorInfo,"");t.Controls.Add(actorInfo,0,5);
   actorSearch.TextChanged+=(s,e)=>FilterActors();actorCategory.SelectedIndexChanged+=(s,e)=>FilterActors();actorFaction.SelectedIndexChanged+=(s,e)=>FilterActors();actorAdvanced.CheckedChanged+=(s,e)=>FilterActors();actorGrid.SelectionChanged+=(s,e)=>FeatureControls();actorSpawn.Click+=(s,e)=>{var a=ActorSelected;if(a==null)return;SendWorld(FeatureData.ActorRequest(a,(int)actorQuantity.Value,(int)actorDistance.Value,actorRank.SelectedIndex+1,actorAdvanced.Checked),"spawn_actor");};FilterActors();
  }
  void FilterActors(){string category=actorCategory.SelectedItem as string,faction=actorFaction.SelectedItem as string,q=Domain.Fold(actorSearch.Text);string current=ActorSelected==null?null:ActorSelected.Id;var filtered=actors.Where(a=>(actorAdvanced.Checked||!a.Restricted)&&(category=="Все существа"||a.Category==category)&&(faction=="Любая группировка"||a.Faction==faction)&&Domain.Fold(a.Name+" "+a.Id+" "+a.Faction).Contains(q));actorGrid.Rows.Clear();foreach(var a in filtered){int n=actorGrid.Rows.Add(a.Name,a.Category,a.Faction,a.Id);actorGrid.Rows[n].Tag=a;if(a.Restricted)actorGrid.Rows[n].DefaultCellStyle.ForeColor=Color.FromArgb(243,157,130);}SelectRow(actorGrid,current);FeatureControls();}
  void FilterBag(){string selected=BagSelected==null?null:BagSelected.Id;bagGrid.Rows.Clear();foreach(var item in bagItems.Where(e=>(!bagAmmoOnly.Checked||FeatureData.Ammo(e))&&(e.Item!=null?Domain.Matches(e.Item,bagSearch.Text):Domain.Fold(e.Id).Contains(Domain.Fold(bagSearch.Text))))){int n=bagGrid.Rows.Add(item.Name,item.Count,item.Item==null?"Нет в каталоге":item.Item.Category,item.Id);bagGrid.Rows[n].Tag=item;}SelectRow(bagGrid,selected);FeatureControls();}
  void SelectRow(DataGridView view,string id){if(view.Rows.Count==0)return;var row=view.Rows.Cast<DataGridViewRow>().FirstOrDefault(r=>{var b=r.Tag as InventoryEntry;var a=r.Tag as ActorEntry;return (b!=null?b.Id:a!=null?a.Id:null)==id;})??view.Rows[0];view.CurrentCell=row.Cells[0];}
  void FeatureControls(){
   bool available=ready&&pending==""&&worldPending==""&&inventoryPending=="";bool fresh=inventorySession==token&&inventoryStamp>0;var b=BagSelected;bagRefresh.Enabled=ready&&inventoryPending=="";bagAdd.Enabled=available&&fresh&&FeatureData.CanAdd(b);foreach(var button in ammoButtons)button.Enabled=available&&fresh&&FeatureData.Ammo(b);
   readPosition.Enabled=savePoint.Enabled=questRefresh.Enabled=available;teleportGo.Enabled=available&&coordinatesChosen&&(teleportTargets.SelectedTab!=locationTargets||LocationSelected!=null&&LocationSelected.Landing!=null);teleportBack.Enabled=available&&previousPosition!=null;actorSpawn.Enabled=available&&ActorSelected!=null;
   var a=ActorSelected;actorRank.Enabled=a!=null&&a.Category=="Персонажи";actorInfo.Text=a==null?"Ничего не найдено":a.Name+"\n"+a.Note+"\nВ помещении точка впереди может оказаться в стене: сократи расстояние или выйди на открытое место.";
  }
  void InvalidateFeatureSession(){CancelInventory();CancelQuestRead();bagItems.Clear();questPoints.Clear();RenderQuestPoints();inventorySession="";inventoryStamp=0;inventoryPending="";inventoryJobId="";worldPending="";worldJobId="";previousPosition=null;FilterBag();ApplyTeleportTarget();if(mainTabs.SelectedTab==bagPage){wantInventory=true;refreshAfter=Domain.Now();}}
  void FeaturePulse(){
   if(inventoryPending!=""){
    try{string file=Path.Combine(bridge,inventoryJobId==""?"inventory-response.txt":"inventory-result.txt");string expected=inventoryJobId==""?inventoryPending:inventoryJobId;if(File.Exists(file)){string raw=File.ReadAllText(file,Encoding.UTF8);if(raw.StartsWith(expected+"\t",StringComparison.Ordinal)){
     var header=FeatureData.Header(raw,expected,token,Domain.Now());
     if(header[3]=="pending"){if(!Regex.IsMatch(header[4],@"\A[a-f0-9]{32}\z"))throw new FormatException("Неверный ID чтения");inventoryJobId=header[4];if(cancelInventoryWhenAttached||!Interactive||mainTabs.SelectedTab!=bagPage)CancelInventory();else bagStatus.Text="Читаю рюкзак короткими порциями…";}
     else {bagItems=FeatureData.Inventory(raw,expected,token,Domain.Now(),catalog);inventorySession=token;inventoryStamp=Domain.Now();inventoryPending="";inventoryJobId="";bagStatus.Text="Снимок: "+DateTime.Now.ToString("HH:mm:ss")+" · "+bagItems.Count+" типов предметов. Обновляй после изменений в игре.";FilterBag();}
    }}}catch(IOException){}catch(Exception e){inventoryPending="";inventoryJobId="";bagItems.Clear();inventoryStamp=0;bagStatus.Text="Не удалось прочитать рюкзак: "+FriendlyError(e.Message);FilterBag();}
    if(inventoryPending!=""&&Domain.Now()-inventorySentAt>20){CancelInventory();inventoryPending="";inventoryJobId="";inventoryStamp=0;bagStatus.Text="Игра не ответила. Автоматического повтора нет.";}
   }
   if(worldPending!=""){
    try{string file=Path.Combine(bridge,worldJobId==""?"world-response.txt":"world-result.txt");string expected=worldJobId==""?worldPending:worldJobId;if(File.Exists(file)){string raw=File.ReadAllText(file,Encoding.UTF8);if(raw.StartsWith(expected+"\t",StringComparison.Ordinal)){var h=FeatureData.Header(raw,expected,token,Domain.Now());
     if(h[3]=="pending"){string job=raw.Split('\n').Skip(1).FirstOrDefault();job=(job??"").Trim();if(!Regex.IsMatch(job,@"\A[a-f0-9]{32}\z"))throw new FormatException("Неверный ID чтения меток");worldJobId=job;if(cancelQuestWhenAttached||!Interactive||mainTabs.SelectedTab!=teleportPage)CancelQuestRead();else worldStatus.Text="Читаю метки открытой карты…";}
     else {string action=worldAction;worldPending="";worldJobId="";if(h[3]=="error")throw new Exception(h[4]);
      if(action=="position"){var point=FeatureData.Position(raw);if(positionPurpose=="quests"){questPoints=FeatureData.QuestPoints(raw);RenderQuestPoints();worldStatus.Text="Метки обновлены: "+questPoints.Count+". Выбери цель в таблице.";var info=raw.Split('\n').FirstOrDefault(line=>line.StartsWith("QINFO\t"));if(info!=null){var parts=info.Trim().Split('\t');if(parts.Length==3)questHint.Text="Активные задания: "+parts[1]+" основных, "+parts[2]+" побочных. Высота — по загруженной поверхности; этаж цели не определяется.";}}
       else {SetCoordinates(point);if(positionPurpose=="save"){point.Name=pointName.Text.Trim();if(point.Name.Length==0)point.Name="Точка "+DateTime.Now.ToString("HH:mm");if(point.Name.Length>100)point.Name=point.Name.Substring(0,100);points.Add(point);SavePoints();RenderPoints();worldStatus.Text="Сохранено: "+point.Name;}else worldStatus.Text="Текущая позиция считана. X="+FeatureData.Number(point.X)+", Y="+FeatureData.Number(point.Y)+", Z="+FeatureData.Number(point.Z);}}
      else if(action=="teleport"){previousPosition=FeatureData.Position(raw);worldStatus.Text="Команда телепорта отправлена. Дождись загрузки мира в игре; возврат доступен.";}
      else actorStatus.Text="Команда спауна отправлена. Проверь появление в игре.";
      AddJournal(action+": "+h[3]);
    }}}}catch(IOException){}catch(Exception e){worldPending="";worldJobId="";worldStatus.Text=actorStatus.Text="Ошибка: "+FriendlyError(e.Message);AddJournal("Ошибка: "+e.Message);}
    if(worldPending!=""&&Domain.Now()-worldSentAt>20){CancelQuestRead();worldPending="";worldJobId="";worldStatus.Text=actorStatus.Text="Нет ответа от игры. Автоматического повтора нет.";}
   }
   if(Interactive&&mainTabs.SelectedTab==bagPage&&wantInventory&&Domain.Now()>=refreshAfter&&ready&&pending==""&&!FeaturePending){wantInventory=false;RequestInventory();}FeatureControls();
  }
  string FriendlyError(string message){if(message=="load_a_save")return "загрузи сохранение";if(message.Contains("open_pda_map"))return "открой карту КПК и нажми «Обновить цели»";if(message=="cancelled")return "чтение остановлено";if(message=="diagnostic_capture")return "собрана диагностическая информация";if(message=="expired"||message=="wrong_session")return "запрос устарел; обнови связь с игрой";return message;}
  void RequestInventory(){
   try{inventoryPending=Guid.NewGuid().ToString("N");inventoryJobId="";cancelInventoryWhenAttached=false;inventorySentAt=Domain.Now();inventoryStamp=0;Domain.Atomic(Path.Combine(bridge,"inventory-request.txt"),inventoryPending+"\t"+token+"\t"+inventorySentAt+"\n");bagStatus.Text="Читаю рюкзак…";FeatureControls();}catch(Exception e){inventoryPending="";bagStatus.Text=e.Message;}
  }
  void CancelInventory(){wantInventory=false;if(inventoryPending=="")return;if(inventoryJobId==""){cancelInventoryWhenAttached=true;return;}try{Domain.Atomic(Path.Combine(bridge,"inventory-cancel.txt"),inventoryJobId+"\t"+token+"\n");inventoryPending="";inventoryJobId="";inventoryStamp=0;bagStatus.Text="Чтение остановлено. Открой вкладку или нажми «Обновить».";}catch(IOException){}catch(UnauthorizedAccessException){}}
  void SendItem(Item i,int amount,bool fromBag){
   Pulse();if(i==null||!ready||pending!=""||FeaturePending||(fromBag&&i.Restricted))return;
   try{Domain.Command(i.Id,amount);pending=Guid.NewGuid().ToString("N");sentAt=Domain.Now();spawnFromBag=fromBag;Domain.Atomic(Path.Combine(bridge,"request.txt"),pending+"\t"+token+"\t"+sentAt+"\t"+i.Id+"\t"+amount+"\t"+(!fromBag&&advanced.Checked?"1":"0")+"\n");requestLabel.Text="Отправлено: "+i.Name+" × "+amount;if(fromBag)bagStatus.Text=requestLabel.Text;AddJournal(i.Id+" × "+amount+" — отправлено");Details();FeatureControls();}catch(Exception e){pending="";requestLabel.Text="Не удалось отправить: "+e.Message;if(fromBag)bagStatus.Text=requestLabel.Text;}
  }
  void AddFromBag(int amount){var e=BagSelected;if(!FeatureData.CanAdd(e)||inventorySession!=token||inventoryStamp==0)return;SendItem(e.Item,amount,true);}
  void FeatureSpawnCompleted(bool submitted,string message){if(spawnFromBag){bagStatus.Text=message;if(submitted){wantInventory=true;refreshAfter=Domain.Now()+1;}}spawnFromBag=false;}
  void SendWorld(string payload,string action){
   Pulse();if(!ready||pending!=""||FeaturePending)return;
   try{worldPending=Guid.NewGuid().ToString("N");worldJobId="";cancelQuestWhenAttached=false;worldSentAt=Domain.Now();worldAction=action;if(action=="position"&&positionPurpose=="quests")Domain.Atomic(Path.Combine(bridge,"quest-request.txt"),worldPending+"\n");Domain.Atomic(Path.Combine(bridge,"world-request.txt"),worldPending+"\t"+token+"\t"+worldSentAt+"\t"+payload+"\n");worldStatus.Text=actorStatus.Text="Запрос отправлен…";AddJournal(payload.Replace('\t',' ')+" — отправлено");FeatureControls();}catch(Exception e){worldPending="";worldStatus.Text=actorStatus.Text=e.Message;}
  }
  void RequestPosition(string purpose){positionPurpose=purpose;SendWorld("position","position");}
  void CancelQuestRead(){if(worldPending==""||worldAction!="position"||positionPurpose!="quests")return;if(worldJobId==""){cancelQuestWhenAttached=true;return;}try{Domain.Atomic(Path.Combine(bridge,"world-cancel.txt"),worldJobId+"\n");worldPending="";worldJobId="";worldStatus.Text="Чтение целей остановлено.";}catch(IOException){}catch(UnauthorizedAccessException){}}
  void BuildQuestTargets(){
   var t=FeatureLayout(questTargets,3);t.Padding=new Padding(10);t.RowStyles.Add(new RowStyle(SizeType.Absolute,45));t.RowStyles.Add(new RowStyle(SizeType.Percent,100));t.RowStyles.Add(new RowStyle(SizeType.Absolute,55));var top=Flow();FeatureButton(questRefresh,"Обновить цели",180);top.Controls.Add(questRefresh);showOtherQuestMarkers.Text="Показать прочие метки заданий";showOtherQuestMarkers.AutoSize=true;showOtherQuestMarkers.Padding=new Padding(10,8,0,0);top.Controls.Add(showOtherQuestMarkers);t.Controls.Add(top,0,0);
   FeatureGrid(questGrid);questGrid.Columns.Add("Name","Цель / метка");questGrid.Columns.Add("X","X");questGrid.Columns.Add("Y","Y");questGrid.Columns.Add("Z","Z / поверхность");questGrid.Columns[0].FillWeight=160;t.Controls.Add(questGrid,0,1);FeatureLabel(questHint,"Открой карту КПК и нажми «Обновить цели». По умолчанию показаны отслеживаемые цели. Если высота неизвестна, телепорт отключён.");t.Controls.Add(questHint,0,2);
   questRefresh.Click+=(s,e)=>RequestPosition("quests");showOtherQuestMarkers.CheckedChanged+=(s,e)=>RenderQuestPoints();questGrid.CurrentCellChanged+=(s,e)=>ApplyQuestSelection();
  }
  void ApplyQuestSelection(){var q=questGrid.CurrentRow==null?null:questGrid.CurrentRow.Tag as QuestPoint;if(q==null)return;coordinateX.Value=(decimal)q.X;coordinateY.Value=(decimal)q.Y;coordinateZ.Value=q.Z.HasValue?(decimal)q.Z.Value:0;requiresManualHeight=!q.Z.HasValue;coordinatesChosen=q.Z.HasValue;worldStatus.Text=q.Z.HasValue?"Выбрана цель. Z получена по поверхности; внутри здания может требоваться другой этаж.":"X/Y цели выбраны. Высота не загружена — укажи Z вручную для телепорта.";FeatureControls();}
  void RenderQuestPoints(){questGrid.Rows.Clear();foreach(var q in questPoints.Where(p=>p.Tracked||showOtherQuestMarkers.Checked)){int i=questGrid.Rows.Add(q.Name+" #"+q.Id,FeatureData.Number(q.X),FeatureData.Number(q.Y),q.Z.HasValue?FeatureData.Number(q.Z.Value):"Не загружена");questGrid.Rows[i].Tag=q;}if(questGrid.Rows.Count>0){questGrid.CurrentCell=questGrid.Rows[0].Cells[0];ApplyQuestSelection();}else{coordinatesChosen=false;FeatureControls();}}
  ZonePoint CurrentPoint(){return new ZonePoint{X=(double)coordinateX.Value,Y=(double)coordinateY.Value,Z=(double)coordinateZ.Value};}
  void SetCoordinates(ZonePoint p){requiresManualHeight=false;coordinatesChosen=true;coordinateX.Value=(decimal)p.X;coordinateY.Value=(decimal)p.Y;coordinateZ.Value=(decimal)p.Z;}
  void SavePoints(){try{Domain.Atomic(Path.Combine(root,"teleport-points.json"),new JavaScriptSerializer().Serialize(points));}catch(Exception e){worldStatus.Text="Не удалось сохранить точки: "+e.Message;}}
  void RenderPoints(){pointList.Items.Clear();pointList.Items.AddRange(points.ToArray());}
  void AddJournal(string entry){journal.Items.Insert(0,DateTime.Now.ToString("HH:mm:ss")+" · "+entry);while(journal.Items.Count>100)journal.Items.RemoveAt(journal.Items.Count-1);}
  static void FeatureViewSelfTest(string root){
   string temporary=Path.Combine(Path.GetTempPath(),"ZoneSpawner-features-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(temporary);
   foreach(string file in new[]{"items.json","actors.json"})File.Copy(Path.Combine(root,file),Path.Combine(temporary,file));
   try{using(var f=new SpawnForm(temporary,true)){
    if(f.mainTabs.TabPages.Count!=5||f.bagQuantity.Maximum!=999||f.actorQuantity.Maximum!=5||f.actorDistance.Maximum!=100)throw new Exception("Feature tabs or quantity controls");
    f.Show();string session="test_session";Domain.Atomic(Path.Combine(f.bridge,"status.txt"),session+"\tready\t"+Domain.Now()+"\n");f.Pulse();f.mainTabs.SelectedTab=f.bagPage;f.Pulse();
    if(f.inventoryPending==""||!File.Exists(Path.Combine(f.bridge,"inventory-request.txt")))throw new Exception("Inventory tab does not request a snapshot");
    string invId=f.inventoryPending;var ammo=f.catalog.First(i=>i.Category=="Патроны"&&!i.Restricted);
    Domain.Atomic(Path.Combine(f.bridge,"inventory-response.txt"),invId+"\t"+session+"\t"+Domain.Now()+"\tready\tsnapshot\n"+ammo.Id+"\t15\nMedkit\t2\nUnknownSID\t1\n");f.Pulse();
    if(f.bagItems.Count!=3||f.inventoryPending!="")throw new Exception("Inventory response not rendered");
    f.bagAmmoOnly.Checked=true;if(f.bagGrid.Rows.Count!=1||!f.ammoButtons.All(b=>b.Enabled))throw new Exception("Backpack ammo selection/actions");
    foreach(int amount in new[]{30,60,100}){
     f.AddFromBag(amount);var parts=File.ReadAllText(Path.Combine(f.bridge,"request.txt")).Trim().Split('\t');if(parts[3]!=ammo.Id||parts[4]!=amount.ToString()||parts[5]!="0")throw new Exception("Quick refill changed ammunition variant or quantity");
     Domain.Atomic(Path.Combine(f.bridge,"response.txt"),f.pending+"\tsubmitted\t"+ammo.Id+"\n");f.Pulse();f.wantInventory=false;
    }
    f.bagAmmoOnly.Checked=false;f.bagSearch.Text="UnknownSID";if(f.bagAdd.Enabled||f.ammoButtons.Any(b=>b.Enabled))throw new Exception("Unknown inventory items enabled");
    f.bagSearch.Text="Medkit";if(!f.bagAdd.Enabled||f.ammoButtons.Any(b=>b.Enabled))throw new Exception("Non-ammo replenishment actions");
    f.bagQuantity.Value=7;f.AddFromBag(7);var same=File.ReadAllText(Path.Combine(f.bridge,"request.txt")).Trim().Split('\t');if(same[3]!="Medkit"||same[4]!="7")throw new Exception("Add same quantity");f.pending="";
    string before=File.ReadAllText(Path.Combine(f.bridge,"inventory-request.txt"));f.Hide();f.wantInventory=true;for(int i=0;i<120;i++)f.Pulse();if(before!=File.ReadAllText(Path.Combine(f.bridge,"inventory-request.txt")))throw new Exception("Hidden menu reads inventory again");
    f.Show();f.mainTabs.SelectedTab=f.actorPage;f.actorCategory.SelectedItem="Мутанты";if(f.actorGrid.Rows.Count==0||f.actorGrid.Rows.Cast<DataGridViewRow>().Any(r=>((ActorEntry)r.Tag).Category!="Мутанты"||((ActorEntry)r.Tag).Restricted))throw new Exception("Actor category or restricted filter");
    f.actorSearch.Text="Bloodsucker";if(f.ActorSelected==null||!f.actorSpawn.Enabled||f.actorRank.Enabled)throw new Exception("Mutant search or rank scope");
    f.mainTabs.SelectedTab=f.teleportPage;f.wantInventory=false;f.SendWorld("position","position");string worldId=f.worldPending;Domain.Atomic(Path.Combine(f.bridge,"world-response.txt"),worldId+"\t"+session+"\t"+Domain.Now()+"\tcaptured\tposition\n1.25\t-20\t50\t90\n");f.Pulse();if(f.coordinateX.Value!=1.25m||f.coordinateY.Value!=-20m)throw new Exception("Position response");
    f.SendWorld("teleport\t100\t200\t300","teleport");Domain.Atomic(Path.Combine(f.bridge,"world-response.txt"),f.worldPending+"\t"+session+"\t"+Domain.Now()+"\tsubmitted\tteleport\n1.25\t-20\t50\t90\n");f.Pulse();if(f.previousPosition==null||f.previousPosition.X!=1.25||!f.teleportBack.Enabled)throw new Exception("Return position not retained");
    f.teleportTargets.SelectedTab=f.questTargets;f.questRefresh.PerformClick();string request=f.worldPending,job=new string('b',32);if(File.ReadAllText(Path.Combine(f.bridge,"quest-request.txt")).Trim()!=request)throw new Exception("Quest query flag not scoped to request");
    Domain.Atomic(Path.Combine(f.bridge,"world-response.txt"),request+"\t"+session+"\t"+Domain.Now()+"\tpending\tposition\n"+job+"\n");f.Pulse();if(f.worldJobId!=job)throw new Exception("Quest job not attached");
    Domain.Atomic(Path.Combine(f.bridge,"world-result.txt"),job+"\t"+session+"\t"+Domain.Now()+"\tcaptured\tquest_markers\n1\t2\t3\t0\nQINFO\t1\t2\nQ\t9\ttracked\t1000\t2000\t?\tОтслеживаемая цель\nQ\t10\tmap\t3000\t4000\t500\tДругая метка\n");f.Pulse();
    if(f.questGrid.Rows.Count!=1||f.coordinatesChosen||f.teleportGo.Enabled||f.coordinateX.Value!=1000)throw new Exception("Quest height gating or tracked-only default");
    f.coordinateX.Value=1001;if(f.coordinatesChosen||f.teleportGo.Enabled)throw new Exception("Editing X bypasses missing quest height");f.coordinateZ.Value=88;if(!f.coordinatesChosen||!f.teleportGo.Enabled)throw new Exception("Explicit quest height does not unlock teleport");
    f.showOtherQuestMarkers.Checked=true;if(f.questGrid.Rows.Count!=2)throw new Exception("Other quest markers filter");f.questGrid.CurrentCell=f.questGrid.Rows[1].Cells[0];if(!f.coordinatesChosen||f.coordinateZ.Value!=500||!f.teleportGo.Enabled)throw new Exception("Surface height not used: chosen="+f.coordinatesChosen+", z="+f.coordinateZ.Value+", button="+f.teleportGo.Enabled+", row="+f.questGrid.CurrentRow.Index+", ready="+f.ready+", world="+f.worldPending+", inv="+f.inventoryPending);
    f.mainTabs.SelectedTab=f.bagPage;f.Pulse();request=f.inventoryPending;job=new string('c',32);Domain.Atomic(Path.Combine(f.bridge,"inventory-response.txt"),request+"\t"+session+"\t"+Domain.Now()+"\tpending\t"+job+"\n");f.Pulse();f.Hide();if(f.inventoryPending!=""||File.ReadAllText(Path.Combine(f.bridge,"inventory-cancel.txt")).Split('\t')[0]!=job)throw new Exception("Hidden menu did not cancel incremental inventory");
    f.InvalidateFeatureSession();if(f.previousPosition!=null||f.bagItems.Count!=0)throw new Exception("Snapshots leaked across game sessions");
    f.timer.Dispose();f.tray.Dispose();
   }}finally{string resolved=Path.GetFullPath(temporary);if(!resolved.StartsWith(Path.GetFullPath(Path.GetTempPath()),StringComparison.OrdinalIgnoreCase)||!Path.GetFileName(resolved).StartsWith("ZoneSpawner-features-",StringComparison.Ordinal))throw new Exception("Unsafe test cleanup path");Directory.Delete(resolved,true);}
   Console.WriteLine("PASS: real tab handlers, exact ammo 30/60/100, hidden idle/cancellation, actor filters, position/return, quest query attachment, tracked-only targets and unknown-height gating");
  }
 }
}
