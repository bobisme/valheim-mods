using BuildShapes;
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
 Console.WriteLine($"Passed {checks} production Hallwright draft persistence checks.");
}
finally{File.Delete(path);File.Delete(path+".tmp");}
