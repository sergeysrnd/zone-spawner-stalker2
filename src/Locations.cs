using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Text;
using System.Web.Script.Serialization;
using System.Windows.Forms;

namespace ZoneSpawner {
 public sealed class TechnicianItem {
  public string Id {get;set;}public string Name {get;set;}public string Category {get;set;}
  public int Available {get;set;}public int Total {get;set;}
 }
 public sealed class TechnicianEntry {
  public string Id {get;set;}public string Name {get;set;}public string Specialization {get;set;}public string Note {get;set;}
  public List<TechnicianItem> Equipment {get;set;}
 }
 public sealed class LocationEntry {
  public string Id {get;set;}public string Name {get;set;}public string Region {get;set;}public string Note {get;set;}public string CoordinateSource {get;set;}
  public ZonePoint Landing {get;set;}public List<TechnicianEntry> Technicians {get;set;}
 }
 public static class LocationData {
  public static List<LocationEntry> Load(string root){
   string path=Path.Combine(root,"locations.json");if(!File.Exists(path))return new List<LocationEntry>();
   var rows=new JavaScriptSerializer().Deserialize<List<LocationEntry>>(File.ReadAllText(path,Encoding.UTF8));
   if(rows==null||rows.Count>500)throw new FormatException("Некорректный каталог локаций");
   var ids=new HashSet<string>(StringComparer.Ordinal);
   foreach(var row in rows){
    if(row==null||String.IsNullOrWhiteSpace(row.Id)||!ids.Add(row.Id)||String.IsNullOrWhiteSpace(row.Name)||String.IsNullOrWhiteSpace(row.Region)||row.Technicians==null)throw new FormatException("Локация без имени, региона или с повторным ID");
    if(row.Landing!=null&&(!FeatureData.Coordinate(row.Landing.X)||!FeatureData.Coordinate(row.Landing.Y)||!FeatureData.Coordinate(row.Landing.Z)||String.IsNullOrWhiteSpace(row.CoordinateSource)))throw new FormatException("Некорректная точка прибытия");
    foreach(var tech in row.Technicians){
     if(tech==null||String.IsNullOrWhiteSpace(tech.Id)||String.IsNullOrWhiteSpace(tech.Name)||tech.Equipment==null||String.IsNullOrWhiteSpace(tech.Specialization))throw new FormatException("Некорректные сведения о технике");
     var equipmentIds=new HashSet<string>(StringComparer.Ordinal);
     foreach(var e in tech.Equipment)if(e==null||String.IsNullOrWhiteSpace(e.Id)||!equipmentIds.Add(e.Id)||String.IsNullOrWhiteSpace(e.Name)||e.Available<1||e.Total<e.Available)throw new FormatException("Некорректная совместимость техника");
    }
   }return rows;
  }
  public static bool Matches(LocationEntry row,string search,string region,bool techniciansOnly,string itemId){
   if(region!="Все регионы"&&row.Region!=region||techniciansOnly&&row.Technicians.Count==0)return false;
   var techs=row.Technicians.Where(t=>String.IsNullOrEmpty(itemId)||t.Equipment.Any(e=>e.Id==itemId)).ToList();
   if(!String.IsNullOrEmpty(itemId)&&techs.Count==0)return false;
   string text=row.Name+" "+row.Region+" "+row.Id+" "+String.Join(" ",techs.Select(t=>t.Name+" "+t.Id+" "+t.Specialization+" "+t.Note+" "+String.Join(" ",t.Equipment.Select(e=>e.Id+" "+e.Name))));
   return Domain.Fold(text).Contains(Domain.Fold(search));
  }
  public static string TechNames(LocationEntry row,string itemId){return String.Join(", ",row.Technicians.Where(t=>String.IsNullOrEmpty(itemId)||t.Equipment.Any(e=>e.Id==itemId)).Select(t=>t.Name));}
  public static string Details(LocationEntry row,string itemId){
   if(row==null)return "Ничего не найдено. Измени поиск или фильтры.";
   var text=new StringBuilder(row.Name+" · "+row.Region+"\r\n"+row.Note+"\r\n");
   text.AppendLine(row.Landing==null?"Готовой точки прибытия нет. Используй «Мои точки»; координаты NPC не считаются точкой для игрока.":"Точка прибытия проводника из данных игры: "+FeatureData.Number(row.Landing.X)+", "+FeatureData.Number(row.Landing.Y)+", "+FeatureData.Number(row.Landing.Z)+".");
   foreach(var t in row.Technicians.Where(t=>String.IsNullOrEmpty(itemId)||t.Equipment.Any(e=>e.Id==itemId))){
    text.AppendLine();text.AppendLine(t.Name+" — "+t.Specialization);if(!String.IsNullOrEmpty(t.Note))text.AppendLine(t.Note);
    foreach(var group in t.Equipment.Where(e=>String.IsNullOrEmpty(itemId)||e.Id==itemId).GroupBy(e=>e.Category))text.AppendLine(group.Key+": "+String.Join("; ",group.Select(e=>e.Name+" ("+e.Available+"/"+e.Total+")")));
   }
   if(row.Technicians.Count==0)text.AppendLine("Техник в каталоге этой локации не указан.");
   text.AppendLine();text.Append("Числа — поддерживаемые/все улучшения базового прототипа, включая установку обвесов. Чертежи, взаимоисключающие ветки, репутация и сюжет проверяются в игре. Наличие NPC сейчас не считывается.");
   return text.ToString();
  }
  public static void SelfTest(string root){
   var rows=Load(root);if(rows.Count!=15||rows.Count(r=>r.Landing!=null)!=12)throw new Exception("Location catalog coverage");
   var linza=rows.Single(r=>r.Id=="zalesie").Technicians.Single();if(linza.Equipment.Any(e=>e.Id=="Exoskeleton_Neutral_Armor"))throw new Exception("Quest-only suppressor counted as general exoskeleton support");
   var garpia=rows.Single(r=>r.Id=="yanov").Technicians.Single();var saiga=garpia.Equipment.Single(e=>e.Id=="GunD12_SG");if(saiga.Available!=26||saiga.Total!=26)throw new Exception("Technician exact capability intersection");
   if(!Matches(rows.Single(r=>r.Id=="yanov"),"ГАРПИЯ","Все регионы",true,"GunD12_SG")||Matches(rows.Single(r=>r.Id=="zalesie"),"","Все регионы",true,"GunD12_SG")||Matches(rows.Single(r=>r.Id=="yanov"),"","Затон",false,null))throw new Exception("Location and technician filter scope");
   var bans=rows.Single(r=>r.Id=="kazkovy").Technicians;if(bans.Count!=2||bans[0].Equipment.Any(e=>e.Id=="GunD12_SG")||!bans[1].Equipment.Any(e=>e.Id=="GunD12_SG"))throw new Exception("Banzai story profiles combined");
   if(rows.Single(r=>r.Id=="ikar").Landing!=null||!Details(rows.Single(r=>r.Id=="ikar"),null).Contains("Готовой точки прибытия нет"))throw new Exception("NPC coordinates invented as player landing");
   Console.WriteLine("PASS: 15 locations, 12 native fast-travel landings, technician capability intersections, story profiles, no quest-only armor matches");
  }
 }
 public sealed partial class SpawnForm {
  readonly TabPage locationTargets=new TabPage("Локации и техники");readonly DataGridView locationGrid=new DataGridView();
  readonly TextBox locationSearch=new TextBox(),locationDetails=new TextBox();readonly ComboBox locationRegion=new ComboBox(),technicianEquipment=new ComboBox();readonly CheckBox techniciansOnly=new CheckBox();
  readonly Label locationCount=new Label();List<LocationEntry> locations=new List<LocationEntry>();bool filteringLocations;
  sealed class EquipmentOption {public string Id,Name;public override string ToString(){return Name;}}
  LocationEntry LocationSelected{get{return locationGrid.CurrentRow==null?null:locationGrid.CurrentRow.Tag as LocationEntry;}}
  string TechnicianItemId{get{var option=technicianEquipment.SelectedItem as EquipmentOption;return option==null?null:option.Id;}}
  void BuildLocationTargets(){
   string warning="";try{locations=LocationData.Load(root);}catch(Exception error){warning="Каталог локаций недоступен: "+error.Message;}
   var t=FeatureLayout(locationTargets,3);t.Padding=new Padding(10);t.RowStyles.Add(new RowStyle(SizeType.Absolute,95));t.RowStyles.Add(new RowStyle(SizeType.Percent,100));t.RowStyles.Add(new RowStyle(SizeType.Absolute,125));
   var filters=Flow();filters.Controls.Add(Caption("Поиск"));FeatureText(locationSearch,"Поиск локации, техника или снаряжения");locationSearch.Width=300;filters.Controls.Add(locationSearch);
   foreach(var combo in new[]{locationRegion,technicianEquipment}){combo.DropDownStyle=ComboBoxStyle.DropDownList;combo.BackColor=PanelColor;combo.ForeColor=TextColor;}
   locationRegion.Width=185;locationRegion.AccessibleName="Регион локации";locationRegion.Items.Add("Все регионы");locationRegion.Items.AddRange(locations.Select(l=>l.Region).Distinct().OrderBy(x=>x).ToArray());locationRegion.SelectedIndex=0;filters.Controls.Add(locationRegion);
   techniciansOnly.Text="Только с техником";techniciansOnly.AutoSize=true;techniciansOnly.Padding=new Padding(10,7,0,0);filters.Controls.Add(techniciansOnly);
   filters.Controls.Add(Caption("Техник для"));technicianEquipment.Width=350;technicianEquipment.AccessibleName="Снаряжение для поиска техника";technicianEquipment.Items.Add(new EquipmentOption{Name="Любое снаряжение"});
   foreach(var e in locations.SelectMany(l=>l.Technicians).SelectMany(n=>n.Equipment).GroupBy(e=>e.Id).Select(g=>g.First()).OrderBy(e=>e.Name))technicianEquipment.Items.Add(new EquipmentOption{Id=e.Id,Name=e.Name+" · "+e.Category});
   technicianEquipment.SelectedIndex=0;filters.Controls.Add(technicianEquipment);locationCount.AutoSize=true;locationCount.Padding=new Padding(10,8,0,0);filters.Controls.Add(locationCount);t.Controls.Add(filters,0,0);
   FeatureGrid(locationGrid);locationGrid.AccessibleName="Локации и техники";locationGrid.Columns.Add("Location","Локация");locationGrid.Columns.Add("Region","Регион");locationGrid.Columns.Add("Technicians","Техники");locationGrid.Columns.Add("Specialization","Специализации");locationGrid.Columns.Add("Landing","Прибытие");
   locationGrid.Columns[0].FillWeight=100;locationGrid.Columns[1].FillWeight=65;locationGrid.Columns[2].FillWeight=90;locationGrid.Columns[3].FillWeight=160;locationGrid.Columns[4].FillWeight=75;t.Controls.Add(locationGrid,0,1);
   locationDetails.Dock=DockStyle.Fill;locationDetails.ReadOnly=true;locationDetails.Multiline=true;locationDetails.ScrollBars=ScrollBars.Vertical;locationDetails.BackColor=PanelColor;locationDetails.ForeColor=TextColor;locationDetails.BorderStyle=BorderStyle.None;locationDetails.AccessibleName="Локация и возможности техников";t.Controls.Add(locationDetails,0,2);
   locationSearch.TextChanged+=(s,e)=>FilterLocations();locationRegion.SelectedIndexChanged+=(s,e)=>FilterLocations();techniciansOnly.CheckedChanged+=(s,e)=>FilterLocations();technicianEquipment.SelectedIndexChanged+=(s,e)=>FilterLocations();locationGrid.CurrentCellChanged+=(s,e)=>{if(!filteringLocations&&teleportTargets.SelectedTab==locationTargets)ApplyLocationSelection();};
   FilterLocations();if(warning!="")locationDetails.Text=warning;
  }
  void FilterLocations(){
   string id=LocationSelected==null?null:LocationSelected.Id;filteringLocations=true;
   try{locationGrid.Rows.Clear();foreach(var row in locations.Where(l=>LocationData.Matches(l,locationSearch.Text,locationRegion.SelectedItem as string,techniciansOnly.Checked,TechnicianItemId)).OrderBy(l=>l.Region).ThenBy(l=>l.Name)){
    var techs=row.Technicians.Where(n=>String.IsNullOrEmpty(TechnicianItemId)||n.Equipment.Any(e=>e.Id==TechnicianItemId));
    int index=locationGrid.Rows.Add(row.Name,row.Region,LocationData.TechNames(row,TechnicianItemId),String.Join("; ",techs.Select(n=>n.Specialization).Distinct()),row.Landing==null?"Нет готовой точки":"Точка проводника");locationGrid.Rows[index].Tag=row;
   }
   if(locationGrid.Rows.Count>0){var row=locationGrid.Rows.Cast<DataGridViewRow>().FirstOrDefault(r=>((LocationEntry)r.Tag).Id==id)??locationGrid.Rows[0];locationGrid.CurrentCell=row.Cells[0];}
   }finally{filteringLocations=false;}
   locationCount.Text=locationGrid.Rows.Count+" / "+locations.Count;if(teleportTargets.SelectedTab==locationTargets)ApplyLocationSelection();
  }
  void ApplyLocationSelection(){
   var row=LocationSelected;locationDetails.Text=LocationData.Details(row,TechnicianItemId);
   if(row==null||row.Landing==null){coordinatesChosen=false;requiresManualHeight=true;worldStatus.Text=row==null?"Нет выбранной локации.":row.Name+": готовой точки прибытия нет. Используй сохранённую позицию во вкладке «Мои точки».";}
   else {SetCoordinates(row.Landing);worldStatus.Text=row.Name+" — точка прибытия проводника. Техники отмечены в таблице; их доступность зависит от сюжета.";}
   FeatureControls();
  }
  void ApplyTeleportTarget(){
   if(teleportTargets.SelectedTab==locationTargets)ApplyLocationSelection();
   else if(teleportTargets.SelectedTab==questTargets){if(questGrid.CurrentRow==null){coordinatesChosen=false;FeatureControls();}else ApplyQuestSelection();}
   else {var point=pointList.SelectedItem as ZonePoint;if(point!=null)SetCoordinates(point);else coordinatesChosen=false;FeatureControls();}
  }
  static void LocationViewSelfTest(string root){
   string temp=Path.Combine(Path.GetTempPath(),"ZoneSpawner-locations-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(temp);
   foreach(var file in new[]{"items.json","actors.json","locations.json"})File.Copy(Path.Combine(root,file),Path.Combine(temp,file));
   try{using(var f=new SpawnForm(temp,true)){
    f.Show();Domain.Atomic(Path.Combine(f.bridge,"status.txt"),"location_test\tready\t"+Domain.Now()+"\n");f.Pulse();f.mainTabs.SelectedTab=f.teleportPage;
    if(f.teleportTargets.TabPages.Count!=3||f.locationGrid.Rows.Count!=15||!f.teleportGo.Enabled)throw new Exception("Location tab initialization: tabs="+f.teleportTargets.TabPages.Count+", rows="+f.locationGrid.Rows.Count+", go="+f.teleportGo.Enabled+", ready="+f.ready+", chosen="+f.coordinatesChosen+", selected="+(f.LocationSelected==null?"null":f.LocationSelected.Id));
    f.locationSearch.Text="Гарпия";if(f.locationGrid.Rows.Count!=1||f.LocationSelected.Id!="yanov"||f.coordinateX.Value!=215900||f.coordinateZ.Value!=946||!f.teleportGo.Enabled)throw new Exception("Location selection does not use fast travel landing");
    f.locationSearch.Clear();f.technicianEquipment.SelectedItem=f.technicianEquipment.Items.Cast<EquipmentOption>().Single(e=>e.Id=="GunD12_SG");
    if(f.locationGrid.Rows.Count!=3||f.locationGrid.Rows.Cast<DataGridViewRow>().Any(r=>((LocationEntry)r.Tag).Id=="zalesie"))throw new Exception("Gear-specific technician filtering");
    f.locationSearch.Text="Банзай";if(f.locationGrid.Rows.Count!=1||f.locationDetails.Text.Contains("Начальный профиль")||!f.locationDetails.Text.Contains("26/26"))throw new Exception("Selected equipment mixes initial Banzai with late profile");
    f.locationSearch.Text="__no_location__";if(f.teleportGo.Enabled||f.coordinatesChosen)throw new Exception("Empty location filter retains a teleport target");
    f.locationSearch.Clear();f.technicianEquipment.SelectedIndex=0;f.locationSearch.Text="Икар";if(f.locationGrid.Rows.Count!=1||f.teleportGo.Enabled||f.coordinatesChosen)throw new Exception("Missing landing enabled");
    f.coordinateZ.Value=500;if(f.teleportGo.Enabled)throw new Exception("Editing Z bypasses missing native landing");
    f.points.Add(new ZonePoint{Name="Saved spot",X=10,Y=20,Z=30});f.RenderPoints();f.teleportTargets.SelectedTab=f.savedTargets;f.pointList.SelectedIndex=0;
    if(f.coordinateX.Value!=10||f.coordinateZ.Value!=30||!f.teleportGo.Enabled)throw new Exception("Personal point changed by locations");
    f.teleportTargets.SelectedTab=f.locationTargets;if(f.teleportGo.Enabled)throw new Exception("Switching back bypasses unavailable location");
    f.locationSearch.Text="Гарпия";f.teleportGo.PerformClick();var req=File.ReadAllText(Path.Combine(f.bridge,"world-request.txt")).Trim().Split('\t');if(req.Length!=7||req[3]!="teleport"||req[4]!="215900"||req[5]!="270210"||req[6]!="946")throw new Exception("Location teleport request altered coordinates");
    Domain.Atomic(Path.Combine(f.bridge,"world-response.txt"),f.worldPending+"\tlocation_test\t"+Domain.Now()+"\tsubmitted\tteleport\n10\t20\t30\t0\n");f.Pulse();if(f.previousPosition==null||f.previousPosition.X!=10||!f.teleportBack.Enabled)throw new Exception("Location teleport lost return position");
    f.teleportTargets.SelectedTab=f.questTargets;f.questPoints=new List<QuestPoint>{new QuestPoint{Id="1",Name="Goal",Tracked=true,X=100,Y=200,Z=null}};f.RenderQuestPoints();if(f.teleportGo.Enabled)throw new Exception("Location height leaked into unknown quest height");
    f.teleportTargets.SelectedTab=f.locationTargets;if(!f.teleportGo.Enabled||f.coordinateZ.Value!=946)throw new Exception("Location selection not restored");
    string before=File.ReadAllText(Path.Combine(f.bridge,"world-request.txt"));for(int i=0;i<120;i++)f.Pulse();if(f.worldPending!=""||File.Exists(Path.Combine(f.bridge,"inventory-request.txt"))||before!=File.ReadAllText(Path.Combine(f.bridge,"world-request.txt")))throw new Exception("Location catalog requests game reads in idle");
    f.Hide();for(int i=0;i<120;i++)f.Pulse();if(before!=File.ReadAllText(Path.Combine(f.bridge,"world-request.txt")))throw new Exception("Hidden location catalog reads game");f.timer.Dispose();f.tray.Dispose();
   }}finally{string resolved=Path.GetFullPath(temp);if(!resolved.StartsWith(Path.GetFullPath(Path.GetTempPath()),StringComparison.OrdinalIgnoreCase)||!Path.GetFileName(resolved).StartsWith("ZoneSpawner-locations-",StringComparison.Ordinal))throw new Exception("Unsafe location test cleanup");Directory.Delete(resolved,true);}
   Console.WriteLine("PASS: location UI filters, precise teleport/return, missing landing gating, saved/quest tab isolation, 120 visible and hidden idle cycles without world reads");
  }
 }
}
