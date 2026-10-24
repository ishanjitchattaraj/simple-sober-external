using System.Diagnostics;
using System.Numerics;
namespace SoberEsp;

public record ExplorerNode(ulong Address,string Name,string ClassName,string RawType,ulong Parent,Vector3? Position=null);
public record ExplorerBranch(ExplorerNode Node,IReadOnlyList<ExplorerNode> Children,int Total,int NextOffset,int Skipped=0,int Offset=0);

public static class ExplorerReader
{
 public static string ClassName(string raw)
 {
  int index=raw.StartsWith('N')?1:0;string last="";
  while(index<raw.Length&&char.IsAsciiDigit(raw[index]))
  {
   int start=index;while(index<raw.Length&&char.IsAsciiDigit(raw[index]))index++;
   if(!int.TryParse(raw.AsSpan(start,index-start),out int length)||length<=0||length>raw.Length-index)return raw;
   last=raw.Substring(index,length);index+=length;
  }
  return last.Length>0&&index==raw.Length-(raw.StartsWith('N')?1:0)?last:raw;
 }
 static ExplorerNode Inspect(SceneMemory memory,ulong address)
 {
  string raw=memory.Type(address);if(raw=="?")throw new IOException("Instance is unavailable; refresh the hierarchy");
  string type=ClassName(raw);Vector3? position=null;
  if(type is "PartInstance" or "MeshPart" or "WedgePart" or "CornerWedgePart" or "UnionOperation")
  {
   try{var values=memory.Floats(memory.U64(address+Offsets.Part.Primitive)+Offsets.Part.Position,3);if(values.All(f=>float.IsFinite(f)&&Math.Abs(f)<1e7))position=new(values[0],values[1],values[2]);}catch(IOException){}
  }
  return new(address,memory.Name(address),type,raw,memory.U64(address+Offsets.Instance.Parent),position);
 }
 public static ExplorerBranch Read(SceneMemory memory,CameraContext context,ulong address,int offset,CancellationToken token)
 {
  token.ThrowIfCancellationRequested();
  if(memory.Type(context.Workspace)!="N3RBX9WorkspaceE")throw new IOException("Scene changed; waiting for a new hierarchy");
  ulong root=memory.U64(context.Workspace+Offsets.Instance.Parent);
  if(root==0)throw new IOException("Game root is unavailable");
  if(address==0)address=root;
  var node=Inspect(memory,address);var children=memory.ChildrenChecked(address);
  if(offset<0||offset>children.Count)throw new IOException("Children changed; refresh this branch");
  var result=new List<ExplorerNode>();int next=offset,skipped=0;var timer=Stopwatch.StartNew();
  // Load one page at a time, on a worker. Never scan the complete scene for the UI.
  while(next<children.Count&&next-offset<128)
  {
   token.ThrowIfCancellationRequested();ulong child=children[next++];
   try{var info=Inspect(memory,child);if(info.Parent==address)result.Add(info);else skipped++;}catch(IOException){skipped++;}
   if(timer.Elapsed.TotalSeconds>2)break;
  }
  return new(node,result,children.Count,next,skipped,offset);
 }
}
