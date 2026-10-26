using System.Numerics;
namespace SoberEsp;

public sealed class TargetUnavailableException(string message):IOException(message);

public record CharacterSample(Target Target,Vector3 Position,Vector3? Head,IReadOnlyDictionary<string,Vector3> Parts,string? ToolName=null,float? HealthFraction=null,float? WalkSpeed=null);

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
 public static bool HasHumanoid(SceneMemory memory,ulong model)
 {
  try{return memory.ChildrenChecked(model).Any(child=>memory.U64(child+Offsets.Instance.Parent)==model&&memory.Type(child)=="N3RBX8HumanoidE");}
  catch(IOException){return false;}
 }
 static (float? HealthFraction,float? WalkSpeed) ReadHumanoidMetrics(SceneMemory memory,ulong model)
 {
  try
  {
   foreach(ulong child in memory.ChildrenChecked(model))
   {
    if(memory.U64(child+Offsets.Instance.Parent)!=model||memory.Type(child)!="N3RBX8HumanoidE")continue;
    float health=memory.Floats(child+Offsets.Humanoid.Health,1)[0];
    // This build's getter invocation maps the Engine Instance to the getter's
    // `this` with +0xa0. Display/filtering intentionally uses the requested
    // conventional 100-point scale rather than a mutable MaxHealth field.
    float? fraction=float.IsFinite(health)&&health>=0&&health<=100000?Math.Clamp(health/100f,0,1):null;
    float speed=memory.Floats(child+Offsets.Humanoid.WalkSpeed,1)[0];
    float? walkSpeed=float.IsFinite(speed)&&speed>=0&&speed<=1000?speed:null;
    return(fraction,walkSpeed);
   }
  }
  catch(IOException){}
  return(null,null);
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
  var parts=new Dictionary<string,Vector3>(StringComparer.Ordinal);string? toolName=null;
  try
  {
   foreach(ulong child in memory.ChildrenChecked(target.Model))
   {
    if(memory.U64(child+Offsets.Instance.Parent)!=target.Model)continue;
    string name=memory.Name(child);if(memory.Type(child)=="N3RBX4ToolE")toolName=name;if(name is not ("Head" or "UpperTorso" or "LowerTorso" or "Torso" or "HumanoidRootPart" or "LeftUpperArm" or "LeftLowerArm" or "LeftHand" or "RightUpperArm" or "RightLowerArm" or "RightHand" or "LeftUpperLeg" or "LeftLowerLeg" or "LeftFoot" or "RightUpperLeg" or "RightLowerLeg" or "RightFoot" or "Left Arm" or "Right Arm" or "Left Leg" or "Right Leg"))continue;
    if(Position(memory,child,target.Model) is{} point)parts[name]=point;
   }
  }
  catch(IOException){/* An incomplete articulated skeleton is fine; the root sample remains valid. */}
  var metrics=ReadHumanoidMetrics(memory,target.Model);
  return new(target,root,Position(memory,target.Head,target.Model),parts,toolName,metrics.HealthFraction,metrics.WalkSpeed);
 }
}

public static class EntityProjection
{
 public static Entity? Create(Target target,Vector3 position,Vector3? head,float[] frame,float fov,int width,int height,Vector3? localPosition,IReadOnlyDictionary<string,Vector3>? parts=null,Vector3 velocity=default,string? toolName=null,float? healthFraction=null,float? walkSpeed=null)
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
  var boxEdges=new List<Vector4>();var corners=new Vector3[8];
  for(int i=0;i<8;i++)corners[i]=position+new Vector3((i&1)==0?-1.5f:1.5f,(i&2)==0?-3f:3f,(i&4)==0?-1.5f:1.5f);
  foreach((int a,int b) in new[]{(0,1),(2,3),(4,5),(6,7),(0,2),(1,3),(4,6),(5,7),(0,4),(1,5),(2,6),(3,7)})
   if(Tracker.Project(corners[a],frame,fov,width,height)is{} p1&&Tracker.Project(corners[b],frame,fov,width,height)is{} p2)boxEdges.Add(new(p1.X,p1.Y,p2.X,p2.Y));
  var screenParts=new Dictionary<string,Vector2>(StringComparer.Ordinal);
  if(parts is not null)foreach(var pair in parts)if(Tracker.Project(pair.Value,frame,fov,width,height)is{} projected)screenParts[pair.Key]=new(projected.X,projected.Y);
  var skeleton=new List<Vector4>();
  void Bone(string from,string to){if(screenParts.TryGetValue(from,out var a)&&screenParts.TryGetValue(to,out var b))skeleton.Add(new(a.X,a.Y,b.X,b.Y));}
  Bone("Head","UpperTorso");Bone("Head","Torso");Bone("UpperTorso","LowerTorso");Bone("UpperTorso","HumanoidRootPart");Bone("LowerTorso","HumanoidRootPart");
  Bone("UpperTorso","LeftUpperArm");Bone("LeftUpperArm","LeftLowerArm");Bone("LeftLowerArm","LeftHand");Bone("UpperTorso","RightUpperArm");Bone("RightUpperArm","RightLowerArm");Bone("RightLowerArm","RightHand");
  Bone("LowerTorso","LeftUpperLeg");Bone("LeftUpperLeg","LeftLowerLeg");Bone("LeftLowerLeg","LeftFoot");Bone("LowerTorso","RightUpperLeg");Bone("RightUpperLeg","RightLowerLeg");Bone("RightLowerLeg","RightFoot");
  Bone("Torso","Left Arm");Bone("Torso","Right Arm");Bone("Torso","Left Leg");Bone("Torso","Right Leg");
  Vector3 aimWorld=head??position;Vector3 horizontalVelocity=new(velocity.X,0,velocity.Z),verticalVelocity=new(0,velocity.Y,0);
  Vector2? Screen(Vector3 world)=>Tracker.Project(world,frame,fov,width,height)is{} projected?new(projected.X,projected.Y):null;
  var predicted=Screen(aimWorld+velocity);var predictedBody=Screen(position+velocity);
  var horizontalPredicted=Screen(aimWorld+horizontalVelocity);var verticalPredicted=Screen(aimWorld+verticalVelocity);
  var horizontalPredictedBody=Screen(position+horizontalVelocity);var verticalPredictedBody=Screen(position+verticalVelocity);
  Vector2? gravityDelta=Screen(aimWorld+new Vector3(0,.5f,0)) is{} gravityPoint&&Screen(aimWorld)is{} originalPoint?gravityPoint-originalPoint:null;
  return new(target.Name,new(left,top,right-left,bottom-top),Vector3.Distance(position,new(frame[9],frame[10],frame[11])),position,
   new(aim.X,aim.Y),target.Model,localPosition is {} local?Vector3.Distance(position,local):null,new(body.Value.X,body.Value.Y),boxVisible,headScreen!=null,boxEdges,skeleton,predicted,predictedBody,velocity,horizontalPredicted,verticalPredicted,horizontalPredictedBody,verticalPredictedBody,gravityDelta,target.Player==0,toolName,healthFraction,walkSpeed);
 }
}
