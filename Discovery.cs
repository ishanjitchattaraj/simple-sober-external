using System.Diagnostics;
using System.Text.Json;
namespace SoberEsp;
public record Profile(int Pid,ulong Base,ulong Workspace,ulong Camera,bool Provisional=false,ulong HeapSize=0,ulong HeapOffset=0);
public sealed class Discovery(MemoryBridge bridge,string home)
{
 string Cache=>System.IO.Path.Combine(home,"profile.json");
 Profile? Load(){try{return JsonSerializer.Deserialize<Profile>(File.ReadAllText(Cache));}catch{return null;}}
 public bool Matches(ulong address){try{return Offsets.Fingerprints.All(p=>bridge.Read(address+p.Key,p.Value.Length).AsSpan().SequenceEqual(p.Value));}catch(IOException){return false;}}
 (int Models,ulong Camera)? Candidate(SceneMemory mem,ulong workspace)
 {
  try{if(mem.Type(workspace)!="N3RBX9WorkspaceE"||mem.U64(workspace+Offsets.Instance.Self)!=workspace)return null;var children=mem.Children(workspace);int models=children.Count(c=>mem.Type(c)=="N3RBX13ModelInstanceE");foreach(var camera in children){if(mem.Type(camera)!="N3RBX6CameraE"||mem.U64(camera+Offsets.Instance.Parent)!=workspace)continue;var viewport=mem.Read(camera+Offsets.Camera.Viewport,4);int w=BitConverter.ToInt16(viewport),h=BitConverter.ToInt16(viewport,2);if(w>1&&h>1&&w<20000&&h<20000)return(models,camera);}}catch{}return null;
 }
 public Profile Find(ProcessMaps live,Action<string> status,CancellationToken token,bool useCache=true)
 {
  var timer=Stopwatch.StartNew();var mem=new SceneMemory(bridge);var old=Load();
  if(useCache&&old?.Pid==live.Pid&&Matches(old.Base)&&Candidate(mem,old.Workspace) is {Models:>0} cached)return old with{Camera=cached.Camera};
  ulong baseAddress=live.Maps.Where(r=>r.Permissions.Contains('x')&&r.Path.Contains("memfd:")&&r.End-r.Start>Offsets.MinimumLibraryMappingSize).Select(r=>r.Start).FirstOrDefault(Matches);
  if(baseAddress==0)throw new IOException("Waiting for the supported client library");
  Profile Save(ulong workspace,(int Models,ulong Camera) result){var r=live.Maps.FirstOrDefault(r=>r.Start<=workspace&&workspace<r.End);var p=new Profile(live.Pid,baseAddress,workspace,result.Camera,result.Models==0,r is null?0:r.End-r.Start,r is null?0:workspace-r.Start);File.WriteAllText(Cache,JsonSerializer.Serialize(p));Console.WriteLine($"Scene discovered in {timer.ElapsedMilliseconds} ms");return p;}
  var heaps=live.Maps.Where(r=>r.Permissions.StartsWith("rw")&&r.Path.Contains("mimalloc")).ToList();
  if(old is not null)foreach(var heap in heaps.Where(r=>r.End-r.Start==old.HeapSize)){var candidate=Candidate(mem,heap.Start+old.HeapOffset);if(candidate is {Models:>0} value)return Save(heap.Start+old.HeapOffset,value);}
  byte[] needle=BitConverter.GetBytes(baseAddress+Offsets.WorkspaceVtable);ulong total=0;Profile? fallback=null;
  foreach(var region in heaps)for(ulong address=region.Start;address<region.End;address+=1048576)
  {
   token.ThrowIfCancellationRequested();byte[] data;try{data=bridge.Read(address,(int)Math.Min(1048576UL,region.End-address));}catch(IOException){continue;}total+=(uint)data.Length;
   for(int i=0;i+8<=data.Length;i+=8)if(data.AsSpan(i,8).SequenceEqual(needle)){var found=Candidate(mem,address+(uint)i);if(found is {Models:>0} ready)return Save(address+(uint)i,ready);if(found.HasValue)fallback=new(live.Pid,baseAddress,address+(uint)i,found.Value.Camera,true);}
   if(total%(64*1048576UL)==0)status($"Searching for the active game: {total/1048576} MiB checked");
  }
  if(fallback is not null)return Save(fallback.Workspace,(0,fallback.Camera));throw new IOException("Waiting for a playable scene");
 }
}
