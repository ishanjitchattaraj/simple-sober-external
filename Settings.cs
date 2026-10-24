using System.Numerics;
using System.Text.Json;
namespace SoberEsp;

public enum AimMethod { Mouse, Camera }

public enum TargetPriority { ClosestToMouse, ClosestToPlayer }

public sealed class Settings
{
 public bool Boxes=true, Names=true, Distances=true, Status=true;
 public bool AimEnabled=false, ShowFov=true, TargetMarker=true;
 public float FovRadius=140, Sensitivity=1, Smoothness=8, MaxDistance=1500;
 public bool AimAtHead=true, AimLock=false;
 public AimMethod AimMethod=AimMethod.Mouse;
 public TargetPriority TargetPriority=TargetPriority.ClosestToMouse;
 static readonly JsonSerializerOptions Json=new(){IncludeFields=true,WriteIndented=true};
 public static Settings Load(string path)
 {
  try{var value=JsonSerializer.Deserialize<Settings>(File.ReadAllText(path),Json)??new();value.Clamp();return value;}
  catch{return new();}
 }
 public void Clamp()
 {
  FovRadius=Valid(FovRadius,140,20,600);Sensitivity=Valid(Sensitivity,1,.1f,20);
  if(!Enum.IsDefined(AimMethod))AimMethod=AimMethod.Mouse;
  if(!Enum.IsDefined(TargetPriority))TargetPriority=TargetPriority.ClosestToMouse;
  Smoothness=Valid(Smoothness,8,1,30);MaxDistance=Valid(MaxDistance,1500,10,10000);
 }
 static float Valid(float value,float fallback,float min,float max)=>float.IsFinite(value)?Math.Clamp(value,min,max):fallback;
 public void Save(string path){Clamp();File.WriteAllText(path+".tmp",JsonSerializer.Serialize(this,Json));File.Move(path+".tmp",path,true);}
}

public static class AimMath
{
 // Frame-rate independent response; cap each motion to avoid a large jump after a stalled frame.
 public static float Response(float sensitivity,float smoothness,double dt)=>
  Math.Clamp((1-MathF.Exp(-(float)Math.Clamp(dt,0,.05)*60/Math.Max(1,smoothness)))*sensitivity,0,1);
 public static Vector2 Motion(Vector2 error,float sensitivity,float smoothness,double dt)
 {
  var move=error*Response(sensitivity,smoothness,dt);
  return move.Length()>40?Vector2.Normalize(move)*40:move;
 }
 public static Vector2 Point(Entity entity,Vector2 scale,Settings settings)=>
  (settings.AimAtHead?entity.AimPoint:entity.TorsoAimPoint??new Vector2(entity.Rect.X+entity.Rect.Z/2,entity.Rect.Y+entity.Rect.W*.45f))*scale;
 public static bool Eligible(Entity entity,Vector2 scale,Vector2 center,Settings settings)
 {
  if(!float.IsFinite(entity.Distance)||entity.Distance>settings.MaxDistance)return false;
  var point=Point(entity,scale,settings);
  if(point.X<0||point.Y<0||point.X>center.X*2||point.Y>center.Y*2)return false;
  float distance=Vector2.DistanceSquared(point,center);
  return float.IsFinite(distance)&&distance<settings.FovRadius*settings.FovRadius;
 }
 public static Entity? Select(IEnumerable<Entity> entities,Vector2 scale,Vector2 center,Settings settings)
 {
  Entity? best=null;float bestScore=float.PositiveInfinity;
  foreach(var entity in entities)
  {
   if(!Eligible(entity,scale,center,settings))continue;
   // The captured desktop cursor can be offset from the game reticle. Use the
   // same overlay-space center as the FOV circle and aim movement.
   float score=settings.TargetPriority==TargetPriority.ClosestToPlayer?(entity.PlayerDistance??float.PositiveInfinity):
    Vector2.DistanceSquared(Point(entity,scale,settings),center);
   if(float.IsFinite(score)&&score<bestScore){bestScore=score;best=entity;}
  }
  return best;
 }
}

// Retain identity across snapshots, not the previous frame's entity object or display name.
public sealed class AimSelection
{
 ulong? lockedId;
 public void Reset()=>lockedId=null;
 public Entity? Update(IReadOnlyList<Entity> entities,Vector2 scale,Vector2 center,Settings settings,bool active)
 {
  if(!active||!settings.AimLock)Reset();
  if(active&&settings.AimLock&&lockedId is {} id)
  {
   // Do not jump to somebody else when a locked target leaves FOV/range or disappears.
   return entities.FirstOrDefault(e=>e.Id==id&&AimMath.Eligible(e,scale,center,settings));
  }
  var target=AimMath.Select(entities,scale,center,settings);
  if(active&&settings.AimLock&&target is {Id: not 0})lockedId=target.Id;
  return target;
 }
}
