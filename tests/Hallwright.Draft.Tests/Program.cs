using Hallwright;
using Newtonsoft.Json;
int checks=0;
void Check(bool ok,string message){checks++;if(!ok)throw new Exception(message);}
void Reject(Action action,string message){try{action();}catch(Exception e)when(e is ArgumentException || e is JsonException){checks++;return;}throw new Exception(message);}
string path=Path.Combine(Path.GetTempPath(),"hallwright-draft-test-"+Guid.NewGuid()+".json");
var record=new HallDraft{World=17,Player=99,Origin=new[]{123f,34.125f,-50f},Yaw=37.25f,Raise=0.17f,
 Corners=new[]{new[]{123f,34.125f,-50f}},Doors=Array.Empty<float[]>(),Height=3,Detail=3,Entrance=0,Material=0,Opening=0,Crest=0,Storeys=2,
 Roof45=true,Solid=false,ShowRoof=true,Tiered=true,Overhang=true,Porch=true,Sweep=true,Basement=true,GuideOnly=true};
try
{
 HallDraftStore.Write(path,record);
 Check(JsonConvert.SerializeObject(HallDraftStore.Read(path,17,99))==JsonConvert.SerializeObject(record),"A partial rotated terrain-height draft and all options survive the production serializer");
 record.Corners=Enumerable.Range(0,24).Select(i=>new[]{123f+i*2,34.125f,-50f+i*2}).ToArray();
 record.Doors=Enumerable.Range(0,8).Select(i=>new[]{(float)i*2,0f,4f}).ToArray();
 HallDraftStore.Write(path,record);var restored=HallDraftStore.Read(path,17,99);
 Check(restored.Corners.Length==24&&restored.Doors.Length==8&&restored.Storeys==2&&restored.Basement&&restored.Tiered&&restored.Sweep&&restored.Detail==3,"Atomic replacement preserves the full-size outline, door hints and royal settings");
 Check(restored.Corners.Zip(record.Corners).All(pair=>pair.First.SequenceEqual(pair.Second))&&restored.Doors.Zip(record.Doors).All(pair=>pair.First.SequenceEqual(pair.Second)),"Marker coordinates are exact, including terrain Y and door-local coordinates");
 Check(!File.Exists(path+".tmp"),"Atomic writes leave no unfinished temporary file");
 Reject(()=>HallDraftStore.Read(path,18,99),"Foreign world accepted");Reject(()=>HallDraftStore.Read(path,17,100),"Foreign character accepted");
 string baseline=File.ReadAllText(path);
 record.Corners[0][0]=float.NaN;Reject(()=>HallDraftStore.Write(path,record),"Nonfinite marker accepted");
 Check(File.ReadAllText(path)==baseline,"Invalid save never overwrites the last usable draft");
 record.Corners[0][0]=123;
 record.Doors=new[]{new float[]{0,0}};Reject(()=>HallDraftStore.Write(path,record),"Malformed door point accepted");
 File.WriteAllText(path,"{not valid");Reject(()=>HallDraftStore.Read(path,17,99),"Corrupt draft accepted");
 File.WriteAllText(path,new string(' ',65537));Reject(()=>HallDraftStore.Read(path,17,99),"Oversized draft parsed");
 record.Doors=Array.Empty<float[]>();record.Storeys=1;record.Basement=false;record.StaveTemple=true;record.StaveHeight=8;record.StaveCrowns=2;record.StaveGallery=false;
 HallDraftStore.Write(path,record);var temple=HallDraftStore.Read(path,17,99);Check(temple.StaveTemple&&temple.StaveHeight==8&&temple.StaveCrowns==2&&!temple.StaveGallery,"Temple style and all options survive reloads");
 var old=Newtonsoft.Json.Linq.JObject.Parse(File.ReadAllText(path));foreach(string name in new[]{"StaveTemple","StaveHeight","StaveCrowns","StaveGallery"})old.Remove(name);File.WriteAllText(path,old.ToString());var legacy=HallDraftStore.Read(path,17,99);Check(!legacy.StaveTemple&&legacy.StaveHeight==6&&legacy.StaveCrowns==1&&legacy.StaveGallery,"Legacy hall drafts retain safe defaults");
 record.StaveHeight=5;Reject(()=>HallDraftStore.Write(path,record),"Invalid temple height accepted");record.StaveHeight=8;record.StaveCrowns=3;Reject(()=>HallDraftStore.Write(path,record),"Unbounded crowns accepted");record.StaveCrowns=2;record.Storeys=2;Reject(()=>HallDraftStore.Write(path,record),"Temple storeys accepted");record.Storeys=1;record.Version=2;Reject(()=>HallDraftStore.Write(path,record),"Unknown schema accepted");
 record.Version=1;record.Storeys=1;
 record.Chimneys=new[]{new[]{3f,0f,5f},new[]{7f,0f,9f}};record.Shutters=false;HallDraftStore.Write(path,record);var marked=HallDraftStore.Read(path,17,99);
 Check(marked.Chimneys.Length==2&&marked.Chimneys[1].SequenceEqual(new[]{7f,0f,9f})&&!marked.Shutters,"Chimney markers and the shutter switch survive reloads");
 old=Newtonsoft.Json.Linq.JObject.Parse(File.ReadAllText(path));old.Remove("Chimneys");old.Remove("Shutters");File.WriteAllText(path,old.ToString());var before=HallDraftStore.Read(path,17,99);
 Check(before.Chimneys==null&&before.Shutters,"Drafts saved before chimneys load with none, and shutters on");
 record.Sketch=false;HallDraftStore.Write(path,record);Check(!HallDraftStore.Read(path,17,99).Sketch,"Leaving the Sketch preview survives reloads");
 old=Newtonsoft.Json.Linq.JObject.Parse(File.ReadAllText(path));old.Remove("Sketch");File.WriteAllText(path,old.ToString());Check(HallDraftStore.Read(path,17,99).Sketch,"Drafts saved before the Sketch preview open in it");
 record.Chimneys=Enumerable.Range(0,5).Select(i=>new[]{i*2+1f,0f,1f}).ToArray();Reject(()=>HallDraftStore.Write(path,record),"Five chimneys accepted");
 record.Chimneys=new[]{new[]{float.NaN,0f,1f}};Reject(()=>HallDraftStore.Write(path,record),"Non-finite chimney accepted");
 record.Chimneys=null;record.StaveTemple=false;record.Norse=true;record.Sink=1.5f;record.Benches=false;record.Hearth=false;record.StoneFooting=false;record.Storeys=1;record.Basement=record.Porch=record.Overhang=false;HallDraftStore.Write(path,record);var norse=HallDraftStore.Read(path,17,99);
 Check(norse.Norse&&norse.Sink==1.5f&&!norse.Benches&&!norse.Hearth&&!norse.StoneFooting,"Longhouse style, depth and switches survive reloads");
 old=Newtonsoft.Json.Linq.JObject.Parse(File.ReadAllText(path));foreach(var key in new[]{"Norse","Sink","Benches","Hearth","StoneFooting"})old.Remove(key);File.WriteAllText(path,old.ToString());var older=HallDraftStore.Read(path,17,99);
 Check(!older.Norse&&older.Sink==0.5f&&older.Benches&&older.Hearth&&older.StoneFooting,"Drafts saved before longhouses load as timber halls with longhouse defaults ready");
 record.Sink=2.5f;Reject(()=>HallDraftStore.Write(path,record),"Sink deeper than 2 m accepted");record.Sink=1;
 record.Porch=true;Reject(()=>HallDraftStore.Write(path,record),"Longhouse porch accepted");record.Porch=false;
 record.Storeys=2;Reject(()=>HallDraftStore.Write(path,record),"Two-storey longhouse accepted");record.Storeys=1;
 Console.WriteLine($"Passed {checks} production Hallwright draft persistence checks.");
}
finally{File.Delete(path);File.Delete(path+".tmp");}
