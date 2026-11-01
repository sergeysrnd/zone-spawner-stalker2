using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.RegularExpressions;
using System.Web.Script.Serialization;
using System.Windows.Forms;

namespace ZoneSpawner {
 public sealed class DoorCodeSource {public string Title {get;set;}public string Url {get;set;}}
 public sealed class DoorCodeEntry {
  public string Id {get;set;}public string Region {get;set;}public string Location {get;set;}public string Name {get;set;}
  public string Type {get;set;}public string Context {get;set;}public string Code {get;set;}public string Hint {get;set;}public string Note {get;set;}
  public List<string> Aliases {get;set;}public List<DoorCodeSource> Sources {get;set;}
 }
 public static class DoorCodeData {
  public static List<DoorCodeEntry> Load(string root){
   string path=Path.Combine(root,"door-codes.json");if(!File.Exists(path))throw new FileNotFoundException("Рядом с меню нет door-codes.json.");
   var rows=new JavaScriptSerializer().Deserialize<List<DoorCodeEntry>>(File.ReadAllText(path,Encoding.UTF8));Validate(rows);return rows;
  }
  public static void Validate(List<DoorCodeEntry> rows){
   if(rows==null||rows.Count==0||rows.Count>500)throw new FormatException("Некорректный размер справочника кодов.");
   var ids=new HashSet<string>(StringComparer.Ordinal);
   foreach(var row in rows){
    if(row==null||!Regex.IsMatch(row.Id??"",@"\A[a-z0-9_-]{1,100}\z")||!ids.Add(row.Id)||String.IsNullOrWhiteSpace(row.Region)||String.IsNullOrWhiteSpace(row.Location)||String.IsNullOrWhiteSpace(row.Name)||String.IsNullOrWhiteSpace(row.Hint))throw new FormatException("Неполная запись или повторный ID замка.");
    if(!new[]{"Дверь","Сейф","Контейнер","Замок"}.Contains(row.Type)||!new[]{"Тайник","Сюжет"}.Contains(row.Context))throw new FormatException("Неизвестный тип или контекст замка.");
    if(row.Code!=null&&!Regex.IsMatch(row.Code,@"\A[0-9]{3,12}\z"))throw new FormatException("Код должен содержать только цифры; нули сохраняются.");
    if(row.Code==null&&String.IsNullOrWhiteSpace(row.Note))throw new FormatException("У переменного кода нет пояснения.");
    if(row.Sources==null||row.Sources.Count==0||row.Sources.Count>8)throw new FormatException("У кода не указан источник.");
    foreach(var source in row.Sources){Uri uri;if(source==null||String.IsNullOrWhiteSpace(source.Title)||!Uri.TryCreate(source.Url,UriKind.Absolute,out uri)||uri.Scheme!="https"||String.IsNullOrEmpty(uri.Host))throw new FormatException("Некорректная ссылка источника.");}
    if(row.Aliases==null)row.Aliases=new List<string>();
   }
  }
  public static bool Matches(DoorCodeEntry row,string text,string region,string type,bool storyOnly,bool codesVisible){
   if(region!="Все регионы"&&row.Region!=region||type!="Все замки"&&row.Type!=type||storyOnly&&row.Context!="Сюжет")return false;
   // Hidden codes are deliberately absent from search, hints and tooltips.
   string content=row.Region+" "+row.Location+" "+row.Name+" "+row.Type+" "+row.Context+" "+row.Hint+" "+row.Note+" "+String.Join(" ",row.Aliases)+(codesVisible?" "+row.Code:"");
   return Domain.Fold(content).Contains(Domain.Fold(text));
  }
  public static string Details(DoorCodeEntry row){
   if(row==null)return "Ничего не найдено. Измени поиск или фильтры.";
   var text=new StringBuilder(row.Location+" · "+row.Region+"\r\n"+row.Name+" · "+row.Type+" · "+row.Context+"\r\n\r\n"+row.Hint);
   if(!String.IsNullOrWhiteSpace(row.Note))text.Append("\r\n\r\n"+row.Note);
   text.Append("\r\n\r\nИсточники: "+String.Join("; ",row.Sources.Select(s=>s.Title+" — "+s.Url)));
   return text.ToString();
  }
 }
 public sealed partial class SpawnForm {
  readonly TabPage doorCodesPage=new TabPage("Коды дверей");readonly DataGridView doorCodesGrid=new DataGridView();
  readonly TextBox doorCodeSearch=new TextBox(),doorCodeValue=new TextBox(),doorCodeDetails=new TextBox();readonly ComboBox doorCodeRegion=new ComboBox(),doorCodeType=new ComboBox();
  readonly CheckBox showDoorCodes=new CheckBox(),storyDoorCodes=new CheckBox();readonly Label doorCodeCount=new Label(),doorCodeStatus=new Label();readonly Button revealDoorCode=new Button(),copyDoorCode=new Button();
  List<DoorCodeEntry> doorCodes=new List<DoorCodeEntry>();bool filteringDoorCodes;string revealedDoorId="",selectedDoorId="";
  Action<string> doorCodeClipboard=text=>Clipboard.SetText(text);
  DoorCodeEntry DoorSelected{get{return doorCodesGrid.CurrentRow==null?null:doorCodesGrid.CurrentRow.Tag as DoorCodeEntry;}}
  bool DoorCodeVisible{get{var row=DoorSelected;return row!=null&&row.Code!=null&&(showDoorCodes.Checked||revealedDoorId==row.Id);}}
  void BuildDoorCodes(){
   string failure="";try{doorCodes=DoorCodeData.Load(root);}catch(Exception e){failure="Справочник недоступен: "+e.Message;}
   var t=FeatureLayout(doorCodesPage,4);t.RowStyles.Add(new RowStyle(SizeType.Absolute,58));t.RowStyles.Add(new RowStyle(SizeType.Absolute,90));t.RowStyles.Add(new RowStyle(SizeType.Percent,100));t.RowStyles.Add(new RowStyle(SizeType.Absolute,190));
   FeatureLabel(doorCodeStatus,"Коды дверей и сейфов. Выбери место и нажми «Показать код». Справочник работает без подключения к игре.");t.Controls.Add(doorCodeStatus,0,0);
   var filters=Flow();filters.Controls.Add(Caption("Поиск"));FeatureText(doorCodeSearch,"Поиск двери, сейфа или локации");filters.Controls.Add(doorCodeSearch);
   foreach(var combo in new[]{doorCodeRegion,doorCodeType}){combo.DropDownStyle=ComboBoxStyle.DropDownList;combo.Width=190;combo.BackColor=PanelColor;combo.ForeColor=TextColor;}
   doorCodeRegion.AccessibleName="Регион кодового замка";doorCodeRegion.Items.Add("Все регионы");doorCodeRegion.Items.AddRange(doorCodes.Select(d=>d.Region).Distinct().OrderBy(x=>x).ToArray());doorCodeRegion.SelectedIndex=0;filters.Controls.Add(doorCodeRegion);
   doorCodeType.AccessibleName="Тип кодового замка";doorCodeType.Items.Add("Все замки");doorCodeType.Items.AddRange(doorCodes.Select(d=>d.Type).Distinct().OrderBy(x=>x).ToArray());doorCodeType.SelectedIndex=0;filters.Controls.Add(doorCodeType);
   foreach(var box in new[]{storyDoorCodes,showDoorCodes}){box.AutoSize=true;box.Padding=new Padding(10,8,0,0);filters.Controls.Add(box);}storyDoorCodes.Text="Только сюжетные";showDoorCodes.Text="Показывать все коды";doorCodeCount.AutoSize=true;doorCodeCount.Padding=new Padding(10,8,0,0);filters.Controls.Add(doorCodeCount);t.Controls.Add(filters,0,1);
   FeatureGrid(doorCodesGrid);doorCodesGrid.AccessibleName="Справочник кодов дверей и сейфов";doorCodesGrid.Columns.Add("Region","Регион");doorCodesGrid.Columns.Add("Location","Локация");doorCodesGrid.Columns.Add("Name","Дверь / сейф");doorCodesGrid.Columns.Add("Type","Тип");doorCodesGrid.Columns.Add("Context","Контекст");doorCodesGrid.Columns.Add("Code","Код");
   var weights=new[]{80,125,150,60,60,75};for(int i=0;i<weights.Length;i++)doorCodesGrid.Columns[i].FillWeight=weights[i];t.Controls.Add(doorCodesGrid,0,2);
   var card=new TableLayoutPanel{Dock=DockStyle.Fill,ColumnCount=2,RowCount=1,Padding=new Padding(0,10,0,0)};card.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute,320));card.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,100));
   var codePane=new FlowLayoutPanel{Dock=DockStyle.Fill,FlowDirection=FlowDirection.TopDown,WrapContents=false};doorCodeValue.ReadOnly=true;doorCodeValue.Width=295;doorCodeValue.Font=new Font("Segoe UI",23,FontStyle.Bold);doorCodeValue.BackColor=PanelColor;doorCodeValue.ForeColor=Accent;doorCodeValue.BorderStyle=BorderStyle.None;doorCodeValue.AccessibleName="Код выбранного замка";codePane.Controls.Add(doorCodeValue);
   FeatureButton(revealDoorCode,"Показать код",295);FeatureButton(copyDoorCode,"Скопировать код",295);codePane.Controls.Add(revealDoorCode);codePane.Controls.Add(copyDoorCode);card.Controls.Add(codePane,0,0);
   doorCodeDetails.Dock=DockStyle.Fill;doorCodeDetails.ReadOnly=true;doorCodeDetails.Multiline=true;doorCodeDetails.ScrollBars=ScrollBars.Vertical;doorCodeDetails.BackColor=PanelColor;doorCodeDetails.ForeColor=TextColor;doorCodeDetails.BorderStyle=BorderStyle.None;doorCodeDetails.AccessibleName="Где применить код и условия доступа";card.Controls.Add(doorCodeDetails,1,0);t.Controls.Add(card,0,3);
   doorCodeSearch.TextChanged+=(s,e)=>FilterDoorCodes();doorCodeRegion.SelectedIndexChanged+=(s,e)=>FilterDoorCodes();doorCodeType.SelectedIndexChanged+=(s,e)=>FilterDoorCodes();storyDoorCodes.CheckedChanged+=(s,e)=>FilterDoorCodes();showDoorCodes.CheckedChanged+=(s,e)=>{revealedDoorId="";FilterDoorCodes();};
   doorCodesGrid.CurrentCellChanged+=(s,e)=>{if(!filteringDoorCodes)RenderDoorCode();};revealDoorCode.Click+=(s,e)=>{var row=DoorSelected;if(row==null||row.Code==null)return;revealedDoorId=revealedDoorId==row.Id?"":row.Id;RenderDoorCode();};copyDoorCode.Click+=(s,e)=>CopyDoorCode();
   FilterDoorCodes();if(failure!="")doorCodeStatus.Text=failure;
  }
  void FilterDoorCodes(){
   string id=DoorSelected==null?null:DoorSelected.Id;filteringDoorCodes=true;
   try{doorCodesGrid.Rows.Clear();foreach(var row in doorCodes.Where(d=>DoorCodeData.Matches(d,doorCodeSearch.Text,doorCodeRegion.SelectedItem as string,doorCodeType.SelectedItem as string,storyDoorCodes.Checked,showDoorCodes.Checked)).OrderBy(d=>d.Region).ThenBy(d=>d.Location).ThenBy(d=>d.Name)){
    int n=doorCodesGrid.Rows.Add(row.Region,row.Location,row.Name,row.Type,row.Context,row.Code==null?"Переменный":showDoorCodes.Checked?row.Code:"Скрыт");doorCodesGrid.Rows[n].Tag=row;
   }if(doorCodesGrid.Rows.Count>0){var row=doorCodesGrid.Rows.Cast<DataGridViewRow>().FirstOrDefault(r=>((DoorCodeEntry)r.Tag).Id==id)??doorCodesGrid.Rows[0];doorCodesGrid.CurrentCell=row.Cells[0];}
   }finally{filteringDoorCodes=false;}doorCodeCount.Text=doorCodesGrid.Rows.Count+" / "+doorCodes.Count;RenderDoorCode();
  }
  void RenderDoorCode(){
   var row=DoorSelected;string id=row==null?"":row.Id;if(id!=selectedDoorId){revealedDoorId="";selectedDoorId=id;}
   doorCodeValue.Text=row==null?"—":row.Code==null?"Индивидуальный":DoorCodeVisible?row.Code:"••••";doorCodeDetails.Text=DoorCodeData.Details(row);
   revealDoorCode.Enabled=row!=null&&row.Code!=null&&!showDoorCodes.Checked;revealDoorCode.Text=showDoorCodes.Checked?"Все коды показаны":DoorCodeVisible?"Скрыть код":"Показать код";copyDoorCode.Enabled=DoorCodeVisible;
  }
  void CopyDoorCode(){
   var row=DoorSelected;if(!DoorCodeVisible||row==null)return;
   try{doorCodeClipboard(row.Code);doorCodeStatus.Text="Код скопирован: "+row.Location+" · "+row.Name+". Введи его на игровой панели замка.";}
   catch(ExternalException){doorCodeStatus.Text="Буфер обмена занят. Повтори копирование или выдели код вручную.";}
   catch(System.Threading.ThreadStateException){doorCodeStatus.Text="Копирование недоступно. Выдели код и скопируй вручную.";}
  }
  static void DoorCodesSelfTest(string root){
   var data=DoorCodeData.Load(root);if(data.Count!=27||data.Select(d=>d.Region).Distinct().Count()!=16)throw new Exception("Door reference coverage");
   if(data.Single(d=>d.Id=="waste-monolith").Code!="0505"||data.Single(d=>d.Id=="intercom-tower-safe").Code!="030794")throw new Exception("Door code leading zeros lost");
   string temp=Path.Combine(Path.GetTempPath(),"ZoneSpawner-doors-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(temp);
   foreach(var name in new[]{"items.json","actors.json","locations.json","door-codes.json"})File.Copy(Path.Combine(root,name),Path.Combine(temp,name));
   try{
    using(var f=new SpawnForm(temp,true)){
     f.Show();f.mainTabs.SelectedTab=f.doorCodesPage;string copied=null;f.doorCodeClipboard=text=>copied=text;
     if(f.doorCodesGrid.Rows.Count!=27||f.copyDoorCode.Enabled||f.showDoorCodes.Checked||f.FeatureSearch()!=f.doorCodeSearch)throw new Exception("Door code default visibility/search scope");
     f.doorCodeSearch.Text="Shift Change";if(f.doorCodesGrid.Rows.Count!=1||f.DoorSelected.Id!="waste-monolith"||f.doorCodeValue.Text.Contains("0505"))throw new Exception("Door name search or hidden value");
     f.copyDoorCode.PerformClick();if(copied!=null)throw new Exception("Hidden code copied");
     f.revealDoorCode.PerformClick();if(f.doorCodeValue.Text!="0505"||!f.copyDoorCode.Enabled)throw new Exception("Selected code not revealed");f.copyDoorCode.PerformClick();if(copied!="0505")throw new Exception("Copy lost leading zero");
     f.doorCodeSearch.Text="030794";if(f.doorCodesGrid.Rows.Count!=0||f.copyDoorCode.Enabled||f.revealDoorCode.Enabled)throw new Exception("Hidden codes searchable or stale selection enabled");
     f.showDoorCodes.Checked=true;if(f.doorCodesGrid.Rows.Count!=1||f.DoorSelected.Id!="intercom-tower-safe"||f.doorCodeValue.Text!="030794")throw new Exception("Visible code search");f.copyDoorCode.PerformClick();if(copied!="030794")throw new Exception("Six-digit copy changed string");
     f.showDoorCodes.Checked=false;f.doorCodeSearch.Text="Малахит";if(f.doorCodesGrid.Rows.Count!=3||f.copyDoorCode.Enabled)throw new Exception("Region search or hide-all state");
     f.doorCodeType.SelectedItem="Сейф";if(f.doorCodesGrid.Rows.Count!=1||f.DoorSelected.Code!="0430")throw new Exception("Door and safe code conflated");f.doorCodeType.SelectedIndex=0;
     f.doorCodeSearch.Clear();f.doorCodeRegion.SelectedItem="Химзавод";f.storyDoorCodes.Checked=true;if(f.doorCodesGrid.Rows.Count!=1||f.DoorSelected.Id!="promin-lab")throw new Exception("Door region/story filters");
     f.revealDoorCode.PerformClick();f.doorCodeSearch.Text="no such door";if(f.copyDoorCode.Enabled||f.DoorSelected!=null||f.doorCodeValue.Text!="—")throw new Exception("Empty door search retained previous code");
     f.doorCodeRegion.SelectedIndex=0;f.storyDoorCodes.Checked=false;f.doorCodeSearch.Text="Сфера";f.revealDoorCode.PerformClick();f.doorCodeClipboard=text=>{throw new ExternalException("busy");};f.copyDoorCode.PerformClick();if(!f.doorCodeStatus.Text.Contains("занят"))throw new Exception("Clipboard busy error escapes UI");
     var variable=new DoorCodeEntry{Id="test-variable",Region="Тест",Location="Личный замок",Name="Код из сохранения",Type="Замок",Context="Сюжет",Code=null,Hint="Узнай комбинацию в своём сохранении.",Note="Индивидуальный код.",Sources=data[0].Sources,Aliases=new List<string>()};DoorCodeData.Validate(new List<DoorCodeEntry>{variable});f.doorCodes.Add(variable);f.doorCodeSearch.Text="Личный замок";f.showDoorCodes.Checked=true;if(f.copyDoorCode.Enabled||f.revealDoorCode.Enabled)throw new Exception("Variable code enabled copy or reveal");
     f.doorCodeSearch.Text="Shift Change";f.showDoorCodes.Checked=false;f.revealDoorCode.PerformClick();f.doorCodeSearch.Text="Сфера";if(f.copyDoorCode.Enabled||f.DoorCodeVisible)throw new Exception("Revealed state leaked to a different door");
     f.Pulse();foreach(bool hidden in new[]{false,true}){if(hidden)f.Hide();for(int i=0;i<120;i++)f.Pulse();foreach(var file in new[]{"request.txt","world-request.txt","inventory-request.txt","quest-request.txt"})if(File.Exists(Path.Combine(f.bridge,file)))throw new Exception("Door reference sends gameplay requests: "+file);}
     f.timer.Dispose();f.tray.Dispose();
    }
    foreach(string code in new[]{"0505;quit","１２３４","12 34",""}){var bad=new JavaScriptSerializer().Deserialize<List<DoorCodeEntry>>(File.ReadAllText(Path.Combine(root,"door-codes.json"),Encoding.UTF8));bad[0].Code=code;bool rejected=false;try{DoorCodeData.Validate(bad);}catch(FormatException){rejected=true;}if(!rejected)throw new Exception("Malformed door code accepted");}
    File.WriteAllText(Path.Combine(temp,"door-codes.json"),"invalid json");using(var f=new SpawnForm(temp,true)){if(!f.doorCodeStatus.Text.Contains("недоступен")||f.copyDoorCode.Enabled||f.catalog.Count==0)throw new Exception("Broken optional door catalog prevents normal menu");f.timer.Dispose();f.tray.Dispose();}
   }finally{string resolved=Path.GetFullPath(temp);if(!resolved.StartsWith(Path.GetFullPath(Path.GetTempPath()),StringComparison.OrdinalIgnoreCase)||!Path.GetFileName(resolved).StartsWith("ZoneSpawner-doors-",StringComparison.Ordinal))throw new Exception("Unsafe door test cleanup");Directory.Delete(resolved,true);}
   Console.WriteLine("PASS: 27 door codes, leading zeros, hidden/search/reveal/copy states, region/type/story filters, variable/malformed codes, busy clipboard and no gameplay requests in visible/hidden idle");
  }
 }
}
