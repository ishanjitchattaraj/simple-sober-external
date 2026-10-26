using System.Net.Sockets;
using System.Numerics;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using Microsoft.Win32.SafeHandles;
namespace SoberEsp;
public sealed class BridgeServer(string path,bool allowCameraWrites=false,uint? ownerUid=null,bool allowSpeedWrites=false)
{
 [DllImport("libc")]static extern uint geteuid();
 [DllImport("libc")]static extern int chown(string path,uint uid,uint gid);
 [DllImport("libc")]static extern int getsockopt(int fd,int level,int option,out Credentials value,ref uint length);
 [StructLayout(LayoutKind.Sequential)]struct Credentials{public int Pid;public uint Uid,Gid;}
 readonly JsonSerializerOptions jsonOptions=new(){PropertyNamingPolicy=JsonNamingPolicy.CamelCase};
 int pid;string stamp="";SafeFileHandle? memory, cameraMemory, speedMemory;List<Mapping> cached=[];long cacheTime;
 readonly object speedLock=new();SpeedLease? speedLease;
 sealed record SpeedLease(int Pid,string Stamp,ulong Workspace,ulong Character,ulong Humanoid,float Original,float Applied,long Expires);
 static string Identity(int p)=>File.ReadAllText($"/proc/{p}/stat").Split(')').Last().Split(' ',StringSplitOptions.RemoveEmptyEntries)[19];
 public static List<Mapping> ReadMaps(int p)
 {
  var result=new List<Mapping>();foreach(string line in File.ReadLines($"/proc/{p}/maps")){var v=line.Split(' ',6,StringSplitOptions.RemoveEmptyEntries);var range=v[0].Split('-');result.Add(new(Convert.ToUInt64(range[0],16),Convert.ToUInt64(range[1],16),v[1],Convert.ToUInt64(v[2],16),v.Length>5?v[5]:""));}return result;
 }
 static uint Owner(int p)=>uint.Parse(File.ReadLines($"/proc/{p}/status").First(s=>s.StartsWith("Uid:")).Split((char[]?)null,StringSplitOptions.RemoveEmptyEntries)[1]);
 public static uint ResolveOwnerUid(uint effectiveUid,uint? sudoUid,uint? requestedOwner,IReadOnlyCollection<uint> soberOwners)
 {
  if(requestedOwner is{} requested)return requested;
  if(sudoUid is{} caller)return caller;
  if(effectiveUid!=0)return effectiveUid;
  var owners=soberOwners.Distinct().ToArray();
  if(owners.Length==1)return owners[0];
  if(owners.Length>1)throw new IOException("Multiple Sober user sessions were found. Restart the bridge with --owner-uid <uid>.");
  throw new IOException("Could not identify the Sober user. Start the bridge with sudo from that user's session so SUDO_UID is set, or specify --owner-uid <uid>.");
 }
 uint BridgeOwner()
 {
  uint? sudo=uint.TryParse(Environment.GetEnvironmentVariable("SUDO_UID"),out uint caller)?caller:null;
  if(ownerUid is not null||sudo is not null||geteuid()!=0)return ResolveOwnerUid(geteuid(),sudo,ownerUid,[]);
  var candidates=new List<uint>();
  foreach(string directory in Directory.EnumerateDirectories("/proc"))
  {
   if(!int.TryParse(System.IO.Path.GetFileName(directory),out int p))continue;
   try{if(File.ReadAllText($"{directory}/comm").Trim()=="Main"&&File.ReadAllLines($"{directory}/root/.flatpak-info").Contains("name=org.vinegarhq.Sober"))candidates.Add(Owner(p));}
   catch(IOException){}catch(UnauthorizedAccessException){}
  }
  return ResolveOwnerUid(geteuid(),sudo,ownerUid,candidates);
 }
 int Target(uint uid)
 {
  try{if(pid!=0&&Identity(pid)==stamp)return pid;}catch(IOException){}
  foreach(string directory in Directory.EnumerateDirectories("/proc"))
  {
   if(!int.TryParse(System.IO.Path.GetFileName(directory),out int p))continue;
   bool target=false;
   try{target=Owner(p)==uid&&File.ReadAllText($"{directory}/comm").Trim()=="Main"&&File.ReadAllLines($"{directory}/root/.flatpak-info").Contains("name=org.vinegarhq.Sober");}
   catch(IOException){}catch(UnauthorizedAccessException){}
   if(!target)continue;
   SafeFileHandle opened;
   try{opened=File.OpenHandle($"/proc/{p}/mem",FileMode.Open,FileAccess.Read,FileShare.ReadWrite,FileOptions.RandomAccess);}
   catch(Exception error)when(error is IOException or UnauthorizedAccessException){throw new IOException("Cannot open Sober process memory. Start the bridge with sudo (for example: sudo ./run-bridge.sh).",error);}
   cameraMemory?.Dispose();cameraMemory=null;memory?.Dispose();memory=opened;pid=p;stamp=Identity(p);cached=[];Console.WriteLine($"Memory connection to Sober PID {p}");return p;
  }
  throw new IOException("Waiting for Sober");
 }
 byte[] Read(ulong address,int length)
 {
  if(length<1||length>1048576||address>long.MaxValue-(ulong)length)throw new IOException("Invalid read range");
  if(cached.Count==0||System.Diagnostics.Stopwatch.GetElapsedTime(cacheTime).TotalSeconds>1){cached=ReadMaps(pid);cacheTime=System.Diagnostics.Stopwatch.GetTimestamp();}
  int lo=0,hi=cached.Count;while(lo<hi){int middle=(lo+hi)/2;if(cached[middle].Start<=address)lo=middle+1;else hi=middle;}
  if(lo==0||address+(uint)length>cached[lo-1].End||!cached[lo-1].Permissions.StartsWith('r'))throw new IOException("Read must stay inside one readable mapping");
  var result=new byte[length];int n=RandomAccess.Read(memory!,result,(long)address);if(n!=length)throw new IOException("Short memory read");return result;
 }
 // This is the only write operation: exactly nine camera rotation floats.
 // The caller supplies object identities, never an arbitrary destination or byte payload.
 object AimCamera(JsonElement request)
 {
  if(!allowCameraWrites)throw new IOException("Camera mode needs a bridge started with --allow-camera-writes; restart the managed session");
  if(request.GetProperty("pid").GetInt32()!=pid)throw new IOException("Sober process changed");
  ulong baseAddress=request.GetProperty("baseAddress").GetUInt64(),workspace=request.GetProperty("workspace").GetUInt64();
  ulong camera=request.GetProperty("camera").GetUInt64(),model=request.GetProperty("model").GetUInt64();
  float response=request.GetProperty("response").GetSingle();
  if(!float.IsFinite(response)||response<0||response>1)throw new IOException("Invalid camera response");
  Vector3 leadVelocity=Vector3.Zero;float leadSeconds=0,gravity=0;
  if(request.TryGetProperty("velocity",out var velocityValue))
  {
   leadVelocity=new(velocityValue.GetProperty("x").GetSingle(),velocityValue.GetProperty("y").GetSingle(),velocityValue.GetProperty("z").GetSingle());
   leadSeconds=request.GetProperty("leadSeconds").GetSingle();
   if(request.TryGetProperty("gravity",out var gravityValue))gravity=gravityValue.GetSingle();
  }
  ValidateAimPrediction(leadVelocity,leadSeconds,gravity);
  if(baseAddress>long.MaxValue-Offsets.WorkspaceVtable||camera>long.MaxValue-Offsets.Camera.Frame-Offsets.Camera.FrameBytes)throw new IOException("Invalid camera address");
  foreach(var fingerprint in Offsets.Fingerprints)
   if(!Read(baseAddress+fingerprint.Key,fingerprint.Value.Length).AsSpan().SequenceEqual(fingerprint.Value))throw new IOException("Unsupported camera build");
  var mem=new SceneMemory(Read);
  if(mem.Type(workspace)!="N3RBX9WorkspaceE"||mem.U64(workspace)!=baseAddress+Offsets.WorkspaceVtable||mem.U64(workspace+Offsets.Instance.Self)!=workspace||
   mem.Type(camera)!="N3RBX6CameraE"||mem.U64(camera+Offsets.Instance.Parent)!=workspace||!mem.Children(workspace).Contains(camera))throw new TargetUnavailableException("Scene or camera changed");
  ulong dataModel=mem.U64(workspace+Offsets.Instance.Parent),players=mem.Children(dataModel).FirstOrDefault(p=>mem.Type(p)=="N3RBX7PlayersE");
  if(players==0)throw new TargetUnavailableException("Players service unavailable");
  ulong local=mem.U64(players+Offsets.Players.LocalPlayer);
  if(local==0||!mem.Children(players).Any(p=>p!=local&&mem.Type(p)=="N3RBX6PlayerE"&&mem.U64(p+Offsets.Player.Character)==model)||
   mem.Type(model)!="N3RBX13ModelInstanceE"||!CharacterReader.IsInWorkspace(mem,model,workspace))throw new TargetUnavailableException("Target character is unavailable");
  var parts=CharacterReader.Resolve(mem,model,mem.Name(model),requirePosition:true);
  var position=request.GetProperty("head").GetBoolean()?CharacterReader.Position(mem,parts.Head,model):null;
  position??=CharacterReader.Position(mem,parts.Root,model);
  if(position==null)throw new TargetUnavailableException("Target part is unavailable");
  position+=leadVelocity*leadSeconds+new Vector3(0,.5f*gravity*leadSeconds*leadSeconds,0);
  // Read this again directly so cached validation pages do not supply an old rotation.
  var bytes=Read(camera+Offsets.Camera.Frame,Offsets.Camera.FrameBytes);var frame=new float[Offsets.Camera.FrameFloats];Buffer.BlockCopy(bytes,0,frame,0,bytes.Length);
  var rotation=CameraAim.Rotation(frame,position.Value,response);
  var output=new byte[Offsets.Camera.RotationBytes];Buffer.BlockCopy(rotation,0,output,0,output.Length);
  ulong destination=camera+Offsets.Camera.Frame;
  if(Identity(pid)!=stamp||!ReadMaps(pid).Any(m=>m.Start<=destination&&destination+(ulong)output.Length<=m.End&&m.Permissions.StartsWith("rw")))throw new IOException("Camera mapping changed");
  cameraMemory??=File.OpenHandle($"/proc/{pid}/mem",FileMode.Open,FileAccess.Write,FileShare.ReadWrite,FileOptions.RandomAccess);
  RandomAccess.Write(cameraMemory,output,(long)destination);
  return new{ok=true};
 }
 static void ValidateWalkSpeed(float speed)
 {if(!float.IsFinite(speed)||speed<0||speed>100)throw new IOException("WalkSpeed must be a finite value from 0 to 100");}
 void RestoreSpeed(bool force=false)
 {
  lock(speedLock)
  {
   var lease=speedLease;if(lease is null)return;
   if(!force&&System.Diagnostics.Stopwatch.GetTimestamp()<lease.Expires)return;
   speedLease=null;
   try
   {
    if(pid!=lease.Pid||Identity(pid)!=lease.Stamp)return;
    var mem=new SceneMemory(Read);
    if(mem.Type(lease.Humanoid)!="N3RBX8HumanoidE"||mem.U64(lease.Humanoid+Offsets.Instance.Parent)!=lease.Character)return;
    float current=mem.Floats(lease.Humanoid+Offsets.Humanoid.WalkSpeed,1)[0];
    if(!float.IsFinite(current)||Math.Abs(current-lease.Applied)>.05f)return;
    ulong address=lease.Humanoid+Offsets.Humanoid.WalkSpeed;
    if(!ReadMaps(pid).Any(m=>m.Start<=address&&address+4<=m.End&&m.Permissions.StartsWith("rw")))return;
    speedMemory??=File.OpenHandle($"/proc/{pid}/mem",FileMode.Open,FileAccess.Write,FileShare.ReadWrite,FileOptions.RandomAccess);
    RandomAccess.Write(speedMemory,BitConverter.GetBytes(lease.Original),(long)address);
   }
   catch(IOException){}catch(UnauthorizedAccessException){}
  }
 }
 object SetLocalWalkSpeed(JsonElement request)
 {
  if(!allowSpeedWrites)throw new IOException("Speed control needs a bridge started with --allow-speed-writes; restart the managed session");
  if(request.GetProperty("pid").GetInt32()!=pid)throw new IOException("Sober process changed");
  ulong baseAddress=request.GetProperty("baseAddress").GetUInt64(),workspace=request.GetProperty("workspace").GetUInt64();
  bool enabled=request.GetProperty("enabled").GetBoolean();float speed=request.GetProperty("speed").GetSingle();ValidateWalkSpeed(speed);
  if(baseAddress>long.MaxValue-Offsets.WorkspaceVtable)throw new IOException("Invalid Workspace address");
  foreach(var fingerprint in Offsets.Fingerprints)if(!Read(baseAddress+fingerprint.Key,fingerprint.Value.Length).AsSpan().SequenceEqual(fingerprint.Value))throw new IOException("Unsupported movement build");
  var mem=new SceneMemory(Read);
  if(mem.Type(workspace)!="N3RBX9WorkspaceE"||mem.U64(workspace)!=baseAddress+Offsets.WorkspaceVtable||mem.U64(workspace+Offsets.Instance.Self)!=workspace)throw new TargetUnavailableException("Workspace changed");
  ulong dataModel=mem.U64(workspace+Offsets.Instance.Parent),players=mem.ChildrenChecked(dataModel).FirstOrDefault(p=>mem.Type(p)=="N3RBX7PlayersE");
  if(players==0)throw new TargetUnavailableException("Players service unavailable");
  ulong local=mem.U64(players+Offsets.Players.LocalPlayer),character=local==0?0:mem.U64(local+Offsets.Player.Character);
  if(local==0||mem.Type(local)!="N3RBX6PlayerE"||mem.U64(local+Offsets.Instance.Parent)!=players||character==0||mem.Type(character)!="N3RBX13ModelInstanceE"||!CharacterReader.IsInWorkspace(mem,character,workspace))throw new TargetUnavailableException("Local character is unavailable");
  ulong humanoid=mem.ChildrenChecked(character).FirstOrDefault(p=>mem.U64(p+Offsets.Instance.Parent)==character&&mem.Type(p)=="N3RBX8HumanoidE");
  if(humanoid==0)throw new TargetUnavailableException("Local Humanoid is unavailable");
  ulong address=humanoid+Offsets.Humanoid.WalkSpeed;
  if(!ReadMaps(pid).Any(m=>m.Start<=address&&address+4<=m.End&&m.Permissions.StartsWith("rw")))throw new IOException("WalkSpeed is not in a writable mapping");
  lock(speedLock)
  {
   if(!enabled){RestoreSpeed(true);return new{ok=true,restored=true};}
   float current=mem.Floats(address,1)[0];if(!float.IsFinite(current)||current<0||current>1000)throw new IOException("Current WalkSpeed is invalid");
   if(speedLease is{} previous&&(previous.Humanoid!=humanoid||previous.Pid!=pid||previous.Stamp!=stamp))RestoreSpeed(true);
   float original=speedLease is{} active&&active.Humanoid==humanoid?current==active.Applied?active.Original:current:current;
   speedMemory??=File.OpenHandle($"/proc/{pid}/mem",FileMode.Open,FileAccess.Write,FileShare.ReadWrite,FileOptions.RandomAccess);
   RandomAccess.Write(speedMemory,BitConverter.GetBytes(speed),(long)address);
   speedLease=new(pid,stamp,workspace,character,humanoid,original,speed,System.Diagnostics.Stopwatch.GetTimestamp()+System.Diagnostics.Stopwatch.Frequency);
  }
  return new{ok=true,expiresMs=1000};
 }
 public static void ValidateAimPrediction(Vector3 velocity,float leadSeconds,float gravity)
 {
  if(!float.IsFinite(velocity.X+velocity.Y+velocity.Z)||velocity.Length()>1000||!float.IsFinite(leadSeconds)||leadSeconds<0||leadSeconds>1||!float.IsFinite(gravity)||gravity<0||gravity>2000)throw new IOException("Invalid predictive aim input");
 }
 public void Run(CancellationToken token)
 {
  uint uid=BridgeOwner();if(File.Exists(path))throw new IOException("Bridge socket already exists; stop the previous helper first");
  using var server=new Socket(AddressFamily.Unix,SocketType.Stream,ProtocolType.Unspecified);server.Bind(new UnixDomainSocketEndPoint(path));File.SetUnixFileMode(path,UnixFileMode.UserRead|UnixFileMode.UserWrite);if(geteuid()==0&&chown(path,uid,uint.MaxValue)!=0)throw new IOException("Could not assign socket ownership");server.Listen(4);Console.WriteLine($"Memory bridge ready (camera writes {(allowCameraWrites?"enabled":"disabled")}): {path}");
  using var registration=token.Register(()=>server.Close());
  var restoreTask=Task.Run(async()=>{while(!token.IsCancellationRequested){try{await Task.Delay(100,token);}catch(OperationCanceledException){break;}RestoreSpeed();}});
  try{while(!token.IsCancellationRequested)
  {
   using var client=server.Accept();uint size=12;if(getsockopt((int)client.Handle,1,17,out var credentials,ref size)!=0||credentials.Uid!=uid)continue;client.ReceiveTimeout=5000;client.SendTimeout=5000;
   object response;try
   {
    using var stream=new NetworkStream(client,false);var request=new List<byte>();while(request.Count<=4096){int b=stream.ReadByte();if(b<0||b==10)break;request.Add((byte)b);}if(request.Count>4096)throw new IOException("Request too large");using var json=JsonDocument.Parse(request.ToArray());int target=Target(uid);var root=json.RootElement;
    response=root.GetProperty("op").GetString() switch{"maps"=>new{pid=target,maps=ReadMaps(target)},"read"=>new{data=Convert.ToBase64String(Read(root.GetProperty("address").GetUInt64(),root.GetProperty("length").GetInt32()))},"aim_camera"=>AimCamera(root),"set_local_walk_speed"=>SetLocalWalkSpeed(root),_=>throw new IOException("Supported operations: maps, read, aim_camera, set_local_walk_speed")};
   }catch(Exception e){response=new{error=e.Message,code=e is TargetUnavailableException?"target_unavailable":"request_failed"};}
   try{byte[] data=Encoding.UTF8.GetBytes(JsonSerializer.Serialize(response,jsonOptions)+"\n");int done=0;while(done<data.Length)done+=client.Send(data.AsSpan(done));}catch(SocketException){}
  }}catch(SocketException)when(token.IsCancellationRequested){}catch(ObjectDisposedException)when(token.IsCancellationRequested){}finally{RestoreSpeed(true);cameraMemory?.Dispose();speedMemory?.Dispose();memory?.Dispose();File.Delete(path);}
 }
}
