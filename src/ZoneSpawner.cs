using System;
using System.Collections.Generic;
using System.Globalization;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.RegularExpressions;
using System.Web.Script.Serialization;
using System.Windows.Forms;
using System.Diagnostics;
using System.Threading;

namespace ZoneSpawner {
 public sealed class GameLifetime : IDisposable {
  readonly Process owner;readonly Action callback;int fired=0;volatile bool disposed=false;
  public GameLifetime(Process process,Action onExit){
   owner=process;callback=onExit;owner.Exited+=Exited;owner.EnableRaisingEvents=true;
   if(owner.HasExited)NotifyExit();
  }
  void Exited(object sender,EventArgs e){NotifyExit();}
  void NotifyExit(){if(!disposed && Interlocked.Exchange(ref fired,1)==0)callback();}
  public void Dispose(){disposed=true;owner.Exited-=Exited;owner.Dispose();}
 }
 public sealed class ArtifactProperty {
  public string Id {get;set;}public string Name {get;set;}public string Level {get;set;}public string ValueText {get;set;}public string Benefit {get;set;}
  public bool Visible {get;set;}public bool Conditional {get;set;}
 }
 public sealed class Item {
  public string Id {get;set;} public string Name {get;set;} public string English {get;set;}
  public string Category {get;set;} public string Rarity {get;set;} public bool Restricted {get;set;} public string Description {get;set;}
  public double? Weight {get;set;} public double? Cost {get;set;}
  public string Caliber {get;set;} public string CaliberId {get;set;}
  public string[] CompatibleIds {get;set;} public string[] CompatibleNames {get;set;}
  public ArtifactProperty[] ArtifactProperties {get;set;}public string ArtifactNote {get;set;}public bool ArtifactSpecial {get;set;}
 }
 public static class Domain {
  public static string Fold(string s) { return (s??"").ToLowerInvariant().Replace('ё','е'); }
  public static bool Matches(Item i,string query) {
   string caliber=i.Caliber??"";
   string hay=Fold(i.Id+" "+i.Name+" "+i.English+" "+i.Category+" "+i.Rarity+" "+caliber+" "+caliber.Replace('x','×')+" "+caliber.Replace('x','х')+" "+caliber.Replace('.',',')+" "+String.Join(" ",i.CompatibleIds??new string[0])+" "+String.Join(" ",i.CompatibleNames??new string[0])+" "+String.Join(" ",(i.ArtifactProperties??new ArtifactProperty[0]).Where(p=>p.Visible).Select(p=>p.Name)));
   return Fold(query).Split(new[]{' ','\t'},StringSplitOptions.RemoveEmptyEntries).All(hay.Contains);
  }
  public static bool RarityMatches(Item i,string rarity) {return rarity=="Любая редкость" || (i.Category=="Артефакты" && i.Rarity==rarity);}
  public static string ArtifactText(Item i){
   if(i==null || i.Category!="Артефакты")return "";
   var lines=new List<string>();if(i.ArtifactSpecial)lines.Add("Особый артефакт — базовые эффекты");
   foreach(var p in (i.ArtifactProperties??new ArtifactProperty[0]).Where(p=>p.Visible))lines.Add((p.Benefit=="Positive"?"+ ":p.Benefit=="Negative"?"− ":"• ")+p.Name+": "+p.ValueText);
   if(!String.IsNullOrEmpty(i.ArtifactNote)){if(lines.Count>0)lines.Add("");lines.Add(i.ArtifactNote);}
   return String.Join("\n",lines);
  }
  public static string[] SortFields(string category) {return category=="Артефакты"?new[]{"Название","Редкость","Категория","ID","Вес","Базовая цена"}:(category=="Оружие"||category=="Патроны")?new[]{"Название","Калибр","Категория","ID","Вес","Базовая цена"}:new[]{"Название","Категория","ID","Вес","Базовая цена"};}
  public static List<Item> FilterCatalog(IEnumerable<Item> source,string category,string query,string rarity,string field,bool descending,bool advanced,HashSet<string> favorites) {
   var items=source.Where(x=>(advanced || !x.Restricted) &&
    (category=="Все предметы" || (category=="★ Избранное"?favorites.Contains(x.Id):x.Category==category)) && Matches(x,query));
   if(category=="Артефакты")items=items.Where(x=>RarityMatches(x,rarity));
   return Sort(items,SortFields(category).Contains(field)?field:"Название",descending);
  }
  public static int RarityRank(string rarity){switch(rarity){case "Обычный":return 1;case "Необычный":return 2;case "Редкий":return 3;case "Легендарный":return 4;default:return 0;}}
  public static List<Item> Sort(IEnumerable<Item> source,string field,bool descending) {
   var names=StringComparer.Create(CultureInfo.GetCultureInfo("ru-RU"),true);
   Func<Item,double?> metric=null;
   if(field=="Редкость")metric=x=>RarityRank(x.Rarity)==0?(double?)null:RarityRank(x.Rarity);
   else if(field=="Вес")metric=x=>x.Weight;
   else if(field=="Базовая цена")metric=x=>x.Cost;
   IOrderedEnumerable<Item> ordered;
   if(metric!=null){var known=source.OrderBy(x=>!metric(x).HasValue);ordered=descending?known.ThenByDescending(metric):known.ThenBy(metric);}
   else {Func<Item,string> key=field=="ID"?(Func<Item,string>)(x=>x.Id):field=="Категория"?(Func<Item,string>)(x=>x.Category):field=="Калибр"?(Func<Item,string>)(x=>x.Caliber):(x=>x.Name);var known=source.OrderBy(x=>field=="Калибр"&&String.IsNullOrEmpty(x.Caliber));ordered=descending?known.ThenByDescending(key,names):known.ThenBy(key,names);}
   return ordered.ThenBy(x=>x.Name,names).ThenBy(x=>x.Id,StringComparer.Ordinal).ToList();
  }
  public static string Command(string id,int count) {
   if (!Regex.IsMatch(id??"", @"\A[A-Za-z0-9_]{1,160}\z")) throw new ArgumentException("Неверный ID предмета");
   if (count<1 || count>999) throw new ArgumentOutOfRangeException("count");
   return "XCreateItemInInventoryByID "+id+" 0 "+count+" 1";
  }
  public static long Now() { return (long)(DateTime.UtcNow-new DateTime(1970,1,1)).TotalSeconds; }
  public static void Atomic(string file,string text) {
   string tmp=file+".tmp"; File.WriteAllText(tmp,text,new UTF8Encoding(false));
   if (File.Exists(file)) File.Replace(tmp,file,null); else File.Move(tmp,file);
  }
  public static void SelfTest(string root) {
   var all=new JavaScriptSerializer().Deserialize<List<Item>>(File.ReadAllText(Path.Combine(root,"items.json"),Encoding.UTF8));
   if (all.Count<300 || all.Select(x=>x.Id).Distinct().Count()!=all.Count) throw new Exception("Catalog identity");
   var med=all.Single(x=>x.Id=="Medkit");
   if (!Matches(med,"АПТЕЧКА") || !Matches(med,"med kit")) throw new Exception("Search");
   if (Fold("ЁЖ")!="еж" || Matches(med,"неизвестный предмет")) throw new Exception("Normalization");
   if (Command("Medkit",5)!="XCreateItemInInventoryByID Medkit 0 5 1") throw new Exception("Quantity command");
   foreach(var bad in new[]{"Medkit;quit","Medkit\nquit","../Medkit",""}) {
    bool rejected=false;try{Command(bad,1);}catch(ArgumentException){rejected=true;}
    if(!rejected)throw new Exception("Rejected ID "+bad);
   }
   foreach(int count in new[]{0,-1,1000}) {
    bool rejected=false;try{Command("Medkit",count);}catch(ArgumentOutOfRangeException){rejected=true;}
    if(!rejected)throw new Exception("Quantity bounds");
   }
   var compass=all.Single(x=>x.Id=="GArtifactCompass");
   if(compass.ArtifactProperties==null || !compass.ArtifactProperties.Any(p=>p.Id=="ArtifactProtectionStrike4"&&p.Level=="Max"&&p.Benefit=="Positive") || !ArtifactText(compass).Contains("− Радиация: Максимально"))throw new Exception("Artifact property severity or radiation sign");
   if(!Matches(compass,"Броня") || ArtifactText(med)!="")throw new Exception("Artifact property search or category scope");
   var nightstar=all.Single(x=>x.Id=="GArtifactNightStar");if(!ArtifactText(nightstar).Contains("+9 кг") || ArtifactText(nightstar).Contains("Порог замедления"))throw new Exception("Carry weight units or hidden effect display");
   var water=all.Single(x=>x.Id=="AArtifactWeirdWater");if(!water.ArtifactSpecial || !ArtifactText(water).Contains("+50%") || String.IsNullOrEmpty(water.ArtifactNote))throw new Exception("Composite or conditional artifact properties");
   var flower=all.Single(x=>x.Id=="AArtifactWeirdFlower");if(flower.ArtifactProperties.Length!=0 || String.IsNullOrEmpty(flower.ArtifactNote))throw new Exception("Missing special artifact effects must be stated");
   if(compass.Rarity!="Легендарный" || !RarityMatches(compass,"Легендарный") || RarityMatches(med,"Легендарный"))throw new Exception("Rarity filter");
   if(all.Any(x=>x.Id.EndsWith("_Fake")&&!x.Restricted))throw new Exception("Service artifacts visible");
   if(all.Any(x=>x.Category=="Артефакты"&&!x.Restricted&&String.IsNullOrEmpty(x.Rarity)))throw new Exception("Artifact rarity missing");
   var sample=new[]{new Item{Id="unknown",Name="unknown"},new Item{Id="a",Name="a",Rarity="Обычный",Weight=10,Cost=500},new Item{Id="b",Name="b",Rarity="Легендарный",Weight=2,Cost=50}};
   if(Sort(sample,"Редкость",true)[0].Id!="b" || Sort(sample,"Редкость",false)[0].Id!="a" || Sort(sample,"Редкость",true)[2].Id!="unknown")throw new Exception("Rarity ordering");
   if(Sort(sample,"Вес",false)[0].Id!="b" || Sort(sample,"Базовая цена",true)[0].Id!="a")throw new Exception("Numeric ordering");
   var scope=all.Single(x=>x.Id=="RU_X4Scope_1");
   if(scope.CompatibleIds==null || !scope.CompatibleIds.Contains("GunAK74_ST") || !Matches(scope,"GunAK74_ST"))throw new Exception("Compatibility lookup");
   var ak=all.Single(x=>x.Id=="GunAK74_ST");var rat=all.Single(x=>x.Id=="Gun_RatKiller_SMG");var zubr=all.Single(x=>x.Id=="GunZubr_PP");
   if(ak.Caliber!="5.45x39" || rat.Caliber!="9x18" || zubr.Caliber!="9x19" || !Matches(ak,"5.45×39"))throw new Exception("Weapon caliber or unique variant");
   var ammo=all.Single(x=>x.Id=="A918D");if(!ammo.CompatibleIds.Contains(rat.Id)||ammo.CompatibleIds.Contains(zubr.Id)||!Matches(ammo,"Крысобой"))throw new Exception("Ammo compatibility ignores unique caliber");
   var default308=all.Single(x=>x.Id=="A762NATOD");var ap308=all.Single(x=>x.Id=="A762NATOA");
   if(default308.CompatibleIds.Contains("GunM701_SP")||!ap308.CompatibleIds.Contains("GunM701_SP"))throw new Exception("Ammo type compatibility ignored");
   if(all.Single(x=>x.Id=="AVOG").Category!="Патроны"||!all.Single(x=>x.Id=="AVOG").CompatibleIds.Contains("RU_GLaunch_1")||all.Single(x=>x.Id=="A762D").CompatibleIds.Length!=0)throw new Exception("Launcher ammo or unavailable compatibility");
   var caliberSample=new[]{new Item{Id="a",Name="a",Caliber="9x19"},new Item{Id="b",Name="b",Caliber="5.45x39"},new Item{Id="missing",Name="missing"}};
   if(Sort(caliberSample,"Калибр",false)[0].Id!="b"||Sort(caliberSample,"Калибр",true)[0].Id!="a"||Sort(caliberSample,"Калибр",true)[2].Id!="missing"||SortFields("Артефакты").Contains("Калибр"))throw new Exception("Caliber sorting or scope");
   var weapons=FilterCatalog(all,"Оружие","","Легендарный","Редкость",false,false,new HashSet<string>());
   var normalWeapons=FilterCatalog(all,"Оружие","","Любая редкость","Название",false,false,new HashSet<string>());
   if(weapons.Count==0 || !weapons.Select(x=>x.Id).SequenceEqual(normalWeapons.Select(x=>x.Id)))throw new Exception("Artifact filter/sort leaked into weapons");
   var legendary=FilterCatalog(all,"Артефакты","","Легендарный","Редкость",false,false,new HashSet<string>());
   if(legendary.Count<4 || legendary.Any(x=>x.Rarity!="Легендарный" || x.Restricted))throw new Exception("Artifact filter scope");
   if(SortFields("Все предметы").Contains("Редкость") || !SortFields("Артефакты").Contains("Редкость"))throw new Exception("Sort options scope");
   var starred=FilterCatalog(all,"★ Избранное","","Легендарный","Название",false,false,new HashSet<string>(new[]{"Medkit","GArtifactCompass"}));
   if(starred.Count!=2)throw new Exception("Artifact filter leaked into favorites");
   Console.WriteLine("PASS: catalog, search, quantity, sort order and category scope, compatibility, favorites and service filtering; "+all.Count+" items");
  }
 }
 public sealed partial class SpawnForm:Form {
  static readonly Color Bg=Color.FromArgb(19,23,23), PanelColor=Color.FromArgb(28,34,33), TextColor=Color.FromArgb(230,235,224), Accent=Color.FromArgb(187,213,115);
  readonly string root,bridge; readonly List<Item> catalog; readonly HashSet<string> favorites=new HashSet<string>();
  readonly TextBox search=new TextBox(),preview=new TextBox(); readonly ListBox categories=new ListBox();
  readonly ComboBox rarity=new ComboBox();
  readonly FlowLayoutPanel artifactFilters=new FlowLayoutPanel();readonly Label categoryTitle=new Label();
  readonly ComboBox sorting=new ComboBox();readonly Button sortDirection=new Button();bool descending=false;
  string categoryKey="Все предметы";readonly Dictionary<string,string> categorySort=new Dictionary<string,string>();readonly Dictionary<string,bool> categoryDescending=new Dictionary<string,bool>();
  readonly TextBox compatibility=new TextBox();
  readonly RichTextBox artifactProperties=new RichTextBox(),fullDescription=new RichTextBox();
  readonly TabControl detailTabs=new TabControl();readonly TabPage propertyTab=new TabPage("Свойства"),descriptionTab=new TabPage("Описание");
  readonly TableLayoutPanel layout,bottom;string detailItemId="";
  readonly NotifyIcon tray=new NotifyIcon();bool exitRequested=false;
  readonly bool testMode;bool suppressInitialShow;
  readonly DataGridView grid=new DataGridView(); readonly NumericUpDown quantity=new NumericUpDown();
  readonly Label status=new Label(),countLabel=new Label(),nameLabel=new Label(),requestLabel=new Label();
  readonly Button spawn=new Button(),favorite=new Button(); readonly CheckBox advanced=new CheckBox();
  readonly System.Windows.Forms.Timer timer=new System.Windows.Forms.Timer(); string token="",pending="";long sentAt=0,lastProbe=0; bool ready=false,building=true,hotkey=false; List<Item> visible=new List<Item>();
  GameLifetime gameLifetime;volatile bool gameEnded=false;
  [DllImport("user32.dll")] static extern bool RegisterHotKey(IntPtr hwnd,int id,uint modifiers,uint key);
  [DllImport("user32.dll")] static extern bool UnregisterHotKey(IntPtr hwnd,int id);
  Item Selected {get {return grid.CurrentRow==null?null:grid.CurrentRow.Tag as Item;}}
  public SpawnForm(string folder,bool selfTest=false,bool startHidden=false) {
   testMode=selfTest;suppressInitialShow=startHidden;
   root=folder;bridge=Path.Combine(root,"bridge");Directory.CreateDirectory(bridge);
   catalog=new JavaScriptSerializer().Deserialize<List<Item>>(File.ReadAllText(Path.Combine(root,"items.json"),Encoding.UTF8));
   var favPath=Path.Combine(root,"favorites.txt");if(File.Exists(favPath))foreach(var id in File.ReadAllLines(favPath))favorites.Add(id);
   Text="Zone Spawner — S.T.A.L.K.E.R. 2"; BackColor=Bg;ForeColor=TextColor;Font=new Font("Segoe UI",10);
   MinimumSize=new Size(1100,780);ClientSize=new Size(1360,860);StartPosition=FormStartPosition.CenterScreen;TopMost=true;KeyPreview=true;
   AutoScaleMode=AutoScaleMode.Dpi;
   layout=new TableLayoutPanel{Dock=DockStyle.Fill,Padding=new Padding(24),ColumnCount=2,RowCount=6};
   layout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute,195));layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,100));
   layout.RowStyles.Add(new RowStyle(SizeType.Absolute,77));layout.RowStyles.Add(new RowStyle(SizeType.Absolute,58));layout.RowStyles.Add(new RowStyle(SizeType.Absolute,38));layout.RowStyles.Add(new RowStyle(SizeType.Absolute,38));layout.RowStyles.Add(new RowStyle(SizeType.Percent,100));layout.RowStyles.Add(new RowStyle(SizeType.Absolute,252));Controls.Add(layout);
   var header=new Panel{Dock=DockStyle.Fill};layout.Controls.Add(header,0,0);layout.SetColumnSpan(header,2);
   var title=new Label{Text="ZONE SPAWNER",Font=new Font("Segoe UI",23,FontStyle.Bold),AutoSize=true,Location=new Point(0,0),ForeColor=Accent};header.Controls.Add(title);
   var subtitle=new Label{Text="S.T.A.L.K.E.R. 2  /  каталог твоей Steam-сборки",AutoSize=true,Location=new Point(2,47),ForeColor=Color.Silver};header.Controls.Add(subtitle);
   status.AutoSize=false;status.TextAlign=ContentAlignment.MiddleRight;status.Size=new Size(410,58);status.Anchor=AnchorStyles.Top|AnchorStyles.Right;status.Location=new Point(header.Width-410,0);header.Controls.Add(status);
   header.Resize+=(s,e)=>status.Left=header.Width-status.Width;
   var catTitle=new Label{Text="КАТЕГОРИИ",Dock=DockStyle.Fill,TextAlign=ContentAlignment.MiddleLeft,ForeColor=Color.Silver};layout.Controls.Add(catTitle,0,1);
   var searchPanel=new TableLayoutPanel{Dock=DockStyle.Fill,ColumnCount=3,RowCount=1,Padding=new Padding(0,7,0,9)};
   searchPanel.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute,65));searchPanel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,100));searchPanel.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute,42));
   search.BorderStyle=BorderStyle.FixedSingle;search.BackColor=PanelColor;search.ForeColor=TextColor;search.Font=new Font("Segoe UI",13);search.Dock=DockStyle.Fill;search.AccessibleName="Поиск предметов";
   searchPanel.Controls.Add(new Label{Text="Поиск",Dock=DockStyle.Fill,TextAlign=ContentAlignment.MiddleLeft},0,0);searchPanel.Controls.Add(search,1,0);
   rarity.DropDownStyle=ComboBoxStyle.DropDownList;rarity.BackColor=PanelColor;rarity.ForeColor=TextColor;rarity.Dock=DockStyle.Fill;rarity.AccessibleName="Редкость артефактов";
   rarity.Items.AddRange(new object[]{"Любая редкость","Обычный","Необычный","Редкий","Легендарный"});rarity.SelectedIndex=0;
   var clear=Button("×");clear.Dock=DockStyle.Fill;clear.Click+=(s,e)=>{search.Clear();search.Focus();};searchPanel.Controls.Add(clear,2,0);layout.Controls.Add(searchPanel,1,1);
   categories.Dock=DockStyle.Fill;categories.BackColor=PanelColor;categories.ForeColor=TextColor;categories.BorderStyle=BorderStyle.None;categories.ItemHeight=30;categories.IntegralHeight=false;
   categories.Items.Add("Все предметы");categories.Items.Add("★ Избранное");foreach(var c in catalog.Select(x=>x.Category).Distinct().OrderBy(x=>x))categories.Items.Add(c);
   categories.SelectedIndex=0;layout.Controls.Add(categories,0,2);layout.SetRowSpan(categories,3);
   var categoryContext=new FlowLayoutPanel{Dock=DockStyle.Fill,WrapContents=false,AccessibleName="Параметры выбранной категории"};
   categoryTitle.Text="Все предметы";categoryTitle.AutoSize=true;categoryTitle.Font=new Font("Segoe UI",11,FontStyle.Bold);categoryTitle.ForeColor=Accent;categoryTitle.Padding=new Padding(0,4,18,0);categoryContext.Controls.Add(categoryTitle);
   artifactFilters.AutoSize=true;artifactFilters.WrapContents=false;artifactFilters.Visible=false;artifactFilters.AccessibleName="Фильтры артефактов";
   artifactFilters.Controls.Add(new Label{Text="Редкость",AutoSize=true,Padding=new Padding(0,5,8,0)});rarity.Dock=DockStyle.None;rarity.Width=195;artifactFilters.Controls.Add(rarity);categoryContext.Controls.Add(artifactFilters);layout.Controls.Add(categoryContext,1,2);
   var sortPanel=new FlowLayoutPanel{Dock=DockStyle.Fill,WrapContents=false};
   sortPanel.Controls.Add(new Label{Text="Сортировка",AutoSize=true,Padding=new Padding(0,4,8,0)});
   sorting.DropDownStyle=ComboBoxStyle.DropDownList;sorting.Width=190;sorting.AccessibleName="Сортировать по";sorting.Items.AddRange(new object[]{"Название","Категория","ID","Вес","Базовая цена"});sorting.SelectedIndex=0;sortPanel.Controls.Add(sorting);
   sortDirection.Text="↑ По возрастанию";Style(sortDirection);sortDirection.Width=170;sortDirection.Height=28;sortPanel.Controls.Add(sortDirection);
   sortPanel.Controls.Add(new Label{Text="или нажми заголовок столбца",AutoSize=true,ForeColor=Color.Silver,Padding=new Padding(12,4,0,0)});layout.Controls.Add(sortPanel,1,3);
   grid.Dock=DockStyle.Fill;grid.BackgroundColor=PanelColor;grid.BorderStyle=BorderStyle.None;grid.AllowUserToAddRows=false;grid.AllowUserToDeleteRows=false;grid.AllowUserToResizeRows=false;grid.ReadOnly=true;grid.MultiSelect=false;grid.RowHeadersVisible=false;grid.SelectionMode=DataGridViewSelectionMode.FullRowSelect;grid.AutoSizeColumnsMode=DataGridViewAutoSizeColumnsMode.Fill;grid.GridColor=Color.FromArgb(40,48,44);grid.EnableHeadersVisualStyles=false;grid.RowTemplate.Height=34;
   grid.DefaultCellStyle.BackColor=PanelColor;grid.DefaultCellStyle.ForeColor=TextColor;grid.DefaultCellStyle.SelectionBackColor=Color.FromArgb(67,81,53);grid.DefaultCellStyle.SelectionForeColor=Color.White;grid.DefaultCellStyle.Padding=new Padding(6,0,6,0);
   grid.ColumnHeadersDefaultCellStyle.BackColor=Color.FromArgb(36,43,39);grid.ColumnHeadersDefaultCellStyle.ForeColor=Accent;grid.ColumnHeadersHeight=34;
   grid.Columns.Add("fav","★");grid.Columns[0].FillWeight=4;grid.Columns.Add("name","Предмет");grid.Columns[1].FillWeight=25;grid.Columns.Add("rarity","Редкость");grid.Columns[2].FillWeight=16;grid.Columns.Add("id","ID");grid.Columns[3].FillWeight=29;grid.Columns.Add("weight","Вес, кг");grid.Columns[4].FillWeight=10;grid.Columns.Add("cost","Базовая цена");grid.Columns[5].FillWeight=16;grid.Columns[5].HeaderCell.ToolTipText="Цена предмета до коэффициентов торговца";grid.Columns.Add("compatibility","Совместимое оружие");grid.Columns[6].FillWeight=35;foreach(DataGridViewColumn col in grid.Columns)col.SortMode=DataGridViewColumnSortMode.Programmatic;
   grid.Columns[0].SortMode=DataGridViewColumnSortMode.NotSortable;
   grid.Columns[2].Visible=false;grid.Columns[6].Visible=false;grid.Columns[6].SortMode=DataGridViewColumnSortMode.NotSortable;
   grid.Columns.Add("caliber","Калибр");grid.Columns[7].FillWeight=16;grid.Columns[7].DisplayIndex=2;grid.Columns[7].Visible=false;grid.Columns[7].SortMode=DataGridViewColumnSortMode.Programmatic;grid.Columns[7].HeaderCell.ToolTipText="Калибр базового прототипа; переоборудование у техника может его изменить";
   layout.Controls.Add(grid,1,4);
   var leftBottom=new Panel{Dock=DockStyle.Fill};layout.Controls.Add(leftBottom,0,5);
   countLabel.SetBounds(0,15,190,42);leftBottom.Controls.Add(countLabel);advanced.Text="Квестовые, чертежи\nи служебные";advanced.SetBounds(0,68,190,48);leftBottom.Controls.Add(advanced);
   var keys=new Label{Text="Ctrl + Alt + S — меню\nCtrl + F — поиск\nEsc — скрыть окно",ForeColor=Color.Silver,Location=new Point(0,128),AutoSize=true};leftBottom.Controls.Add(keys);
   bottom=new TableLayoutPanel{Dock=DockStyle.Fill,ColumnCount=3,RowCount=6,Padding=new Padding(0,12,0,0)};
   bottom.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,100));bottom.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute,130));bottom.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute,220));
   foreach(int height in new[]{28,38,50,43,35,35})bottom.RowStyles.Add(new RowStyle(SizeType.Absolute,height));layout.Controls.Add(bottom,1,5);
   nameLabel.Dock=DockStyle.Fill;nameLabel.Font=new Font("Segoe UI",12,FontStyle.Bold);bottom.Controls.Add(nameLabel,0,0);bottom.SetColumnSpan(nameLabel,2);
   favorite.Text="☆ В избранное";Style(favorite);favorite.Dock=DockStyle.Fill;bottom.Controls.Add(favorite,2,0);
   detailTabs.Dock=DockStyle.Fill;detailTabs.AccessibleName="Карточка предмета";detailTabs.DrawMode=TabDrawMode.OwnerDrawFixed;
   detailTabs.DrawItem+=(s,e)=>{using(var brush=new SolidBrush(PanelColor))e.Graphics.FillRectangle(brush,e.Bounds);TextRenderer.DrawText(e.Graphics,detailTabs.TabPages[e.Index].Text,Font,e.Bounds,e.Index==detailTabs.SelectedIndex?Accent:TextColor,TextFormatFlags.HorizontalCenter|TextFormatFlags.VerticalCenter);};
   propertyTab.BackColor=PanelColor;descriptionTab.BackColor=PanelColor;
   foreach(var box in new[]{artifactProperties,fullDescription}){box.ReadOnly=true;box.Dock=DockStyle.Fill;box.BorderStyle=BorderStyle.None;box.BackColor=PanelColor;box.ForeColor=TextColor;box.Font=Font;box.ScrollBars=RichTextBoxScrollBars.Vertical;box.DetectUrls=false;}
   artifactProperties.AccessibleName="Свойства артефакта";fullDescription.AccessibleName="Полное описание предмета";
   propertyTab.Controls.Add(artifactProperties);descriptionTab.Controls.Add(fullDescription);
   compatibility.ReadOnly=true;compatibility.Multiline=true;compatibility.ScrollBars=ScrollBars.Vertical;compatibility.Dock=DockStyle.Fill;compatibility.BorderStyle=BorderStyle.None;compatibility.BackColor=PanelColor;compatibility.ForeColor=Accent;compatibility.AccessibleName="Совместимость предмета";propertyTab.Controls.Add(compatibility);
   detailTabs.TabPages.Add(descriptionTab);bottom.Controls.Add(detailTabs,0,1);bottom.SetColumnSpan(detailTabs,3);bottom.SetRowSpan(detailTabs,2);
   var quantityPanel=new FlowLayoutPanel{Dock=DockStyle.Fill,WrapContents=false};quantityPanel.Controls.Add(new Label{Text="Количество",AutoSize=true,Padding=new Padding(0,7,12,0)});
   quantity.Minimum=1;quantity.Maximum=999;quantity.Value=1;quantity.Width=95;quantity.BackColor=PanelColor;quantity.ForeColor=TextColor;quantity.AccessibleName="Количество предметов";quantityPanel.Controls.Add(quantity);bottom.Controls.Add(quantityPanel,0,3);
   var copy=Button("Копировать");copy.Dock=DockStyle.Fill;copy.Click+=(s,e)=>{if(Selected!=null){try{Clipboard.SetText(Domain.Command(Selected.Id,(int)quantity.Value));requestLabel.Text="Команда скопирована";}catch(ExternalException){requestLabel.Text="Буфер обмена занят. Попробуй скопировать ещё раз.";}}};bottom.Controls.Add(copy,1,3);
   spawn.Text="Выдать в инвентарь";Style(spawn);spawn.BackColor=Accent;spawn.ForeColor=Bg;spawn.Dock=DockStyle.Fill;spawn.AccessibleName="Выдать в инвентарь";bottom.Controls.Add(spawn,2,3);
   preview.ReadOnly=true;preview.Dock=DockStyle.Fill;preview.BackColor=Bg;preview.ForeColor=Color.Silver;preview.BorderStyle=BorderStyle.None;preview.Font=new Font("Consolas",10);bottom.Controls.Add(preview,0,4);bottom.SetColumnSpan(preview,3);
   requestLabel.Dock=DockStyle.Fill;requestLabel.AutoEllipsis=true;requestLabel.ForeColor=Accent;requestLabel.Text="Загрузи сохранение в игре, затем выбери предмет.";bottom.Controls.Add(requestLabel,0,5);bottom.SetColumnSpan(requestLabel,3);
   search.TextChanged+=(s,e)=>Filter();categories.SelectedIndexChanged+=(s,e)=>ChangeCategory();rarity.SelectedIndexChanged+=(s,e)=>Filter();advanced.CheckedChanged+=(s,e)=>Filter();grid.SelectionChanged+=(s,e)=>Details();quantity.ValueChanged+=(s,e)=>Details();favorite.Click+=(s,e)=>ToggleFavorite();spawn.Click+=(s,e)=>SendRequest();
   sorting.SelectedIndexChanged+=(s,e)=>Filter();sortDirection.Click+=(s,e)=>{descending=!descending;sortDirection.Text=descending?"↓ По убыванию":"↑ По возрастанию";Filter();};
   grid.ColumnHeaderMouseClick+=(s,e)=>{string[] fields={"","Название","Редкость","ID","Вес","Базовая цена","","Калибр"};if(e.ColumnIndex<1 || e.ColumnIndex>=fields.Length || !Domain.SortFields(categoryKey).Contains(fields[e.ColumnIndex]))return;string field=fields[e.ColumnIndex];if(sorting.SelectedItem as string==field){descending=!descending;sortDirection.Text=descending?"↓ По убыванию":"↑ По возрастанию";Filter();}else sorting.SelectedItem=field;};
   InitializeFeatures();
   KeyDown+=(s,e)=>{if(e.Control&&e.KeyCode==Keys.F){FeatureSearch().Focus();e.SuppressKeyPress=true;}else if(e.KeyCode==Keys.Escape){Hide();e.SuppressKeyPress=true;}};
   timer.Interval=500;timer.Tick+=(s,e)=>Pulse();if(!selfTest)timer.Start();rarity.Enabled=false;building=false;Filter();Pulse();
   Shown+=(s,e)=>{Probe(true);Pulse();if(!hotkey && !testMode)requestLabel.Text="Ctrl+Alt+S занят другим приложением. Окно доступно через панель задач.";};
   tray.Icon=SystemIcons.Application;tray.Text="Zone Spawner — Ctrl + Alt + S";tray.Visible=!selfTest;var trayMenu=new ContextMenuStrip();trayMenu.Items.Add("Открыть меню",null,(s,e)=>OpenMenu());trayMenu.Items.Add("Завершить",null,(s,e)=>{exitRequested=true;Close();});tray.ContextMenuStrip=trayMenu;tray.DoubleClick+=(s,e)=>OpenMenu();
   FormClosing+=(s,e)=>{if(!exitRequested && e.CloseReason==CloseReason.UserClosing){e.Cancel=true;Hide();}};
   FormClosed+=(s,e)=>{timer.Stop();tray.Visible=false;tray.Dispose();if(gameLifetime!=null)gameLifetime.Dispose();};
  }
  static Button Button(string text) {var b=new Button{Text=text};Style(b);return b;}
  static void Style(Button b) {b.FlatStyle=FlatStyle.Flat;b.FlatAppearance.BorderSize=0;b.BackColor=Color.FromArgb(49,59,46);b.ForeColor=TextColor;b.Cursor=Cursors.Hand;}
  void ChangeCategory(){if(building)return;categorySort[categoryKey]=sorting.SelectedItem as string;categoryDescending[categoryKey]=descending;building=true;categoryKey=categories.SelectedItem as string;categoryTitle.Text=categoryKey;bool artifacts=categoryKey=="Артефакты";artifactFilters.Visible=artifacts;rarity.Enabled=artifacts;if(!artifacts)rarity.SelectedIndex=0;sorting.Items.Clear();sorting.Items.AddRange(Domain.SortFields(categoryKey));string saved;sorting.SelectedItem=categorySort.TryGetValue(categoryKey,out saved)&&sorting.Items.Contains(saved)?saved:"Название";bool savedDirection;descending=categoryDescending.TryGetValue(categoryKey,out savedDirection)&&savedDirection;sortDirection.Text=descending?"↓ По убыванию":"↑ По возрастанию";grid.Columns[2].Visible=artifacts;grid.Columns[6].Visible=categoryKey=="Обвесы"||categoryKey=="Патроны";grid.Columns[7].Visible=categoryKey=="Оружие"||categoryKey=="Патроны";float[] widths={4,32,16,32,11,18,42,16};for(int n=0;n<widths.Length;n++)grid.Columns[n].FillWeight=widths[n];building=false;Filter();}
  void Filter() {
   if(building)return;string selectedId=Selected==null?null:Selected.Id;string c=categories.SelectedItem as string;
   visible=Domain.FilterCatalog(catalog,c,search.Text,rarity.SelectedItem as string,sorting.SelectedItem as string,descending,advanced.Checked,favorites);
   grid.SuspendLayout();grid.Rows.Clear();foreach(var i in visible){string compatible=String.Join(", ",i.CompatibleNames??new string[0]);int index=grid.Rows.Add(favorites.Contains(i.Id)?"★":"",i.Name,i.Rarity,i.Id,i.Weight.HasValue?i.Weight.Value.ToString("0.###",CultureInfo.GetCultureInfo("ru-RU")):"—",i.Cost.HasValue?i.Cost.Value.ToString("N0",CultureInfo.GetCultureInfo("ru-RU")):"—",i.Category=="Патроны"&&compatible==""?"Не указано":compatible,String.IsNullOrEmpty(i.Caliber)?"—":i.Caliber);grid.Rows[index].Tag=i;grid.Rows[index].Cells[2].Style.ForeColor=RarityColor(i.Rarity);grid.Rows[index].Cells[3].ToolTipText=i.Id;grid.Rows[index].Cells[6].ToolTipText=CompatibilityText(i);grid.Rows[index].Cells[7].ToolTipText=String.IsNullOrEmpty(i.Caliber)?"Калибр не указан для этого предмета.":"Базовый калибр: "+i.Caliber+". Переоборудование у техника может изменить калибр оружия.";}
   string[] sortFields={"","Название","Редкость","ID","Вес","Базовая цена","","Калибр"};for(int n=1;n<sortFields.Length;n++)grid.Columns[n].HeaderCell.SortGlyphDirection=sortFields[n]==sorting.SelectedItem as string?(descending?SortOrder.Descending:SortOrder.Ascending):SortOrder.None;
   if(grid.Rows.Count>0){var row=grid.Rows.Cast<DataGridViewRow>().FirstOrDefault(x=>((Item)x.Tag).Id==selectedId)??grid.Rows[0];grid.CurrentCell=row.Cells[1];}
   grid.ResumeLayout();countLabel.Text="Найдено: "+visible.Count+"\nВсего в каталоге: "+catalog.Count;Details();
  }
  static Color RarityColor(string rarity){switch(rarity){case "Необычный":return Color.FromArgb(144,207,133);case "Редкий":return Color.FromArgb(130,185,244);case "Легендарный":return Color.FromArgb(238,194,97);default:return TextColor;}}
  static string CompatibilityText(Item i){if(i==null)return "";if(i.CompatibleIds==null || i.CompatibleIds.Length==0)return i.Category=="Обвесы"||i.Category=="Патроны"?"Совместимость не указана в конфигурации игры.":"";string title=i.Category=="Патроны"?"Калибр: "+i.Caliber+"\r\nСовместимое оружие и гранатомёты:\r\n":"Совместимо: ";return title+String.Join(i.Category=="Патроны"?"\r\n":"; ",i.CompatibleIds.Select((id,n)=>(i.CompatibleNames!=null&&n<i.CompatibleNames.Length?i.CompatibleNames[n]:id)+" ["+id+"]"))+(i.Category=="Обвесы"?"\r\nДля установки может требоваться крепление или улучшение у техника.":i.Category=="Патроны"?"\r\nБазовая конфигурация. После смены калибра у техника совместимость может измениться.":"");}
  void Details() {
   var i=Selected;nameLabel.Text=i==null?"Ничего не найдено":i.Name;
   string nextId=i==null?"":i.Id;
   if(detailItemId!=nextId || detailTabs.TabPages.Count==0){
    detailItemId=nextId;bool artifact=i!=null && i.Category=="Артефакты",fitting=i!=null && (i.Category=="Обвесы"||i.Category=="Патроны");
    detailTabs.TabPages.Clear();artifactProperties.Visible=artifact;compatibility.Visible=fitting;
    if(artifact || fitting){propertyTab.Text=artifact?"Свойства":"Совместимость";detailTabs.TabPages.Add(propertyTab);}
    detailTabs.TabPages.Add(descriptionTab);detailTabs.SelectedIndex=0;
    fullDescription.Text=i==null?"Попробуй другое название или ID.":(i.Restricted?"Квестовый / служебный предмет.\n\n":"")+i.Description;
    compatibility.Text=CompatibilityText(i);artifactProperties.Text=Domain.ArtifactText(i);
    for(int n=0;n<artifactProperties.Lines.Length;n++){string line=artifactProperties.Lines[n];int start=artifactProperties.GetFirstCharIndexFromLine(n);if(start<0)continue;artifactProperties.Select(start,line.Length);artifactProperties.SelectionColor=line.StartsWith("+ ")?Color.FromArgb(144,207,133):line.StartsWith("− ")?Color.FromArgb(243,157,130):TextColor;}
    artifactProperties.Select(0,0);artifactProperties.ScrollToCaret();
    layout.RowStyles[5].Height=artifact?336:252;bottom.RowStyles[1].Height=artifact?24:38;bottom.RowStyles[2].Height=artifact?146:50;
   }
   preview.Text=i==null?"":Domain.Command(i.Id,(int)quantity.Value);favorite.Enabled=i!=null;favorite.Text=i!=null&&favorites.Contains(i.Id)?"★ Убрать":"☆ В избранное";spawn.Enabled=i!=null&&ready&&pending==""&&!FeaturePending;
  }
  void ToggleFavorite() {var i=Selected;if(i==null)return;bool adding=!favorites.Contains(i.Id);if(adding)favorites.Add(i.Id);else favorites.Remove(i.Id);try{Domain.Atomic(Path.Combine(root,"favorites.txt"),String.Join("\n",favorites.OrderBy(x=>x))+"\n");Filter();}catch(IOException){if(adding)favorites.Remove(i.Id);else favorites.Add(i.Id);requestLabel.Text="Не удалось сохранить избранное. Проверь доступ к папке меню.";}catch(UnauthorizedAccessException){if(adding)favorites.Remove(i.Id);else favorites.Add(i.Id);requestLabel.Text="Нет доступа к файлу избранного.";}}
  void Pulse() {
   if((!Visible || WindowState==FormWindowState.Minimized) && pending=="" && !FeaturePending)return;
   Probe(false);
   ready=false;string previousToken=token;
   try {var file=Path.Combine(bridge,"status.txt");if(File.Exists(file)) {var parts=File.ReadAllText(file).Trim().Split('\t');long stamp;
    if(parts.Length>=3&&Regex.IsMatch(parts[0],@"\A[A-Za-z0-9_-]+\z")&&long.TryParse(parts[2],out stamp)&&Domain.Now()-stamp<=4&&stamp<=Domain.Now()+2){token=parts[0];ready=parts[1]=="ready";}
   }}catch(IOException){}
   status.Text=ready?"● Игра подключена\nПерсонаж готов к выдаче предметов":"○ Ожидание игры\nНужны UE4SS и загруженное сохранение";status.ForeColor=ready?Accent:Color.Silver;
   if(previousToken!=token)InvalidateFeatureSession();
   if(pending!="") {
    try {string ack=Path.Combine(bridge,"response.txt");if(File.Exists(ack)){var p=File.ReadAllText(ack).Trim().Split('\t');if(p.Length>=2&&p[0]==pending){requestLabel.Text=p[1]=="submitted"?"Команда выдана игре. Проверь предмет в инвентаре.":"Выдача отклонена: "+(p.Length>2?p[2]:p[1]);pending="";FeatureSpawnCompleted(p[1]=="submitted",requestLabel.Text);}}}catch(IOException){}
    if(pending!=""&&Domain.Now()-sentAt>15){requestLabel.Text="Нет ответа от игры. Запрос истёк; автоматического повтора нет.";pending="";FeatureSpawnCompleted(false,requestLabel.Text);}
   }
   FeaturePulse();Details();
  }
  void SendRequest() {
   SendItem(Selected,(int)quantity.Value,false);
  }
  void Probe(bool force){
   if(!Visible || WindowState==FormWindowState.Minimized)return;
   long now=Domain.Now();if(!force && now-lastProbe<2)return;
   try{Domain.Atomic(Path.Combine(bridge,"probe.txt"),Guid.NewGuid().ToString("N")+"\t"+now+"\n");lastProbe=now;}
   catch(IOException){requestLabel.Text="Не удалось проверить связь с игрой.";}
   catch(UnauthorizedAccessException){requestLabel.Text="Нет доступа к папке связи с игрой.";}
  }
  void OpenMenu(){Show();WindowState=FormWindowState.Normal;Activate();FeatureSearch().Focus();Probe(true);Pulse();}
  public void BindGame(Process process){gameLifetime=new GameLifetime(process,GameExited);}
  void GameExited(){
   gameEnded=true;
   try{if(IsHandleCreated && !IsDisposed)BeginInvoke((Action)ExitWithGame);}catch(InvalidOperationException){}
  }
  void ExitWithGame(){if(IsDisposed)return;exitRequested=true;Close();}
  protected override void SetVisibleCore(bool value){
   if(value && suppressInitialShow){suppressInitialShow=false;value=false;if(!IsHandleCreated)CreateHandle();}
   base.SetVisibleCore(value);
  }
  protected override void OnHandleCreated(EventArgs e){
   base.OnHandleCreated(e);
   if(!testMode)hotkey=RegisterHotKey(Handle,1,0x4003,(uint)Keys.S);
   if(gameEnded)BeginInvoke((Action)ExitWithGame);
  }
  protected override void OnHandleDestroyed(EventArgs e){
   if(hotkey){UnregisterHotKey(Handle,1);hotkey=false;}
   base.OnHandleDestroyed(e);
  }
  protected override void WndProc(ref Message m){if(m.Msg==0x0312&&m.WParam.ToInt32()==1){if(Visible && WindowState!=FormWindowState.Minimized)Hide();else OpenMenu();}base.WndProc(ref m);}
  public static void ReviewSelfTest(string root){
   FeatureData.SelfTest(root);
   FeatureViewSelfTest(root);
   using(var form=new SpawnForm(root,true)){
    form.Pulse();if(form.lastProbe!=0)throw new Exception("Hidden menu sends readiness probes");
    if(form.rarity.Parent!=form.artifactFilters || form.artifactFilters.Parent.AccessibleName!="Параметры выбранной категории")throw new Exception("Rarity filter outside category panel");
    form.categories.SelectedItem="Артефакты";form.rarity.SelectedItem="Легендарный";form.sorting.SelectedItem="Редкость";form.descending=true;form.Filter();
    if(!form.grid.Columns[2].Visible || form.visible.Count<4 || form.visible.Any(x=>x.Rarity!="Легендарный"))throw new Exception("Artifact view");
    form.categories.SelectedItem="Оружие";
    if(form.rarity.Enabled || form.grid.Columns[2].Visible || form.sorting.Items.Contains("Редкость") || form.sorting.SelectedItem as string!="Название" || form.descending || form.visible.Any(x=>x.Category!="Оружие") || form.visible.Count==0)throw new Exception("Weapon view inherits artifact filters");
    form.sorting.SelectedItem="Вес";form.categories.SelectedItem="Артефакты";
    if(form.sorting.SelectedItem as string!="Редкость" || !form.descending)throw new Exception("Artifact sort restoration");
    form.categories.SelectedItem="Оружие";
    if(form.sorting.SelectedItem as string!="Вес" || form.descending)throw new Exception("Weapon sort restoration");
    if(!form.grid.Columns[7].Visible||form.grid.Columns[6].Visible||!form.sorting.Items.Contains("Калибр"))throw new Exception("Weapon caliber column scope");
    form.search.Text="Gun_RatKiller_SMG";if(form.grid.Rows.Count!=1||form.grid.Rows[0].Cells[7].Value as string!="9x18")throw new Exception("Weapon caliber cell");
    form.search.Clear();form.sorting.SelectedItem="Калибр";form.categories.SelectedItem="Патроны";form.search.Text="Крысобой";
    if(!form.grid.Columns[6].Visible||!form.grid.Columns[7].Visible||form.visible.Count==0||form.visible.Any(x=>!x.CompatibleIds.Contains("Gun_RatKiller_SMG"))||form.detailTabs.TabPages[0].Text!="Совместимость"||!form.compatibility.Text.Contains("9x18"))throw new Exception("Ammo compatibility column, search or card");
    form.search.Clear();form.search.Text="A762D";if(form.visible.Count!=1||!form.compatibility.Text.Contains("не указана"))throw new Exception("Unknown ammo compatibility not explained");
    form.search.Clear();form.categories.SelectedItem="Оружие";if(form.sorting.SelectedItem as string!="Калибр")throw new Exception("Caliber sorting restoration");
    form.categories.SelectedItem="Артефакты";if(form.grid.Columns[6].Visible||form.grid.Columns[7].Visible||form.sorting.Items.Contains("Калибр"))throw new Exception("Caliber leaked into artifacts");
    form.categories.SelectedItem="Обвесы";form.search.Text="GunAK74_ST";
    if(!form.grid.Columns[6].Visible || form.visible.Count==0 || form.visible.Any(x=>x.CompatibleIds==null || !x.CompatibleIds.Contains("GunAK74_ST")))throw new Exception("Attachment compatibility search");
    form.quantity.Value=5;form.Details();if(!form.preview.Text.EndsWith(" 0 5 1"))throw new Exception("Quantity preview");
    form.search.Text="__there_is_no_such_item__";if(form.visible.Count!=0 || form.spawn.Enabled)throw new Exception("Empty search state");
    if(form.quantity.Minimum!=1 || form.quantity.Maximum!=999)throw new Exception("Quantity controls");
    form.search.Clear();form.categories.SelectedItem="Артефакты";form.rarity.SelectedItem="Любая редкость";form.search.Text="GArtifactCompass";form.Details();
    if(form.detailTabs.TabPages.Count!=2 || form.detailTabs.TabPages[0].Text!="Свойства" || !form.artifactProperties.Text.Contains("− Радиация: Максимально"))throw new Exception("Artifact property card");
    int radiation=form.artifactProperties.Text.IndexOf("− Радиация");form.artifactProperties.Select(radiation,1);
    if(form.artifactProperties.SelectionColor!=Color.FromArgb(243,157,130))throw new Exception("Harmful artifact effect is not marked");
    form.detailTabs.SelectedTab=form.descriptionTab;form.pending="tab_retention_check";form.sentAt=Domain.Now();form.Pulse();form.pending="";
    if(form.detailTabs.SelectedTab!=form.descriptionTab || form.fullDescription.Text.Replace("\r\n","\n")!=form.Selected.Description.Replace("\r\n","\n"))throw new Exception("Full description or selected tab changes during status refresh");
    form.search.Text="AArtifactWeirdFlower";form.Details();if(!form.artifactProperties.Text.Contains("Фиксированные эффекты") || form.fullDescription.Text.Replace("\r\n","\n")!=form.Selected.Description.Replace("\r\n","\n"))throw new Exception("Special artifact missing data or full description");
    form.search.Clear();form.categories.SelectedItem="Обвесы";form.search.Text="RU_X4Scope_1";
    if(form.detailTabs.TabPages[0].Text!="Совместимость" || !form.compatibility.Text.Contains("GunAK74_ST"))throw new Exception("Attachment card lost compatibility");
    form.search.Clear();form.categories.SelectedItem="Оружие";
    if(form.detailTabs.TabPages.Count!=1 || form.detailTabs.TabPages[0].Text!="Описание" || form.artifactProperties.Text!="")throw new Exception("Artifact card leaked into non-artifact items");
    form.timer.Dispose();form.tray.Dispose();
   }
   Console.WriteLine("PASS: category placement, artifact-only controls, independent sorting, weapon calibers, ammunition compatibility, quantity and empty results in the WinForms view");
   Console.WriteLine("PASS: artifact effects, harmful-effect color, full description, tab retention, missing special effects and attachment card");
   using(var form=new SpawnForm(root,true,true)){
    form.Show();form.Pulse();
    if(form.Visible || !form.IsHandleCreated || form.suppressInitialShow || form.lastProbe!=0)throw new Exception("Hidden startup does not initialize a window handle or sends a probe");
    form.timer.Dispose();form.tray.Dispose();
   }
   Console.WriteLine("PASS: hidden startup creates a message handle without showing the menu or polling the game");
  }
  public static void StartupSelfTest(string root){
   using(var form=new SpawnForm(root,false,true)){
    form.Show();
    try{
     if(form.Visible || !form.IsHandleCreated || !form.hotkey || form.lastProbe!=0)throw new Exception("Hidden startup or native Ctrl+Alt+S registration failed");
     Console.WriteLine("PASS: hidden menu successfully registered native Ctrl+Alt+S");
    }finally{form.timer.Stop();form.timer.Dispose();form.tray.Visible=false;form.tray.Dispose();}
   }
  }
  public static void LifetimeSelfTest(string root,Process process){
   using(var form=new SpawnForm(root,true,true))using(var deadline=new System.Windows.Forms.Timer()){
    bool timedOut=false;deadline.Interval=5000;deadline.Tick+=(s,e)=>{timedOut=true;form.ExitWithGame();};
    form.BindGame(process);deadline.Start();Application.Run(form);deadline.Stop();
    if(timedOut || !form.gameEnded || !form.exitRequested || form.lastProbe!=0)throw new Exception("Menu failed to exit with its owner or polled the game");
   }
   Console.WriteLine("PASS: hidden menu exits with its owner process without polling");
  }
 }
 public static class Program {
  sealed class Installation {public string GameRoot {get;set;}}
  static Process OpenGame(string root,int pid){
   var installation=new JavaScriptSerializer().Deserialize<Installation>(File.ReadAllText(Path.Combine(root,"installed-manifest.json")));
   string expected=Path.GetFullPath(Path.Combine(installation.GameRoot,@"Stalker2\Binaries\Win64\Stalker2-Win64-Shipping.exe"));
   Process process=Process.GetProcessById(pid);
   try{if(process.HasExited || !String.Equals(process.MainModule.FileName,expected,StringComparison.OrdinalIgnoreCase))throw new ArgumentException("Game process does not match the installed game");return process;}
   catch{process.Dispose();throw;}
  }
  [STAThread] public static int Main(string[] args) {
   string root=AppDomain.CurrentDomain.BaseDirectory;
   if(args.Contains("--self-test")){Domain.SelfTest(root);return 0;}
   bool created;using(var mutex=new System.Threading.Mutex(true,"Local\\ZoneSpawnerMenu",out created)){
    if(!created){if(!args.Contains("--start-hidden"))MessageBox.Show("Zone Spawner уже запущен. Нажми Ctrl+Alt+S.");return 0;}
    try{
     Process game=null;int index=Array.IndexOf(args,"--game-pid");
     if(index>=0){int pid;if(index+1>=args.Length || !Int32.TryParse(args[index+1],out pid) || pid<=0)throw new ArgumentException("Invalid game PID");game=OpenGame(root,pid);}
     Application.EnableVisualStyles();Application.SetCompatibleTextRenderingDefault(false);
     using(var form=new SpawnForm(root,false,args.Contains("--start-hidden"))){if(game!=null)form.BindGame(game);Application.Run(form);}return 0;
    }
    catch(Exception e){if(args.Contains("--start-hidden")){try{Domain.Atomic(Path.Combine(root,"bridge","menu-error.txt"),e.Message+"\n");}catch{} }else MessageBox.Show(e.Message,"Zone Spawner");return 1;}
   }
  }
 }
}
