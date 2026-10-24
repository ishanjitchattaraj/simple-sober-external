using System.Numerics;
namespace SoberEsp;

public record CameraContext(int Pid,ulong Base,ulong Workspace,ulong Camera);

public static class CameraAim
{
 // Engine storage is row-major, with right/up/back in columns. Numerics uses
 // row vectors, so transpose the basis for quaternion interpolation.
 public static float[] Rotation(float[] frame,Vector3 target,float response)
 {
  if(frame.Length!=Offsets.Camera.FrameFloats||frame.Any(f=>!float.IsFinite(f))||
   !float.IsFinite(target.X)||!float.IsFinite(target.Y)||!float.IsFinite(target.Z)||
   !float.IsFinite(response)||response<0||response>1)throw new IOException("Invalid camera aim input");
  var right=new Vector3(frame[0],frame[3],frame[6]);
  var up=new Vector3(frame[1],frame[4],frame[7]);
  var back=new Vector3(frame[2],frame[5],frame[8]);
  if(Math.Abs(right.LengthSquared()-1)>.02f||Math.Abs(up.LengthSquared()-1)>.02f||Math.Abs(back.LengthSquared()-1)>.02f||
   Math.Abs(Vector3.Dot(right,up))>.02f||Math.Abs(Vector3.Dot(right,back))>.02f||Math.Abs(Vector3.Dot(up,back))>.02f||
   Vector3.Dot(Vector3.Cross(right,up),back)<.98f)throw new IOException("Camera basis is not a valid rotation");
  var delta=new Vector3(frame[9],frame[10],frame[11])-target;
  if(delta.LengthSquared()<.0001f)throw new IOException("Target is at the camera position");
  var wantedBack=Vector3.Normalize(delta);var wantedRight=Vector3.Cross(Vector3.UnitY,wantedBack);
  if(wantedRight.LengthSquared()<.0001f)wantedRight=right-wantedBack*Vector3.Dot(right,wantedBack);
  if(wantedRight.LengthSquared()<.0001f)wantedRight=Vector3.Cross(Vector3.UnitZ,wantedBack);
  wantedRight=Vector3.Normalize(wantedRight);var wantedUp=Vector3.Normalize(Vector3.Cross(wantedBack,wantedRight));
  static Quaternion Q(Vector3 r,Vector3 u,Vector3 b)=>Quaternion.Normalize(Quaternion.CreateFromRotationMatrix(
   new Matrix4x4(r.X,r.Y,r.Z,0,u.X,u.Y,u.Z,0,b.X,b.Y,b.Z,0,0,0,0,1)));
  var rotation=Matrix4x4.CreateFromQuaternion(Quaternion.Slerp(Q(right,up,back),Q(wantedRight,wantedUp,wantedBack),response));
  return [rotation.M11,rotation.M21,rotation.M31,rotation.M12,rotation.M22,rotation.M32,rotation.M13,rotation.M23,rotation.M33];
 }
}
