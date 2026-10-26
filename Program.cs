using System.Numerics;
using System.Text.Json;
using System.Runtime.InteropServices;
using SoberEsp;
public static class Program
{
 [STAThread]public static int Main(string[] args)
 {
  string? Option(string name){int i=Array.IndexOf(args,name);return i>=0&&i+1<args.Length?args[i+1]:null;}
  string home=Path.GetFullPath(Option("--home")??Environment.GetEnvironmentVariable("SOBER_ESP_HOME")??AppContext.BaseDirectory);Directory.CreateDirectory(home);string socket=Option("--socket")??Path.Combine(home,"memory.sock");
  using var cancel=new CancellationTokenSource();Console.CancelKeyPress+=(_,e)=>{e.Cancel=true;cancel.Cancel();};
  using var terminate=PosixSignalRegistration.Create(PosixSignal.SIGTERM,context=>{context.Cancel=true;cancel.Cancel();});
  using var hangup=PosixSignalRegistration.Create(PosixSignal.SIGHUP,context=>{context.Cancel=true;cancel.Cancel();});
  try
  {
   if(args.Contains("--self-test")){SelfTest();return 0;}
   if(args.Contains("--bridge"))
   {
    if(Option("--watch-pid") is {} watch)
    {
     int watched=int.Parse(watch);
     _=Task.Run(async()=>{try{using var process=System.Diagnostics.Process.GetProcessById(watched);await process.WaitForExitAsync(cancel.Token);}catch(ArgumentException){}catch(OperationCanceledException){}finally{cancel.Cancel();}});
    }
    uint? owner=Option("--owner-uid") is{} ownerText?uint.Parse(ownerText):null;
    new BridgeServer(socket,args.Contains("--allow-camera-writes"),owner).Run(cancel.Token);return 0;
   }
   OpenTK.Windowing.GraphicsLibraryFramework.GLFW.InitHint(OpenTK.Windowing.GraphicsLibraryFramework.InitHintPlatform.Platform,OpenTK.Windowing.GraphicsLibraryFramework.Platform.X11);
   var bridge=new MemoryBridge(socket);var tracker=new Tracker(bridge,home);
   if(args.Contains("--render-test")){using var test=new Overlay(tracker,home,true);test.Run();return 0;}
   var worker=Task.Run(()=>tracker.Run(cancel.Token));
   if(args.Contains("--snapshot"))
   {
    var end=DateTime.UtcNow.AddSeconds(30);while(DateTime.UtcNow<end&&tracker.Latest.Status!="Live"&&!cancel.IsCancellationRequested)Thread.Sleep(50);var state=tracker.Latest;if(state.Status!="Live")throw new IOException(state.Status);Console.WriteLine(JsonSerializer.Serialize(state,new JsonSerializerOptions{WriteIndented=true,IncludeFields=true}));cancel.Cancel();return 0;
   }
   if(args.Contains("--inspect-scene"))
   {
    var end=DateTime.UtcNow.AddSeconds(30);while(DateTime.UtcNow<end&&tracker.Latest.Status!="Live"&&!cancel.IsCancellationRequested)Thread.Sleep(50);var state=tracker.Latest;if(state.Status!="Live"||state.CameraContext is not{} scene)throw new IOException(state.Status);
    var visited=new HashSet<ulong>();var queue=new Queue<(ulong Address,int Depth,string Path)>();queue.Enqueue((0,0,"DataModel"));
    while(queue.Count>0&&visited.Count<512)
    {
     var item=queue.Dequeue();if(item.Depth>3||item.Address!=0&&!visited.Add(item.Address))continue;
     var branch=tracker.ReadExplorer(scene,item.Address,0,cancel.Token);var node=branch.Node;
     Console.WriteLine($"{item.Path}: {node.Name} [{node.ClassName}] raw={node.RawType} address=0x{node.Address:X} children={branch.Total}"+(node.Position is{} pos?$" position=({pos.X:F2},{pos.Y:F2},{pos.Z:F2})":""));
     foreach(var child in branch.Children)
     {
      Console.WriteLine($"  {child.Name} [{child.ClassName}] raw={child.RawType} address=0x{child.Address:X}"+(child.Position is{} childPos?$" position=({childPos.X:F2},{childPos.Y:F2},{childPos.Z:F2})":""));
      if(item.Depth<3&&(child.ClassName is "Players" or "Workspace" or "Lighting" or "Teams" or "StarterPack" or "ReplicatedStorage" or "Player" or "ModelInstance" or "Folder" or "Tool" or "Humanoid" or "Camera"))queue.Enqueue((child.Address,item.Depth+1,item.Path+"/"+child.Name));
     }
     if(branch.NextOffset<branch.Total)Console.WriteLine($"  … truncated {branch.Total-branch.NextOffset} children");
    }
    Console.WriteLine($"Live scene: PID {state.Pid}, {state.Tracked} tracked models, {visited.Count} expanded nodes");cancel.Cancel();return 0;
   }
   if(Option("--compare-image") is{} imagePath)
   {
    var end=DateTime.UtcNow.AddSeconds(30);while(DateTime.UtcNow<end&&tracker.Latest.Status!="Live"&&!cancel.IsCancellationRequested)Thread.Sleep(50);var state=tracker.Latest;if(state.Status!="Live"||state.CameraContext is not{} scene)throw new IOException(state.Status);
    var maps=bridge.GetMaps();var imageMap=maps.Maps.FirstOrDefault(m=>m.Start==scene.Base&&m.Path.Contains("memfd:"));if(imageMap is null)throw new IOException("Supported image mapping is unavailable");
    using var file=File.OpenRead(imagePath);var buffer=new byte[1024*1024];long compared=0,mismatches=0;long firstMismatch=-1;
    foreach(var mapping in maps.Maps.Where(m=>m.Path==imageMap.Path&&m.Permissions.Contains('r')).OrderBy(m=>m.Offset))
    {
     ulong offset=0;while(offset<mapping.End-mapping.Start)
     {
      int count=(int)Math.Min((ulong)buffer.Length,mapping.End-mapping.Start-offset);byte[] live=bridge.Read(mapping.Start+offset,count);int available=mapping.Offset+offset>=(ulong)file.Length?0:(int)Math.Min((ulong)count,(ulong)file.Length-(mapping.Offset+offset));
      if(available>0){file.Position=(long)(mapping.Offset+offset);int done=0;while(done<available){int n=file.Read(buffer,done,available-done);if(n==0)throw new EndOfStreamException();done+=n;}for(int i=0;i<available;i++)if(live[i]!=buffer[i]){mismatches++;if(firstMismatch<0)firstMismatch=(long)(mapping.Offset+offset+(ulong)i);}compared+=available;}
      offset+=(uint)count;
     }
    }
    Console.WriteLine($"Compared {compared:N0} mapped bytes from PID {state.Pid} against {imagePath}: {mismatches:N0} mismatches"+(firstMismatch>=0?$", first file offset 0x{firstMismatch:X}":"; all comparable mapped bytes match"));cancel.Cancel();return 0;
   }
   using var instance=new FileStream(Path.Combine(home,"overlay.lock"),FileMode.OpenOrCreate,FileAccess.ReadWrite,FileShare.None);File.WriteAllText(Path.Combine(home,"overlay.pid"),Environment.ProcessId.ToString());
   try{using var overlay=new Overlay(tracker,home);using var registration=cancel.Token.Register(()=>overlay.Close());overlay.Run();}finally{cancel.Cancel();File.Delete(Path.Combine(home,"overlay.pid"));}
   return 0;
  }
  catch(Exception e){Console.Error.WriteLine(e);return 1;}
 }
 static void SelfTest()
 {
  float[] identity=[1,0,0,0,1,0,0,0,1,0,0,0];var center=Tracker.Project(new(0,0,-10),identity,MathF.PI/2,1000,1000);if(center is null||Vector3.Distance(center.Value,new(500,500,10))>.01)throw new Exception("Center projection failed");
  var axes=Tracker.Project(new(1,1,-10),identity,MathF.PI/2,1000,1000);if(axes is null||Vector3.Distance(axes.Value,new(550,450,10))>.01)throw new Exception("Axes projection failed");
  if(Tracker.Project(new(0,0,10),identity,1,100,100)!=null||Tracker.Project(new(float.NaN,0,-10),identity,1,100,100)!=null)throw new Exception("Clipping failed");
  float[] rotated=[0,0,1,0,1,0,-1,0,0,20,3,4];var result=Tracker.Project(new(10,3,4),rotated,MathF.PI/2,1000,1000);if(result is null||Vector3.Distance(result.Value,new(500,500,10))>.01)throw new Exception("Rotated projection failed");
  var maps=BridgeServer.ReadMaps(Environment.ProcessId);if(maps.Count==0||maps.Any(m=>m.Start>=m.End))throw new Exception("Memory-map parsing failed");if(Tracker.Project(new(0,0,-10),identity,float.NaN,100,100)!=null)throw new Exception("NaN FOV accepted");
  if(BridgeServer.ResolveOwnerUid(0,null,null,new uint[]{1000})!=1000||BridgeServer.ResolveOwnerUid(0,1001,null,new uint[]{1000})!=1001||BridgeServer.ResolveOwnerUid(0,null,1002,new uint[]{1000,1001})!=1002)throw new Exception("Bridge owner selection failed");
  try{BridgeServer.ResolveOwnerUid(0,null,null,new uint[]{1000,1001});throw new Exception("Ambiguous bridge owner accepted");}catch(IOException){}
  var settings=new Settings();var entity=new Entity("test",new(490,400,20,100),10,default,new(510,500));
  if(AimMath.Select([entity],Vector2.One,new(500,500),settings)!=entity)throw new Exception("Aim selection failed");
  settings.MaxDistance=5;if(AimMath.Select([entity],Vector2.One,new(500,500),settings)!=null)throw new Exception("Aim distance filter failed");
  settings.MaxDistance=100;settings.FovRadius=5;if(AimMath.Select([entity],Vector2.One,new(500,500),settings)!=null)throw new Exception("Aim FOV filter failed");
  var nearMouse=new Entity("same name",new(490,400,20,100),100,default,new(510,500),1,80);
  var nearPlayer=new Entity("same name",new(540,400,20,100),200,default,new(550,500),2,10);
  var ranking=new Settings();var crosshair=new Vector2(500,500);
  if(AimMath.Select([nearMouse,nearPlayer],Vector2.One,crosshair,ranking)?.Id!=1)throw new Exception("Mouse priority failed");
  var left=nearMouse with{AimPoint=new(470,500),Id=3};
  var right=nearPlayer with{AimPoint=new(510,500),Id=4};
  if(AimMath.Select([left,right],Vector2.One,crosshair,ranking)?.Id!=4)throw new Exception("Crosshair selection biased left");
  if(AimMath.Select([left with{AimPoint=new(490,500)},right with{AimPoint=new(530,500)}],Vector2.One,crosshair,ranking)?.Id!=3)throw new Exception("Crosshair selection biased right");
  if(AimMath.Select([left,right],new(2,1.5f),new(1000,750),ranking)?.Id!=4)throw new Exception("Scaled crosshair selection failed");
  ranking.TargetPriority=TargetPriority.ClosestToPlayer;
  if(AimMath.Select([nearMouse,nearPlayer],Vector2.One,crosshair,ranking)?.Id!=2)throw new Exception("Player priority used camera distance");
  if(AimMath.Select([nearPlayer with{PlayerDistance=null}],Vector2.One,crosshair,ranking)!=null)throw new Exception("Missing local position must not select a player");
  ranking.FovRadius=20;if(AimMath.Select([nearMouse,nearPlayer],Vector2.One,crosshair,ranking)?.Id!=1)throw new Exception("Player priority ignored FOV");
  ranking.FovRadius=140;ranking.TargetPriority=TargetPriority.ClosestToMouse;ranking.AimLock=true;
  var selection=new AimSelection();
  if(selection.Update([nearMouse,nearPlayer],Vector2.One,crosshair,ranking,true)?.Id!=1)throw new Exception("Initial lock failed");
  var moved=nearMouse with{AimPoint=new(560,500)};
  var challenger=nearPlayer with{AimPoint=new(501,500)};
  if(selection.Update([moved,challenger],Vector2.One,crosshair,ranking,true)!=moved)throw new Exception("Lock hopped targets or kept stale coordinates");
  if(selection.Update([challenger],Vector2.One,crosshair,ranking,true)!=null)throw new Exception("Missing locked target caused a hop");
  if(selection.Update([moved with{AimPoint=new(900,500)},challenger],Vector2.One,crosshair,ranking,true)!=null)throw new Exception("Lock ignored FOV");
  if(selection.Update([moved with{Distance=9999},challenger],Vector2.One,crosshair,ranking,true)!=null)throw new Exception("Lock ignored range");
  selection.Update([moved,challenger],Vector2.One,crosshair,ranking,false);
  if(selection.Update([moved,challenger],Vector2.One,crosshair,ranking,true)?.Id!=2)throw new Exception("Release did not unlock");
  ranking.AimLock=false;
  if(selection.Update([nearMouse,nearPlayer],Vector2.One,crosshair,ranking,true)?.Id!=1)throw new Exception("Disabling lock did not resume ranking");
  // Default/missing player distance must remain serializable in --snapshot output.
  _=JsonSerializer.Serialize(nearMouse with{PlayerDistance=null});
  var step=AimMath.Motion(new(100,0),1,8,1d/60);if(step.X<=0||step.X>=100||step.Y!=0)throw new Exception("Aim smoothing failed");
  if(AimMath.Motion(new(10000,0),3,1,1).Length()>40.01f)throw new Exception("Aim motion cap failed");
  var predicted=nearMouse with{PredictedPoint=nearMouse.AimPoint+new Vector2(40,0),HorizontalPredictedPoint=nearMouse.AimPoint+new Vector2(30,0),VerticalPredictedPoint=nearMouse.AimPoint+new Vector2(0,20)};
  var predictionSettings=new Settings{AimPrediction=true,PredictionSeconds=.5f,HorizontalPrediction=true,VerticalPrediction=false};
  if(Vector2.Distance(AimMath.Point(predicted,Vector2.One,predictionSettings),nearMouse.AimPoint+new Vector2(15,0))>.01f)throw new Exception("Horizontal prediction lead failed");
  predictionSettings.VerticalPrediction=true;if(Vector2.Distance(AimMath.Point(predicted,Vector2.One,predictionSettings),nearMouse.AimPoint+new Vector2(15,10))>.01f)throw new Exception("Combined horizontal/vertical prediction failed");
  var filters=new Settings{MaxDistance=1000,FovRadius=100,DynamicFov=true};var edge=nearMouse with{Distance=500,AimPoint=new Vector2(575,500)};
  if(AimMath.Eligible(edge,Vector2.One,new(500,500),filters))throw new Exception("Dynamic FOV did not shrink with distance");
  filters.DynamicFov=false;if(!AimMath.Eligible(edge,Vector2.One,new(500,500),filters))throw new Exception("Static FOV rejected valid target");
  filters.Whitelist.Add(edge.Name);if(AimMath.Eligible(edge,Vector2.One,new(500,500),filters))throw new Exception("Whitelist did not exclude target");
  filters.Whitelist.Clear();filters.HorizontalVelocityCheck=true;filters.MinHorizontalVelocity=10;if(AimMath.Eligible(edge with{Velocity=new(2,0,0)},Vector2.One,new(500,500),filters))throw new Exception("Horizontal velocity filter failed");
  var healthFilters=new Settings{MaxDistance=1000,FovRadius=100,AimHealthCheck=true,MinHealthPercent=50};if(AimMath.Eligible(edge with{HealthFraction=null},Vector2.One,new(500,500),healthFilters)||AimMath.Eligible(edge with{HealthFraction=.4f},Vector2.One,new(500,500),healthFilters)||!AimMath.Eligible(edge with{HealthFraction=.75f},Vector2.One,new(500,500),healthFilters))throw new Exception("Health threshold filter failed");healthFilters.AimHealthCheck=false;healthFilters.AimDeadCheck=true;if(AimMath.Eligible(edge with{HealthFraction=null},Vector2.One,new(500,500),healthFilters)||AimMath.Eligible(edge with{HealthFraction=0},Vector2.One,new(500,500),healthFilters)||!AimMath.Eligible(edge with{HealthFraction=.1f},Vector2.One,new(500,500),healthFilters))throw new Exception("Dead target filter failed");
  var npcEdge=edge with{IsNpc=true};if(AimMath.Eligible(npcEdge,Vector2.One,new(500,500),filters))throw new Exception("NPC was aimable while the NPC option was disabled");filters.HorizontalVelocityCheck=false;filters.AimNpcs=true;if(!AimMath.Eligible(npcEdge,Vector2.One,new(500,500),filters))throw new Exception("NPC option did not allow a valid humanoid target");
  var distanceSettings=new Settings{DistanceSmoothing=true,MaxDistance=1000};if(AimMath.Response(distanceSettings,1d/60,900)>=AimMath.Response(distanceSettings,1d/60,0))throw new Exception("Distance smoothing did not slow distant motion");
  var full=AimMath.Motion(new(10,0),1,8,1d/30);
  var half=AimMath.Motion(new(10,0),1,8,1d/60);
  var halfAgain=AimMath.Motion(new Vector2(10,0)-half,1,8,1d/60);
  if(Vector2.Distance(full,half+halfAgain)>.001f)throw new Exception("Aim frame-rate independence failed");
  CameraTests();ExplorerTests.Run();CharacterTrackingTests.Run();
  string settingsPath=Path.Combine(Path.GetTempPath(),"sober-settings-"+Guid.NewGuid()+".json");
  try{var saved=new Settings{FovRadius=250,Smoothness=12,Names=false,Sensitivity=20,AimLock=true,TargetPriority=TargetPriority.ClosestToPlayer,AimMethod=AimMethod.Camera,EspColor=new(.1f,.2f,.3f,.4f),Whitelist=["test"]};saved.Save(settingsPath);var loaded=Settings.Load(settingsPath);if(loaded.FovRadius!=250||loaded.Smoothness!=12||loaded.Names||loaded.Sensitivity!=20||!loaded.AimLock||loaded.TargetPriority!=TargetPriority.ClosestToPlayer||loaded.AimMethod!=AimMethod.Camera||loaded.Whitelist.Count!=1||loaded.Whitelist[0]!="test"||Math.Abs(loaded.EspColor.X-.1f)>.001)throw new Exception("Settings round-trip failed");File.WriteAllText(settingsPath,"invalid");if(Settings.Load(settingsPath).AimEnabled)throw new Exception("Invalid settings fallback failed");}finally{File.Delete(settingsPath);}
  Console.WriteLine("Self-tests passed: fast movement, close/ragdoll projection, head failures, part replacement, nested characters, explorer hierarchy/pagination/cancellation, camera rotation/projection/smoothing, centered/scaled crosshair selection, target priorities, stable aim lock, release/reacquisition, settings persistence/fallback, frame-rate independence, aim selection, distance/FOV filters, smoothing, motion cap, NaN FOV, projection, axes, rotation, clipping and /proc map parsing.");
 }
 static void CameraTests()
 {
  BridgeServer.ValidateAimPrediction(Vector3.Zero,0,0); // Existing camera requests with no prediction remain valid.
  foreach(var badPrediction in new[]{(new Vector3(float.NaN,0,0),.1f,0f),(new Vector3(float.PositiveInfinity,0,0),.1f,0f),(new Vector3(1001,0,0),.1f,0f),(new Vector3(0,0,0),1.01f,0f),(new Vector3(0,0,0),float.NaN,0f),(new Vector3(0,0,0),.1f,float.PositiveInfinity),(new Vector3(0,0,0),.1f,2001f)})
  {
   bool rejected=false;try{BridgeServer.ValidateAimPrediction(badPrediction.Item1,badPrediction.Item2,badPrediction.Item3);}catch(IOException){rejected=true;}if(!rejected)throw new Exception("Unsafe predictive camera input was accepted");
  }
  float[] original=[1,0,0,0,1,0,0,0,1,20,3,4];
  foreach(var target in new Vector3[]{new(30,5,-6),new(10,1,-6),new(20,13,4),new(20,-7,4),new(20,3,14)})
  {
   var rotation=CameraAim.Rotation(original,target,1);var aimed=(float[])original.Clone();Array.Copy(rotation,aimed,rotation.Length);
   var projected=Tracker.Project(target,aimed,MathF.PI/2,1000,800);
   if(projected is null||Vector2.Distance(new(projected.Value.X,projected.Value.Y),new(500,400))>.01f)throw new Exception("Camera rotation did not center target");
   if(aimed[9]!=20||aimed[10]!=3||aimed[11]!=4)throw new Exception("Camera aim changed position");
   // Also validate this result as the next input; covers handedness and pole rotations.
   _=CameraAim.Rotation(aimed,new(30,5,-6),.1f);
  }
  var unchanged=CameraAim.Rotation(original,new(30,3,-6),0);
  if(Enumerable.Range(0,9).Any(i=>Math.Abs(unchanged[i]-original[i])>.0001f))throw new Exception("Zero camera response changed orientation");
  var half=CameraAim.Rotation(original,new(30,3,-6),.5f);var halfway=(float[])original.Clone();Array.Copy(half,halfway,9);
  var before=Tracker.Project(new(30,3,-6),original,MathF.PI/2,1000,800)!.Value;
  var after=Tracker.Project(new(30,3,-6),halfway,MathF.PI/2,1000,800)!.Value;
  if(!(after.X>500&&after.X<before.X))throw new Exception("Camera smoothing did not approach the target");
  try{CameraAim.Rotation(original,new(float.NaN,0,0),1);throw new Exception("Camera accepted NaN target");}catch(IOException){}
  try{CameraAim.Rotation(original,new(20,3,4),1);throw new Exception("Camera accepted zero target direction");}catch(IOException){}
  var invalid=(float[])original.Clone();invalid[0]=2;
  try{CameraAim.Rotation(invalid,new(30,3,-6),1);throw new Exception("Camera accepted invalid basis");}catch(IOException){}
  if(half.Length*sizeof(float)!=Offsets.Camera.RotationBytes)throw new Exception("Camera write size mismatch");
 }

}
