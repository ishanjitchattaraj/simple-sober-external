using System.Numerics;
namespace SoberEsp;

public sealed class TargetUnavailableException(string message):IOException(message);

public record CharacterSample(Target Target,Vector3 Position,Vector3? Head);

public static class CharacterReader
{
 public static bool IsInWorkspace(SceneMemory memory,ulong instance,ulong workspace)
 {
  var seen=new HashSet<ulong>();
  for(int depth=0;depth<64&&instance!=0&&seen.Add(instance);depth++)
  {
   if(instance==workspace)return true;
   instance=memory.U64(instance+Offsets.Instance.Parent);
  }
  return false;
 }
 public static Target Resolve(SceneMemory memory,ulong model,string name,ulong player=0,bool requirePosition=false)
 {
  var parts=new Dictionary<string,ulong>();
  foreach(var child in memory.ChildrenChecked(model))
   if(memory.U64(child+Offsets.Instance.Parent)==model)parts[memory.Name(child)]=child;
  ulong root=0;
  foreach(string part in new[]{"HumanoidRootPart","UpperTorso","Torso","LowerTorso","Head"})
   if(parts.TryGetValue(part,out var candidate)&&(!requirePosition||Position(memory,candidate,model)!=null)){root=candidate;break;}
  return new(model,name,root,parts.GetValueOrDefault("Head"),player);
 }
 public static Vector3? Position(SceneMemory memory,ulong part,ulong model=0)
 {
  if(part==0)return null;
  try
  {
   if(model!=0&&memory.U64(part+Offsets.Instance.Parent)!=model)return null;
   ulong primitive=memory.U64(part+Offsets.Part.Primitive);if(primitive==0)return null;
   var v=memory.Floats(primitive+Offsets.Part.Position,3);
   return v.All(f=>float.IsFinite(f)&&Math.Abs(f)<1e7)?new Vector3(v[0],v[1],v[2]):null;
  }
  catch(IOException){return null;}
 }
 public static CharacterSample? Read(SceneMemory memory,Target target,ulong workspace)
 {
  if(target.Player!=0&&memory.U64(target.Player+Offsets.Player.Character)!=target.Model)return null;
  if(!IsInWorkspace(memory,target.Model,workspace))return null;
  var position=Position(memory,target.Root,target.Model);
  if(position==null)
  {
   target=Resolve(memory,target.Model,target.Name,target.Player,requirePosition:true);
   position=Position(memory,target.Root,target.Model);
  }
  if(position is not {} root)return null;
  // A missing, streamed, replaced, or unreadable head must not hide a valid body.
  return new(target,root,Position(memory,target.Head,target.Model));
 }
}

public static class EntityProjection
{
 public static Entity? Create(Target target,Vector3 position,Vector3? head,float[] frame,float fov,int width,int height,Vector3? localPosition)
 {
  var body=Tracker.Project(position,frame,fov,width,height);if(body is null)return null;
  var headScreen=head is {} h?Tracker.Project(h,frame,fov,width,height):null;
  var aim=headScreen??body.Value;
  // Project a volume rather than subtracting two Y coordinates: prone/rolling
  // characters and a close-up body can have coincident or near-plane endpoints.
  var points=new List<Vector3>();
  for(int x=-1;x<=1;x+=2)for(int y=-1;y<=1;y+=2)for(int z=-1;z<=1;z+=2)
   if(Tracker.Project(position+new Vector3(x*1.5f,y*3,z*1.5f),frame,fov,width,height) is {} point)points.Add(point);
  points.Add(body.Value);
  if(headScreen is {} projectedHead)points.Add(projectedHead);
  float left=points.Min(p=>p.X),right=points.Max(p=>p.X),top=points.Min(p=>p.Y),bottom=points.Max(p=>p.Y);
  // Tiny/distant bodies remain selectable, and close boxes are clipped instead
  // of causing the entire character to be rejected by a size threshold.
  if(right-left<2){left-=1;right+=1;}
  if(bottom-top<2){top-=1;bottom+=1;}
  left=Math.Clamp(left,0,width);right=Math.Clamp(right,0,width);top=Math.Clamp(top,0,height);bottom=Math.Clamp(bottom,0,height);
  bool boxVisible=right>left&&bottom>top;
  return new(target.Name,new(left,top,right-left,bottom-top),Vector3.Distance(position,new(frame[9],frame[10],frame[11])),position,
   new(aim.X,aim.Y),target.Model,localPosition is {} local?Vector3.Distance(position,local):null,new(body.Value.X,body.Value.Y),boxVisible,headScreen!=null);
 }
}
