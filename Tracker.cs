using System.Diagnostics;
using System.Numerics;
namespace SoberEsp;
public record Entity(string Name,Vector4 Rect,float Distance,Vector3 Position,Vector2 AimPoint,ulong Id=0,float? PlayerDistance=null,Vector2? TorsoAimPoint=null,bool BoxVisible=true,bool HeadAvailable=true,IReadOnlyList<Vector4>? Box3DEdges=null,IReadOnlyList<Vector4>? Skeleton=null,Vector2? PredictedPoint=null,Vector2? PredictedTorsoPoint=null,Vector3 Velocity=default,Vector2? HorizontalPredictedPoint=null,Vector2? VerticalPredictedPoint=null,Vector2? HorizontalPredictedTorsoPoint=null,Vector2? VerticalPredictedTorsoPoint=null,Vector2? GravityProjectionDelta=null,bool IsNpc=false,string? ToolName=null,float? HealthFraction=null,float? WalkSpeed=null);
public record Snapshot(string Status,int Pid=0,int Width=1,int Height=1,IReadOnlyList<Entity>? Entities=null,int Tracked=0,double SampleMs=0,CameraContext? CameraContext=null,Vector3? LocalPosition=null);
public record Target(ulong Model,string Name,ulong Root,ulong Head,ulong Player=0);
public sealed class Tracker(MemoryBridge bridge,string home)
{
 public volatile Snapshot Latest=new("Connecting");
 Profile? profile;readonly Discovery discovery=new(bridge,home);List<Target> targets=[];long lastTargets;long lastPid;long lastDiscovery;ulong localRoot,localModel,localPlayer;
 List<Target> npcTargets=[];long lastNpcScan;ulong npcWorkspace;
 readonly Dictionary<ulong,(Vector3 Position,long Stamp)> positionHistory=[];
 public static Vector3? Project(Vector3 world,float[] frame,float fov,int width,int height)
 {
  if(frame.Length<12||!float.IsFinite(fov)||fov<=.01||fov>=3.13||width<2||height<2||frame.Any(v=>!float.IsFinite(v))||!float.IsFinite(world.X+world.Y+world.Z))return null;
  var d=world-new Vector3(frame[9],frame[10],frame[11]);float x=frame[0]*d.X+frame[3]*d.Y+frame[6]*d.Z,y=frame[1]*d.X+frame[4]*d.Y+frame[7]*d.Z,z=-(frame[2]*d.X+frame[5]*d.Y+frame[8]*d.Z);if(z<=.05)return null;float focal=height/(2*MathF.Tan(fov/2));return new(width/2f+x*focal/z,height/2f-y*focal/z,z);
 }
 void UpdateTargets(SceneMemory mem)
 {
  ulong workspace=profile!.Workspace;if(mem.Type(workspace)!="N3RBX9WorkspaceE")throw new IOException("Scene changed");
  ulong dataModel=mem.U64(workspace+Offsets.Instance.Parent);
  ulong players=mem.ChildrenChecked(dataModel).FirstOrDefault(p=>mem.Type(p)=="N3RBX7PlayersE");
  if(players==0)throw new IOException("Players service not ready");
  ulong local=mem.U64(players+Offsets.Players.LocalPlayer);
  if(local==0||mem.Type(local)!="N3RBX6PlayerE"||mem.U64(local+Offsets.Instance.Parent)!=players)throw new IOException("Local player not ready");
  ulong character=mem.U64(local+Offsets.Player.Character);
  ulong ownRoot=0;try{if(character!=0)ownRoot=CharacterReader.Resolve(mem,character,"local").Root;}catch(IOException){}
  var next=new List<Target>();
  foreach(ulong player in mem.ChildrenChecked(players))
  {
   if(player==local||mem.Type(player)!="N3RBX6PlayerE")continue;
   try
   {
    ulong model=mem.U64(player+Offsets.Player.Character);
    if(model==0||!CharacterReader.IsInWorkspace(mem,model,workspace))continue;
    var previous=targets.FirstOrDefault(t=>t.Player==player&&t.Model==model);
    try{next.Add(CharacterReader.Resolve(mem,model,mem.Name(model),player));}
    catch(IOException){next.Add(previous??new(model,mem.Name(model),0,0,player));}
   }
   catch(IOException){var previous=targets.FirstOrDefault(t=>t.Player==player);if(previous!=null)next.Add(previous);}
  }
  var playerModels=next.Select(t=>t.Model).ToHashSet();if(character!=0)playerModels.Add(character);
  // NPC discovery is deliberately bounded and cached. Large Workspace folders
  // are skipped rather than materialized; only small (<129 child) branches are
  // traversed, and the scan runs at most every three seconds.
  if(npcWorkspace!=workspace){npcTargets=[];lastNpcScan=0;npcWorkspace=workspace;}
  if(Stopwatch.GetElapsedTime(lastNpcScan).TotalSeconds>=3)
  {
   var found=new List<Target>();var queue=new Queue<(ulong Instance,int Depth,ulong Parent)>();
   try
   {
    var (roots,total)=mem.ChildrenPageChecked(workspace,0,128);
    if(total<=128)
    {
     foreach(ulong child in roots)queue.Enqueue((child,0,workspace));int scanned=0;
     while(queue.Count>0&&scanned++<512&&found.Count<128)
     {
      var (instance,depth,parent)=queue.Dequeue();if(mem.U64(instance+Offsets.Instance.Parent)!=parent)continue;
      string type=mem.Type(instance);if(playerModels.Contains(instance))continue;
      if(type=="N3RBX13ModelInstanceE"&&CharacterReader.HasHumanoid(mem,instance))
      {
       try{var npc=CharacterReader.Resolve(mem,instance,mem.Name(instance),requirePosition:true);if(npc.Root!=0)found.Add(npc);}catch(IOException){}
       continue;
      }
      if(depth<2&&type is "N3RBX6FolderE" or "N3RBX13ModelInstanceE")
      {
       var (children,count)=mem.ChildrenPageChecked(instance,0,128);
       if(count<=128)foreach(ulong child in children)queue.Enqueue((child,depth+1,instance));
      }
     }
    }
    npcTargets=found;lastNpcScan=Stopwatch.GetTimestamp();
   }
   catch(IOException){lastNpcScan=Stopwatch.GetTimestamp();}
  }
  var activePlayers=next.Select(t=>t.Model).ToHashSet();
  foreach(var npc in npcTargets)if(!activePlayers.Contains(npc.Model)&&!playerModels.Contains(npc.Model))next.Add(npc);
  // Commit after enumeration succeeds, so a failed service/vector read does not
  // replace all tracked characters with an empty list.
  targets=next;localPlayer=local;localModel=character;localRoot=ownRoot;lastTargets=Stopwatch.GetTimestamp();
  foreach(var camera in mem.Children(workspace))
   if(mem.Type(camera)=="N3RBX6CameraE"&&mem.U64(camera+Offsets.Instance.Parent)==workspace)
   {var v=mem.Read(camera+Offsets.Camera.Viewport,4);if(BitConverter.ToInt16(v)>1&&BitConverter.ToInt16(v,2)>1)profile=profile with{Camera=camera};}
 }
 public Snapshot Sample()
 {
  var timer=Stopwatch.StartNew();var mem=new SceneMemory(bridge);if(profile is null)throw new IOException("No scene");
  if(Stopwatch.GetElapsedTime(lastTargets).TotalMilliseconds>250)
  {
   if(mem.Type(profile.Workspace)!="N3RBX9WorkspaceE")throw new IOException("Scene changed");
   try{UpdateTargets(mem);}catch(IOException){/* Validate cached ownership per sample and retry enumeration next frame. */}
  }
  var camera=mem.Read(profile.Camera+Offsets.Camera.Frame,Offsets.Camera.SampleSize);var frame=new float[Offsets.Camera.FrameFloats];Buffer.BlockCopy(camera,0,frame,0,Offsets.Camera.FrameBytes);float fov=BitConverter.ToSingle(camera,Offsets.Camera.FovInSample);int w=BitConverter.ToInt16(camera,Offsets.Camera.WidthInSample),h=BitConverter.ToInt16(camera,Offsets.Camera.HeightInSample);if(w<=1||h<=1||w>20000||h>20000)throw new IOException("Camera not ready");
  Vector3? localPosition=null;try{if(localPlayer!=0&&mem.U64(localPlayer+Offsets.Player.Character)==localModel)localPosition=CharacterReader.Position(mem,localRoot,localModel);}catch(IOException){}
  var result=new List<Entity>();
  for(int i=0;i<targets.Count;i++)
  {
   var target=targets[i];CharacterSample? sample=null;
   try{sample=CharacterReader.Read(mem,target,profile.Workspace);}catch(IOException){}
   if(sample==null)
   {
    // Physics/streaming can replace a primitive while we read it. Retry once
    // with fresh pages rather than reusing a failed pointer from this sample.
    try{sample=CharacterReader.Read(new SceneMemory(bridge),target,profile.Workspace);}catch(IOException){}
   }
   if(sample==null)continue;
   targets[i]=sample.Target;
   long now=Stopwatch.GetTimestamp();Vector3 velocity=default;
   if(positionHistory.TryGetValue(sample.Target.Model,out var previous))
   {
    double dt=Stopwatch.GetElapsedTime(previous.Stamp,now).TotalSeconds;
    if(dt is >.001 and <1){velocity=(sample.Position-previous.Position)/(float)dt;if(!float.IsFinite(velocity.X+velocity.Y+velocity.Z)||velocity.Length()>1000)velocity=default;}
   }
   positionHistory[sample.Target.Model]=(sample.Position,now);
   var entity=EntityProjection.Create(sample.Target,sample.Position,sample.Head,frame,fov,w,h,localPosition,sample.Parts,velocity,sample.ToolName,sample.HealthFraction,sample.WalkSpeed);
   if(entity!=null)result.Add(entity);
  }
  var activeIds=targets.Select(t=>t.Model).ToHashSet();foreach(var id in positionHistory.Keys.Where(id=>!activeIds.Contains(id)).ToArray())positionHistory.Remove(id);
  return new("Live",profile.Pid,w,h,result,targets.Count,timer.Elapsed.TotalMilliseconds,new(profile.Pid,profile.Base,profile.Workspace,profile.Camera),localPosition);
 }
 public ExplorerBranch ReadExplorer(CameraContext context,ulong address,int offset,CancellationToken token)=>
  ExplorerReader.Read(new SceneMemory(bridge),context,address,offset,token);
 public void AimCamera(Snapshot state,Entity target,Settings settings,double dt)
 {
  if(state.CameraContext is not {} camera)throw new IOException("Camera context is unavailable");
  var velocity=settings.AimPrediction?target.Velocity:Vector3.Zero;
  if(!settings.HorizontalPrediction)velocity.X=velocity.Z=0;if(!settings.VerticalPrediction)velocity.Y=0;
  float lead=PredictiveAim.LeadSeconds(target.Distance,settings);
  float gravity=settings.AimPrediction&&settings.ProjectilePrediction?settings.ProjectileGravity:0;
  bridge.AimCamera(camera,target.Id,settings.AimAtHead&&target.HeadAvailable,AimMath.Response(settings,dt,target.Distance),velocity,settings.AimPrediction?lead:0,gravity);
 }
 public async Task Run(CancellationToken token)
 {
  while(!token.IsCancellationRequested)
  {
   var iteration=Stopwatch.StartNew();try
   {
    if(profile is null||Stopwatch.GetElapsedTime(lastPid).TotalSeconds>1)
    {
    var live=bridge.GetMaps();lastPid=Stopwatch.GetTimestamp();if(profile is null||profile.Pid!=live.Pid||(profile.Provisional&&Stopwatch.GetElapsedTime(lastDiscovery).TotalSeconds>2)){profile=discovery.Find(live,s=>Latest=new(s),token,profile is null);lastDiscovery=Stopwatch.GetTimestamp();targets=[];npcTargets=[];npcWorkspace=0;lastNpcScan=0;lastTargets=0;}
    }
    Latest=Sample();
   }
   catch(OperationCanceledException){return;}
   catch(Exception e){Latest=new("Waiting: "+e.Message);profile=null;try{await Task.Delay(200,token);}catch(OperationCanceledException){return;}}
   int wait=Math.Max(0,(int)(1000/60d-iteration.Elapsed.TotalMilliseconds));if(wait>0)try{await Task.Delay(wait,token);}catch(OperationCanceledException){return;}
  }
 }
}
