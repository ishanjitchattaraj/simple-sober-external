using System.Numerics;
using System.Text.Json;
namespace SoberEsp;

public enum AimMethod { Mouse, Camera }

public enum TargetPriority { ClosestToMouse, ClosestToPlayer }
public enum SmoothingCurve { Linear, EaseIn, EaseOut }

public sealed class Settings
{
 public bool Boxes=true, Names=true, Distances=true, Status=true, HealthBars=false;
 public bool CornerBoxes=false,Snaplines=false,HeadDots=false,Crosshair=false,DistanceFade=false,EspNpcs=false,AimNpcs=false,ToolNames=false;
 public bool Boxes3D=false,Skeleton=false,Facing=false,OffscreenArrows=false,Radar=false,Trails=false,TargetHighlight=false,Prediction=false,Gradient=false,Outline=true,TargetHud=false;
 public bool AimEnabled=false, ShowFov=true, TargetMarker=true;
 public float FovRadius=140, Sensitivity=1, Smoothness=8, MaxDistance=1500;
 public bool AimAtHead=true, AimLock=false,AimDeadCheck=false,AimHealthCheck=false;
 public AimMethod AimMethod=AimMethod.Mouse;
 public TargetPriority TargetPriority=TargetPriority.ClosestToMouse;
 public SmoothingCurve SmoothingCurve=SmoothingCurve.Linear;
 public float Deadzone=0,FadeStart=500,Thickness=1.5f,Alpha=1,PredictionSeconds=.2f,ProjectileSpeed=300,ProjectileGravity=0,MinHorizontalVelocity=0,MinVerticalVelocity=0,MinHealthPercent=0;
 public bool HorizontalVelocityCheck=false,VerticalVelocityCheck=false,DynamicFov=false,AimPrediction=false,ProjectilePrediction=false,HorizontalPrediction=true,VerticalPrediction=true,DistanceSmoothing=false;
 public Vector4 EspColor=new(.25f,1,.35f,1),EspGradientColor=new(.75f,.35f,1,1);
 public List<string> Whitelist=[];
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
  if(!Enum.IsDefined(SmoothingCurve))SmoothingCurve=SmoothingCurve.Linear;
  Smoothness=Valid(Smoothness,8,1,30);MaxDistance=Valid(MaxDistance,1500,10,10000);
  Deadzone=Valid(Deadzone,0,0,100);FadeStart=Valid(FadeStart,Math.Min(500,MaxDistance),10,MaxDistance);Thickness=Valid(Thickness,1.5f,.5f,5);Alpha=Valid(Alpha,1,0,1);PredictionSeconds=Valid(PredictionSeconds,.2f,.02f,1);ProjectileSpeed=Valid(ProjectileSpeed,300,1,5000);ProjectileGravity=Valid(ProjectileGravity,0,0,2000);MinHorizontalVelocity=Valid(MinHorizontalVelocity,0,0,10000);MinVerticalVelocity=Valid(MinVerticalVelocity,0,0,10000);MinHealthPercent=Valid(MinHealthPercent,0,0,100);
  Whitelist=Whitelist.Where(n=>!string.IsNullOrWhiteSpace(n)).Distinct(StringComparer.OrdinalIgnoreCase).Take(256).ToList();
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
 public static Vector2 Motion(Vector2 error,Settings settings,double dt,float distance=0)
 {
  float length=error.Length();if(length<=settings.Deadzone)return Vector2.Zero;
  if(length>0&&settings.Deadzone>0)error*=((length-settings.Deadzone)/length);
  var move=error*Response(settings,dt,distance);
  return move.Length()>40?Vector2.Normalize(move)*40:move;
 }
 public static float Response(Settings settings,double dt,float distance=0)
 {
  float smoothness=settings.Smoothness*(settings.DistanceSmoothing?1+Math.Clamp(distance/Math.Max(1,settings.MaxDistance),0,1)*2:1);
  float value=Response(settings.Sensitivity,smoothness,dt);
  return settings.SmoothingCurve switch{SmoothingCurve.EaseIn=>value*value,SmoothingCurve.EaseOut=>1-(1-value)*(1-value),_=>value};
 }
 public static Vector2 Point(Entity entity,Vector2 scale,Settings settings)
 {
  var point=settings.AimAtHead?entity.AimPoint:entity.TorsoAimPoint??new Vector2(entity.Rect.X+entity.Rect.Z/2,entity.Rect.Y+entity.Rect.W*.45f);
  if(settings.AimPrediction)
  {
   float lead=PredictiveAim.LeadSeconds(entity.Distance,settings);
   var horizontal=settings.AimAtHead?entity.HorizontalPredictedPoint:entity.HorizontalPredictedTorsoPoint;
   var vertical=settings.AimAtHead?entity.VerticalPredictedPoint:entity.VerticalPredictedTorsoPoint;
   var origin=point;
   if(settings.HorizontalPrediction&&horizontal is{} h)point+=(h-origin)*lead;
   if(settings.VerticalPrediction&&vertical is{} v)point+=(v-origin)*lead;
   if(settings.ProjectilePrediction&&entity.GravityProjectionDelta is{} gravityDelta)point+=gravityDelta*(.5f*settings.ProjectileGravity*lead*lead);
  }
  return point*scale;
 }
 public static bool Eligible(Entity entity,Vector2 scale,Vector2 center,Settings settings)
 {
  if(!float.IsFinite(entity.Distance)||entity.Distance>settings.MaxDistance||settings.Whitelist.Contains(entity.Name,StringComparer.OrdinalIgnoreCase)||entity.IsNpc&&!settings.AimNpcs)return false;
  if(settings.AimDeadCheck&&(entity.HealthFraction is not{} deadHealth||deadHealth<=0))return false;
  if(settings.AimHealthCheck&&(entity.HealthFraction is not{} health||health*100<settings.MinHealthPercent))return false;
  float horizontal=MathF.Sqrt(entity.Velocity.X*entity.Velocity.X+entity.Velocity.Z*entity.Velocity.Z);
  if(settings.HorizontalVelocityCheck&&horizontal<settings.MinHorizontalVelocity||settings.VerticalVelocityCheck&&Math.Abs(entity.Velocity.Y)<settings.MinVerticalVelocity)return false;
  var point=Point(entity,scale,settings);
  if(point.X<0||point.Y<0||point.X>center.X*2||point.Y>center.Y*2)return false;
  float distance=Vector2.DistanceSquared(point,center);
  float radius=settings.DynamicFov?settings.FovRadius*Math.Clamp(1-entity.Distance/Math.Max(1,settings.MaxDistance),.2f,1):settings.FovRadius;
  return float.IsFinite(distance)&&distance<radius*radius;
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

public static class PredictiveAim
{
 public static float LeadSeconds(float distance,Settings settings)=>settings.ProjectilePrediction?
  Math.Clamp(distance/Math.Max(1,settings.ProjectileSpeed),0,1):Math.Clamp(settings.PredictionSeconds,0,1);
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
