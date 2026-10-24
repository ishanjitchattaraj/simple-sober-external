using System.Net.Sockets;
using System.Numerics;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using Microsoft.Win32.SafeHandles;
namespace SoberEsp;
public sealed class BridgeServer(string path,bool allowCameraWrites=false)
{
 [DllImport("libc")]static extern uint geteuid();
 [DllImport("libc")]static extern int chown(string path,uint uid,uint gid);
 [DllImport("libc")]static extern int getsockopt(int fd,int level,int option,out Credentials value,ref uint length);
 [StructLayout(LayoutKind.Sequential)]struct Credentials{public int Pid;public uint Uid,Gid;}
 readonly JsonSerializerOptions jsonOptions=new(){PropertyNamingPolicy=JsonNamingPolicy.CamelCase};
 int pid;string stamp="";SafeFileHandle? memory, cameraMemory;List<Mapping> cached=[];long cacheTime;
 static string Identity(int p)=>File.ReadAllText($"/proc/{p}/stat").Split(')').Last().Split(' ',StringSplitOptions.RemoveEmptyEntries)[19];
 public static List<Mapping> ReadMaps(int p)
 {
  var result=new List<Mapping>();foreach(string line in File.ReadLines($"/proc/{p}/maps")){var v=line.Split(' ',6,StringSplitOptions.RemoveEmptyEntries);var range=v[0].Split('-');result.Add(new(Convert.ToUInt64(range[0],16),Convert.ToUInt64(range[1],16),v[1],Convert.ToUInt64(v[2],16),v.Length>5?v[5]:""));}return result;
 }
 static uint Owner(int p)=>uint.Parse(File.ReadLines($"/proc/{p}/status").First(s=>s.StartsWith("Uid:")).Split((char[]?)null,StringSplitOptions.RemoveEmptyEntries)[1]);
 int Target(uint uid)
 {
  try{if(pid!=0&&Identity(pid)==stamp)return pid;}catch(IOException){}
  foreach(string directory in Directory.EnumerateDirectories("/proc"))try
  {
   if(!int.TryParse(System.IO.Path.GetFileName(directory),out int p)||Owner(p)!=uid||File.ReadAllText($"{directory}/comm").Trim()!="Main")continue;
   if(!File.ReadAllLines($"{directory}/root/.flatpak-info").Contains("name=org.vinegarhq.Sober"))continue;
   cameraMemory?.Dispose();cameraMemory=null;memory?.Dispose();memory=File.OpenHandle($"/proc/{p}/mem",FileMode.Open,FileAccess.Read,FileShare.ReadWrite,FileOptions.RandomAccess);pid=p;stamp=Identity(p);cached=[];Console.WriteLine($"Memory connection to Sober PID {p}");return p;
  }catch(IOException){}catch(UnauthorizedAccessException){}
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
 public void Run(CancellationToken token)
 {
  uint uid=uint.TryParse(Environment.GetEnvironmentVariable("SUDO_UID"),out uint caller)?caller:geteuid();if(File.Exists(path))throw new IOException("Bridge socket already exists; stop the previous helper first");
  using var server=new Socket(AddressFamily.Unix,SocketType.Stream,ProtocolType.Unspecified);server.Bind(new UnixDomainSocketEndPoint(path));File.SetUnixFileMode(path,UnixFileMode.UserRead|UnixFileMode.UserWrite);if(geteuid()==0&&chown(path,uid,uint.MaxValue)!=0)throw new IOException("Could not assign socket ownership");server.Listen(4);Console.WriteLine($"Memory bridge ready (camera writes {(allowCameraWrites?"enabled":"disabled")}): {path}");
  using var registration=token.Register(()=>server.Close());
  try{while(!token.IsCancellationRequested)
  {
   using var client=server.Accept();uint size=12;if(getsockopt((int)client.Handle,1,17,out var credentials,ref size)!=0||credentials.Uid!=uid)continue;client.ReceiveTimeout=5000;client.SendTimeout=5000;
   object response;try
   {
    using var stream=new NetworkStream(client,false);var request=new List<byte>();while(request.Count<=4096){int b=stream.ReadByte();if(b<0||b==10)break;request.Add((byte)b);}if(request.Count>4096)throw new IOException("Request too large");using var json=JsonDocument.Parse(request.ToArray());int target=Target(uid);var root=json.RootElement;
    response=root.GetProperty("op").GetString() switch{"maps"=>new{pid=target,maps=ReadMaps(target)},"read"=>new{data=Convert.ToBase64String(Read(root.GetProperty("address").GetUInt64(),root.GetProperty("length").GetInt32()))},"aim_camera"=>AimCamera(root),_=>throw new IOException("Supported operations: maps, read, aim_camera")};
   }catch(Exception e){response=new{error=e.Message,code=e is TargetUnavailableException?"target_unavailable":"request_failed"};}
   try{byte[] data=Encoding.UTF8.GetBytes(JsonSerializer.Serialize(response,jsonOptions)+"\n");int done=0;while(done<data.Length)done+=client.Send(data.AsSpan(done));}catch(SocketException){}
  }}catch(SocketException)when(token.IsCancellationRequested){}catch(ObjectDisposedException)when(token.IsCancellationRequested){}finally{cameraMemory?.Dispose();memory?.Dispose();File.Delete(path);}
 }
}
