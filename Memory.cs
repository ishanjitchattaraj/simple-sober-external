using System.Buffers.Binary;
using System.Net.Sockets;
using System.Numerics;
using System.Text;
using System.Text.Json;
namespace SoberEsp;
public record Mapping(ulong Start,ulong End,string Permissions,ulong Offset,string Path);
public record ProcessMaps(int Pid,List<Mapping> Maps);
public sealed class MemoryBridge(string path)
{
 public string Path=>path;
 public JsonDocument Request(object request)
 {
  using var socket=new Socket(AddressFamily.Unix,SocketType.Stream,ProtocolType.Unspecified);
  socket.ReceiveTimeout=5000;socket.SendTimeout=5000;socket.Connect(new UnixDomainSocketEndPoint(path));
  var bytes=Encoding.UTF8.GetBytes(JsonSerializer.Serialize(request)+"\n");int sent=0;while(sent<bytes.Length){int n=socket.Send(bytes.AsSpan(sent));if(n==0)throw new IOException("Bridge disconnected during send");sent+=n;}
  using var buffer=new MemoryStream();var chunk=new byte[65536];
  while(true){int n=socket.Receive(chunk);if(n==0)throw new IOException("Memory bridge disconnected");int end=Array.IndexOf(chunk,(byte)10,0,n);buffer.Write(chunk,0,end<0?n:end);if(end>=0)break;if(buffer.Length>32*1024*1024)throw new IOException("Bridge response too large");}
  var json=JsonDocument.Parse(buffer.ToArray());if(json.RootElement.TryGetProperty("error",out var error)){string message=error.GetString()??"Bridge error";bool transient=json.RootElement.TryGetProperty("code",out var code)&&code.GetString()=="target_unavailable";json.Dispose();if(transient)throw new TargetUnavailableException(message);throw new IOException(message);}return json;
 }
 public ProcessMaps GetMaps()
 {
  using var json=Request(new{op="maps"});var root=json.RootElement;
  return new(root.GetProperty("pid").GetInt32(),root.GetProperty("maps").EnumerateArray().Select(r=>new Mapping(r.GetProperty("start").GetUInt64(),r.GetProperty("end").GetUInt64(),r.GetProperty("permissions").GetString()!,r.GetProperty("offset").GetUInt64(),r.GetProperty("path").GetString()!)).ToList());
 }
 public void AimCamera(CameraContext context,ulong model,bool head,float response,Vector3 velocity=default,float leadSeconds=0,float gravity=0)
 {
  using var json=Request(new{op="aim_camera",pid=context.Pid,baseAddress=context.Base,workspace=context.Workspace,camera=context.Camera,model,head,response,velocity=new{x=velocity.X,y=velocity.Y,z=velocity.Z},leadSeconds,gravity});
 }
 public byte[] Read(ulong address,int length)
 {
  using var json=Request(new{op="read",address,length});return Convert.FromBase64String(json.RootElement.GetProperty("data").GetString()!);
 }
}
public sealed class SceneMemory(Func<ulong,int,byte[]> readMemory)
{
 public SceneMemory(MemoryBridge bridge):this(bridge.Read){}
 readonly Dictionary<ulong,byte[]> pages=[];
 public byte[] Read(ulong address,int length)
 {
  if(length<0||length>1048576)throw new IOException("Invalid memory range");var result=new byte[length];int done=0;
  while(done<length){ulong page=address&~4095UL;int offset=(int)(address-page),take=Math.Min(length-done,4096-offset);if(!pages.TryGetValue(page,out var data)){data=readMemory(page,4096);pages[page]=data;}Buffer.BlockCopy(data,offset,result,done,take);done+=take;address+=(uint)take;}return result;
 }
 public ulong U64(ulong address)=>BinaryPrimitives.ReadUInt64LittleEndian(Read(address,8));
 public float[] Floats(ulong address,int count){var b=Read(address,count*4);var result=new float[count];Buffer.BlockCopy(b,0,result,0,b.Length);return result;}
 public string Text(ulong address)
 {
  var d=Read(address,Offsets.Text.Size);if((d[0]&1)==0)return Encoding.UTF8.GetString(d,Offsets.Text.ShortData,Math.Min(d[0]>>1,Offsets.Text.ShortCapacity));ulong n=BinaryPrimitives.ReadUInt64LittleEndian(d.AsSpan(Offsets.Text.Length));if(n>256)return "?";return Encoding.UTF8.GetString(Read(BinaryPrimitives.ReadUInt64LittleEndian(d.AsSpan(Offsets.Text.Data)),(int)n));
 }
 public string Name(ulong address){try{return Text(U64(address+Offsets.Instance.Name)+Offsets.Instance.NameText);}catch{return "?";}}
 public string Type(ulong address)
 {
  try{ulong v=U64(address),ti=U64(v-Offsets.Rtti.VtableTypeInfoBack),name=U64(ti+Offsets.Rtti.TypeName);var data=Read(name,96);int end=Array.IndexOf(data,(byte)0);return Encoding.ASCII.GetString(data,0,end<0?data.Length:end);}catch{return "?";}
 }
 public List<ulong> Children(ulong address){try{return ChildrenChecked(address);}catch{return [];}}
 public List<ulong> ChildrenChecked(ulong address)
 {
  ulong vector=U64(address+Offsets.Instance.Children);if(vector==0)return [];
  ulong begin=U64(vector+Offsets.Instance.ChildrenBegin),end=U64(vector+Offsets.Instance.ChildrenEnd);
  if(end<begin||end-begin>160000||(end-begin)%Offsets.Instance.ChildStride!=0)throw new IOException("Instance children changed or are invalid");
  var data=Read(begin,(int)(end-begin));var result=new List<ulong>();
  for(int i=0;i<data.Length;i+=Offsets.Instance.ChildStride){ulong p=BinaryPrimitives.ReadUInt64LittleEndian(data.AsSpan(i));if(p!=0)result.Add(p);}
  return result;
 }
 public (List<ulong> Children,int Total) ChildrenPageChecked(ulong address,int offset,int limit)
 {
  if(limit<1||limit>128||offset<0)throw new IOException("Invalid child page");
  ulong vector=U64(address+Offsets.Instance.Children);if(vector==0)return([],0);
  ulong begin=U64(vector+Offsets.Instance.ChildrenBegin),end=U64(vector+Offsets.Instance.ChildrenEnd);
  if(end<begin||end-begin>160000|| (end-begin)%Offsets.Instance.ChildStride!=0)throw new IOException("Instance children changed or are invalid");
  int total=(int)((end-begin)/Offsets.Instance.ChildStride);if(offset>total)throw new IOException("Children changed; refresh this branch");
  int take=Math.Min(limit,total-offset);if(take==0)return([],total);
  ulong start=begin+(ulong)offset*Offsets.Instance.ChildStride;byte[] data=Read(start,take*Offsets.Instance.ChildStride);var result=new List<ulong>(take);
  for(int i=0;i<data.Length;i+=Offsets.Instance.ChildStride){ulong child=BinaryPrimitives.ReadUInt64LittleEndian(data.AsSpan(i));if(child!=0)result.Add(child);}
  return(result,total);
 }
}
