using ImGuiNET;
using System.Numerics;
namespace SoberEsp;

public sealed class GameExplorer(Func<CameraContext,ulong,int,CancellationToken,ExplorerBranch> read):IDisposable
{
 readonly Dictionary<ulong,ExplorerBranch> branches=[];
 readonly Dictionary<ulong,ExplorerNode> nodes=[];
 readonly Queue<(ulong Address,int Offset)> queue=[];
 readonly HashSet<(ulong,int)> queued=[];
 readonly Dictionary<ulong,string> errors=[];
 Task<ExplorerBranch>? pending;CancellationTokenSource? cancellation;
 (ulong Address,int Offset) pendingKey;CameraContext? context,pendingContext,previewContext;
 ulong root,selected;int treeId;string message="";
 const int MaxNodes=4096;
 static bool SameScene(CameraContext? a,CameraContext? b)=>a?.Pid==b?.Pid&&a?.Workspace==b?.Workspace&&a?.Base==b?.Base;
 void Clear()
 {
  Pause();branches.Clear();nodes.Clear();errors.Clear();root=selected=0;treeId++;message="";
 }
 public void Pause(){queue.Clear();queued.Clear();cancellation?.Cancel();}
 void Request(ulong address,int offset=0)
 {
  if(nodes.Count>=MaxNodes){message="Explorer cache limit reached. Refresh to browse another branch.";return;}
  var key=(address,offset);
  if(errors.ContainsKey(address)||queued.Contains(key)||(pending!=null&&pendingKey==key))return;
  if(queue.Count<128){queue.Enqueue(key);queued.Add(key);}
 }
 void Pump()
 {
  if(pending is {IsCompleted:true})
  {
   try
   {
    var result=pending.GetAwaiter().GetResult();
    if(!cancellation!.IsCancellationRequested&&SameScene(context,pendingContext))
    {
     if(pendingKey.Address==0){root=result.Node.Address;if(selected==0)selected=root;}
     if(result.Offset>0&&branches.TryGetValue(result.Node.Address,out var previous))
      result=result with{Children=previous.Children.Concat(result.Children).DistinctBy(n=>n.Address).ToArray(),Skipped=previous.Skipped+result.Skipped};
     branches[result.Node.Address]=result;nodes[result.Node.Address]=result.Node;
     foreach(var child in result.Children)nodes[child.Address]=child;
     errors.Remove(result.Node.Address);
    }
   }
   catch(OperationCanceledException){}
   catch(Exception error){if(!cancellation!.IsCancellationRequested&&SameScene(context,pendingContext))errors[pendingKey.Address]=error.Message;}
   finally{pending=null;cancellation?.Dispose();cancellation=null;}
  }
  if(pending==null&&queue.TryDequeue(out var request)&&context is {} scene)
  {
   queued.Remove(request);pendingKey=request;pendingContext=scene;cancellation=new();var token=cancellation.Token;
   pending=Task.Run(()=>read(scene,request.Address,request.Offset,token),token);
  }
 }
 void Node(ExplorerNode node,HashSet<ulong> ancestors,int depth=0)
 {
  if(depth>=32||!ancestors.Add(node.Address)){ImGui.TextDisabled("Cycle or depth limit reached");return;}
  bool loaded=branches.TryGetValue(node.Address,out var branch);
  var flags=ImGuiTreeNodeFlags.OpenOnArrow|ImGuiTreeNodeFlags.SpanAvailWidth;
  if(selected==node.Address)flags|=ImGuiTreeNodeFlags.Selected;
  if(loaded&&branch!.Total==0)flags|=ImGuiTreeNodeFlags.Leaf;
  if(node.Address==root)flags|=ImGuiTreeNodeFlags.DefaultOpen;
  string name=node.Name.Replace('#','＃').Replace('\n',' ');
  bool open=ImGui.TreeNodeEx($"{name} [{node.ClassName}]##{node.Address:X}",flags);
  if(ImGui.IsItemClicked()){selected=node.Address;if(!loaded)Request(node.Address);}
  if(open)
  {
   if(errors.TryGetValue(node.Address,out var error)){ImGui.TextWrapped(error);if(ImGui.SmallButton($"Retry##{node.Address:X}")){errors.Remove(node.Address);Request(node.Address);}}
   else if(!loaded){Request(node.Address);ImGui.TextDisabled("Loading children...");}
   else
   {
    foreach(var child in branch!.Children)Node(child,ancestors,depth+1);
    if(branch.NextOffset<branch.Total&&ImGui.SmallButton($"Load more ({branch.NextOffset}/{branch.Total})##{node.Address:X}"))Request(node.Address,branch.NextOffset);
    if(branch.Skipped>0)ImGui.TextDisabled($"{branch.Skipped} unavailable or moved children skipped");
   }
   ImGui.TreePop();
  }
  ancestors.Remove(node.Address);
 }
 string Path(ExplorerNode node)
 {
  var names=new List<string>();var visited=new HashSet<ulong>();var current=node;
  while(visited.Add(current.Address)&&names.Count<32){names.Add(current.Name);if(!nodes.TryGetValue(current.Parent,out var parent))break;current=parent;}
  names.Reverse();return string.Join(" / ",names);
 }
 public void Draw(Snapshot state)
 {
  var scene=previewContext??state.CameraContext;
  if(!SameScene(context,scene)){Clear();context=scene;}
  Pump();
  if(ImGui.Button("Refresh hierarchy"))Clear();ImGui.SameLine();ImGui.TextDisabled(pending!=null?"Loading...":"Read-only explorer");
  ImGui.TextWrapped("Expand arrows to load children. Select an instance to inspect it. Scroll either pane with the mouse wheel.");
  if(context==null){ImGui.TextWrapped("Waiting for a playable scene: "+state.Status);return;}
  if(root==0)
  {
   if(errors.TryGetValue(0,out var error))ImGui.TextWrapped(error);else{Request(0);Pump();ImGui.TextDisabled("Loading game root...");}
   return;
  }
  if(message.Length>0)ImGui.TextWrapped(message);
  var size=ImGui.GetContentRegionAvail();float left=Math.Max(180,size.X*.56f);
  ImGui.PushID(treeId);
  ImGui.BeginChild("hierarchy",new(left,Math.Max(140,size.Y)),ImGuiChildFlags.Borders,ImGuiWindowFlags.HorizontalScrollbar);
  if(nodes.TryGetValue(root,out var rootNode))Node(rootNode,[]);
  ImGui.EndChild();ImGui.PopID();ImGui.SameLine();
  ImGui.BeginChild("properties",new(0,Math.Max(140,size.Y)),ImGuiChildFlags.Borders);
  if(nodes.TryGetValue(selected,out var node))
  {
   ImGui.TextUnformatted(node.Name);ImGui.Separator();
   ImGui.TextWrapped("Class: "+node.ClassName);ImGui.TextWrapped($"Address: 0x{node.Address:X}");
   ImGui.TextWrapped($"Parent: 0x{node.Parent:X}");ImGui.TextWrapped("Path: "+Path(node));
   if(branches.TryGetValue(selected,out var branch))ImGui.TextWrapped($"Children: {branch.Total} ({branch.Children.Count} loaded)");
   else{Request(selected);ImGui.TextDisabled("Children: loading...");}
   if(node.Position is {} position)ImGui.TextWrapped($"Position: {position.X:F2}, {position.Y:F2}, {position.Z:F2}");
   ImGui.TextWrapped("RTTI: "+node.RawType);
   if(ImGui.Button("Refresh selected")){errors.Remove(selected);Request(selected);}
   if(errors.TryGetValue(selected,out var error))ImGui.TextWrapped(error);
  }
  else ImGui.TextWrapped("Select an instance in the hierarchy.");
  ImGui.EndChild();Pump();
 }
 public static GameExplorer CreatePreview()
 {
  var scene=new CameraContext(1,1,0x2000,0x3000);
  var root=new ExplorerNode(0x1000,"Game","DataModel","N3RBX9DataModelE",0);
  var workspace=new ExplorerNode(0x2000,"Workspace","Workspace","N3RBX9WorkspaceE",0x1000);
  var camera=new ExplorerNode(0x3000,"Camera","Camera","N3RBX6CameraE",0x2000);
  var player=new ExplorerNode(0x4000,"Player character","ModelInstance","N3RBX13ModelInstanceE",0x2000);
  var preview=new GameExplorer((_,address,_,token)=>throw new InvalidOperationException("Preview branch missing")){context=scene,previewContext=scene,root=root.Address,selected=camera.Address};
  foreach(var node in new[]{root,workspace,camera,player})preview.nodes[node.Address]=node;
  preview.branches[root.Address]=new(root,[workspace],1,1);
  preview.branches[workspace.Address]=new(workspace,[camera,player],2,2);
  preview.branches[camera.Address]=new(camera,[],0,0);preview.branches[player.Address]=new(player,[],0,0);
  return preview;
 }
 public void Dispose(){Pause();cancellation?.Dispose();}
}
