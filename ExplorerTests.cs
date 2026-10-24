using System.Buffers.Binary;
using System.Text;
namespace SoberEsp;

public static class ExplorerTests
{
 public static void Run()
 {
  var pages=new Dictionary<ulong,byte[]>();ulong next=0x100000;
  ulong Alloc(){ulong address=next;next+=4096;pages[address]=new byte[4096];return address;}
  void Bytes(ulong address,byte[] bytes){Buffer.BlockCopy(bytes,0,pages[address&~4095UL],(int)(address&4095),bytes.Length);}
  void U64(ulong address,ulong value){var b=new byte[8];BinaryPrimitives.WriteUInt64LittleEndian(b,value);Bytes(address,b);}
  ulong Object(string name,string type,ulong parent)
  {
   ulong address=Alloc(),vtable=Alloc()+8,info=Alloc(),raw=Alloc(),text=Alloc();
   U64(address,vtable);U64(vtable-Offsets.Rtti.VtableTypeInfoBack,info);U64(info+Offsets.Rtti.TypeName,raw);Bytes(raw,Encoding.ASCII.GetBytes(type+"\0"));
   U64(address+Offsets.Instance.Name,text);Bytes(text+Offsets.Instance.NameText,[(byte)(name.Length*2),..Encoding.UTF8.GetBytes(name)]);
   U64(address+Offsets.Instance.Parent,parent);return address;
  }
  ulong Children(ulong address,IReadOnlyList<ulong> children)
  {
   ulong vector=Alloc(),data=Alloc();U64(address+Offsets.Instance.Children,vector);U64(vector,data);U64(vector+8,data+(ulong)children.Count*16);
   for(int i=0;i<children.Count;i++)U64(data+(ulong)i*16,children[i]);return vector;
  }
  SceneMemory Memory()=>new((address,length)=>pages.TryGetValue(address,out var page)?page.ToArray():throw new IOException("Unmapped fixture page"));
  void Check(bool value,string error){if(!value)throw new Exception("Explorer: "+error);}
  ulong root=Object("Game","N3RBX9DataModelE",0),workspace=Object("Workspace","N3RBX9WorkspaceE",root);
  var children=new List<ulong>{workspace};
  for(int i=1;i<130;i++)children.Add(Object("Child"+i,"N3RBX13ModelInstanceE",root));
  var vector=Children(root,children);var scene=new CameraContext(1,0,workspace,0);
  var first=ExplorerReader.Read(Memory(),scene,0,0,CancellationToken.None);
  Check(first.Node.Address==root&&first.Node.ClassName=="DataModel","game root/class lookup");
  Check(first.Total==130&&first.Children.Count==128&&first.NextOffset==128,"first page bounds");
  var second=ExplorerReader.Read(Memory(),scene,root,first.NextOffset,CancellationToken.None);
  Check(second.Children.Count==2&&second.NextOffset==130&&second.Offset==128,"second page bounds");
  Check(!first.Children.Select(n=>n.Address).Intersect(second.Children.Select(n=>n.Address)).Any(),"pagination repeated a child");
  U64(children[1]+Offsets.Instance.Parent,workspace);
  var moved=ExplorerReader.Read(Memory(),scene,root,0,CancellationToken.None);
  Check(moved.Skipped==1&&moved.Children.Count==127,"moved children must be skipped");
  U64(vector+8,next+1000000);
  try{ExplorerReader.Read(Memory(),scene,root,0,CancellationToken.None);throw new Exception("Explorer accepted malformed children");}catch(IOException){}
  using var cancel=new CancellationTokenSource();cancel.Cancel();
  try{ExplorerReader.Read(Memory(),scene,root,0,cancel.Token);throw new Exception("Explorer ignored cancellation");}catch(OperationCanceledException){}
  Check(ExplorerReader.ClassName("N3RBX9WorkspaceE")=="Workspace"&&ExplorerReader.ClassName("unknown")=="unknown","class-name formatting");
 }
}
