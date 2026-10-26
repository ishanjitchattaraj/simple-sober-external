using Dear_ImGui_Sample.Backends;
using ImGuiNET;
using OpenTK.Graphics.OpenGL4;
using OpenTK.Mathematics;
using OpenTK.Windowing.Common;
using OpenTK.Windowing.Desktop;
using OpenTK.Windowing.GraphicsLibraryFramework;
using Num=System.Numerics;
namespace SoberEsp;
public unsafe sealed class Overlay(Tracker tracker,string home,bool renderTest=false):GameWindow(new GameWindowSettings{UpdateFrequency=60},new NativeWindowSettings{Title="Sober ImGui ESP",ClientSize=new(800,600),StartVisible=false,StartFocused=false,WindowBorder=WindowBorder.Hidden,TransparentFramebuffer=true,APIVersion=new(3,3),Profile=ContextProfile.Core})
{
 X11? x11;nuint own,game;int frames;long lastLookup;bool menu,insertWasDown,mouseWasDown;string lastStatus="",saveError="",aimError="",playerSearch="";
 readonly Settings settings=Settings.Load(Path.Combine(home,"settings.json"));
 readonly AimSelection selection=new();int aimPid;
 readonly HashSet<int> renderedTabs=[];
 readonly Dictionary<ulong,Queue<Num.Vector2>> trails=[];
 readonly GameExplorer explorer=renderTest?GameExplorer.CreatePreview():new GameExplorer(tracker.ReadExplorer);
 Num.Vector2 remainder;
 protected override void OnLoad()
 {
  base.OnLoad();x11=new();own=(nuint)GLFW.GetX11Window(WindowPtr);if(own==0)throw new IOException("Overlay X11 window not found");x11.Unmanaged(own);
  GLFW.SetWindowAttrib(WindowPtr,WindowAttribute.FocusOnShow,false);GLFW.SetWindowAttrib(WindowPtr,WindowAttribute.Floating,true);MousePassthrough=true;x11.ClickThrough(own);
  if(!HasTransparentFramebuffer)throw new IOException("A transparent OpenGL framebuffer could not be created");
  ImGui.CreateContext();var io=ImGui.GetIO();io.NativePtr->IniFilename=null;io.DisplaySize=new(800,600);io.DeltaTime=1/60f;ImguiImplOpenGL3.Init(false);VSync=VSyncMode.Off;
  ApplyStyle();
  Console.WriteLine("Overlay ready. Insert: menu. Aim: hold right mouse (disabled by default).");
 }
 static void ApplyStyle()
 {
  var s=ImGui.GetStyle();s.WindowRounding=2;s.ChildRounding=1;s.FrameRounding=2;s.PopupRounding=2;s.ScrollbarRounding=2;s.GrabRounding=2;s.TabRounding=2;
  s.WindowBorderSize=1;s.ChildBorderSize=1;s.FrameBorderSize=1;s.PopupBorderSize=1;s.TabBorderSize=1;
  s.WindowPadding=new(12,10);s.FramePadding=new(8,5);s.ItemSpacing=new(8,7);s.ItemInnerSpacing=new(6,5);s.ScrollbarSize=12;s.GrabMinSize=8;
  var c=s.Colors;
  c[(int)ImGuiCol.Text]=new(0.83f,0.82f,0.88f,1);c[(int)ImGuiCol.TextDisabled]=new(0.40f,0.39f,0.45f,1);
  c[(int)ImGuiCol.WindowBg]=new(0.035f,0.035f,0.045f,0.98f);c[(int)ImGuiCol.ChildBg]=new(0.045f,0.045f,0.055f,1);c[(int)ImGuiCol.PopupBg]=new(0.055f,0.055f,0.07f,1);
  c[(int)ImGuiCol.Border]=new(0.20f,0.19f,0.23f,0.8f);c[(int)ImGuiCol.FrameBg]=new(0.08f,0.075f,0.09f,1);c[(int)ImGuiCol.FrameBgHovered]=new(0.14f,0.11f,0.16f,1);c[(int)ImGuiCol.FrameBgActive]=new(0.17f,0.12f,0.20f,1);
  c[(int)ImGuiCol.TitleBg]=new(0.045f,0.04f,0.055f,1);c[(int)ImGuiCol.TitleBgActive]=new(0.07f,0.055f,0.085f,1);c[(int)ImGuiCol.MenuBarBg]=new(0.05f,0.045f,0.06f,1);
  c[(int)ImGuiCol.Tab]=new(0.065f,0.06f,0.075f,1);c[(int)ImGuiCol.TabHovered]=new(0.25f,0.16f,0.31f,1);c[(int)ImGuiCol.TabSelected]=new(0.15f,0.10f,0.19f,1);c[(int)ImGuiCol.TabDimmed]=new(0.05f,0.05f,0.06f,1);c[(int)ImGuiCol.TabDimmedSelected]=new(0.10f,0.075f,0.12f,1);
  c[(int)ImGuiCol.CheckMark]=new(0.72f,0.43f,0.80f,1);c[(int)ImGuiCol.SliderGrab]=new(0.65f,0.36f,0.75f,1);c[(int)ImGuiCol.SliderGrabActive]=new(0.82f,0.53f,0.91f,1);c[(int)ImGuiCol.Button]=new(0.12f,0.09f,0.15f,1);c[(int)ImGuiCol.ButtonHovered]=new(0.24f,0.15f,0.29f,1);c[(int)ImGuiCol.ButtonActive]=new(0.30f,0.18f,0.36f,1);
 }
 void Save(){try{settings.Save(Path.Combine(home,"settings.json"));saveError="";}catch(Exception e){saveError=e.Message;Console.Error.WriteLine("Settings: "+e.Message);}}
 void SetMenu(bool value)
 {
  bool returnFocus=menu&&x11!.Focused(own);
  menu=value;
  if(value)IsVisible=true;
  MousePassthrough=!value;x11!.Interactive(own,value);remainder=default;selection.Reset();
  if(value){x11.Focus(own);Console.WriteLine("Settings menu opened");}
  else{explorer.Pause();if(returnFocus&&game!=0)x11.Focus(game);Save();Console.WriteLine("Settings menu closed");}
 }
 protected override void OnResize(ResizeEventArgs e){base.OnResize(e);if(own!=0)x11?.Interactive(own,menu);}
 protected override void OnMouseWheel(MouseWheelEventArgs e)
 {
  base.OnMouseWheel(e);if(menu)ImGui.GetIO().AddMouseWheelEvent(e.OffsetX,e.OffsetY);
 }
 void DrawAimbot()
 {
  ImGui.Checkbox("Enable aim assistance (hold right mouse)",ref settings.AimEnabled);
  int method=(int)settings.AimMethod;
  if(ImGui.Combo("Aim method",ref method,"Mouse\0Camera\0")){settings.AimMethod=(AimMethod)method;selection.Reset();remainder=default;aimError="";}
  ImGui.Checkbox("Show FOV circle",ref settings.ShowFov);ImGui.SameLine();ImGui.Checkbox("Target marker",ref settings.TargetMarker);
  ImGui.SliderFloat("FOV radius (pixels)",ref settings.FovRadius,20,600,"%.0f");
  ImGui.SliderFloat("Sensitivity multiplier",ref settings.Sensitivity,.1f,20,"%.2f");
  ImGui.SliderFloat("Smoothness",ref settings.Smoothness,1,30,"%.1f");
  int curve=(int)settings.SmoothingCurve;if(ImGui.Combo("Smoothing curve",ref curve,"Linear\0Ease in\0Ease out\0"))settings.SmoothingCurve=(SmoothingCurve)curve;
  ImGui.Checkbox("Increase smoothing with distance",ref settings.DistanceSmoothing);
  ImGui.SliderFloat("Deadzone (pixels)",ref settings.Deadzone,0,100,"%.0f");
  ImGui.SliderFloat("Maximum distance",ref settings.MaxDistance,10,10000,"%.0f studs");
  ImGui.Checkbox("Dynamic FOV",ref settings.DynamicFov);
  ImGui.Checkbox("Predict target motion",ref settings.AimPrediction);if(settings.AimPrediction){ImGui.Checkbox("Projectile flight-time lead",ref settings.ProjectilePrediction);if(settings.ProjectilePrediction){ImGui.SliderFloat("Manual projectile speed",ref settings.ProjectileSpeed,1,3000,"%.0f studs/s");ImGui.SliderFloat("Manual gravity",ref settings.ProjectileGravity,0,1000,"%.0f studs/s²");ImGui.TextDisabled("Manual projectile values. Airborne/grounded state is unknown; target vertical velocity is sampled directly.");}else ImGui.SliderFloat("Prediction lead",ref settings.PredictionSeconds,.02f,1,"%.2f s");ImGui.Checkbox("Horizontal lead",ref settings.HorizontalPrediction);ImGui.SameLine();ImGui.Checkbox("Vertical lead",ref settings.VerticalPrediction);}
  ImGui.Checkbox("Horizontal velocity filter",ref settings.HorizontalVelocityCheck);if(settings.HorizontalVelocityCheck)ImGui.SliderFloat("Minimum horizontal speed",ref settings.MinHorizontalVelocity,0,200,"%.0f");
  ImGui.Checkbox("Vertical velocity filter",ref settings.VerticalVelocityCheck);if(settings.VerticalVelocityCheck)ImGui.SliderFloat("Minimum vertical speed",ref settings.MinVerticalVelocity,0,200,"%.0f");
  ImGui.Checkbox("Skip dead targets (Health = 0)",ref settings.AimDeadCheck);
  ImGui.Checkbox("Minimum health filter",ref settings.AimHealthCheck);if(settings.AimHealthCheck)ImGui.SliderFloat("Minimum health (%)",ref settings.MinHealthPercent,0,100,"%.0f%%");
  int priority=(int)settings.TargetPriority;
  if(ImGui.Combo("Target priority",ref priority,"Closest to mouse\0Closest to player\0")){settings.TargetPriority=(TargetPriority)priority;selection.Reset();}
  if(ImGui.Checkbox("Aim lock (keep target while held)",ref settings.AimLock))selection.Reset();
  ImGui.Checkbox("Aim at head (otherwise torso)",ref settings.AimAtHead);
  ImGui.TextWrapped("Higher smoothness moves more slowly. Closest to mouse uses the centered crosshair. Closest to player uses world distance. Health uses a fixed 100-point scale. Team and visibility checks plus knocked/grab state still need verified fields.");
  if(aimError.Length>0)ImGui.TextWrapped("Aim: "+aimError);
 }
 void DrawEsp()
 {
  ImGui.Checkbox("Player boxes",ref settings.Boxes);
  ImGui.Checkbox("NPCs (Workspace models with Humanoid, not Player.Character)",ref settings.EspNpcs);
  ImGui.Checkbox("Equipped Tool labels",ref settings.ToolNames);
  ImGui.Checkbox("Corner boxes",ref settings.CornerBoxes);
  ImGui.Checkbox("3D boxes",ref settings.Boxes3D);ImGui.SameLine();ImGui.Checkbox("Skeleton",ref settings.Skeleton);
  ImGui.Checkbox("Snaplines",ref settings.Snaplines);ImGui.SameLine();ImGui.Checkbox("Head dots",ref settings.HeadDots);
  ImGui.Checkbox("Offscreen arrows",ref settings.OffscreenArrows);ImGui.SameLine();ImGui.Checkbox("Motion trails",ref settings.Trails);
  ImGui.Checkbox("Radar",ref settings.Radar);ImGui.SameLine();ImGui.Checkbox("Predicted point",ref settings.Prediction);
  ImGui.Checkbox("Highlight target",ref settings.TargetHighlight);ImGui.SameLine();ImGui.Checkbox("Distance fade",ref settings.DistanceFade);
  ImGui.Checkbox("Color gradient",ref settings.Gradient);ImGui.SameLine();ImGui.Checkbox("Dark outline",ref settings.Outline);
  ImGui.ColorEdit4("ESP color",ref settings.EspColor);if(settings.Gradient)ImGui.ColorEdit4("Far color",ref settings.EspGradientColor);
  ImGui.SliderFloat("Line thickness",ref settings.Thickness,.5f,5,"%.1f px");ImGui.SliderFloat("ESP alpha",ref settings.Alpha,0,1,"%.2f");
  if(settings.DistanceFade)ImGui.SliderFloat("Fade start (studs)",ref settings.FadeStart,10,Math.Max(100,settings.MaxDistance),"%.0f");
  ImGui.SliderFloat("Maximum render distance",ref settings.MaxDistance,10,10000,"%.0f studs");
  ImGui.Checkbox("Player names",ref settings.Names);
  ImGui.Checkbox("Distances (studs)",ref settings.Distances);
  ImGui.Checkbox("Status bar",ref settings.Status);
  ImGui.Checkbox("Health bars (assumed max 100)",ref settings.HealthBars);
  ImGui.TextDisabled("Facing, visible/occluded, and team colors still need verified live fields.");
  DrawEspPreview();
 }
 static uint PackColor(System.Numerics.Vector4 color,float alpha=1)
 {
  uint r=(uint)(Math.Clamp(color.X,0,1)*255),g=(uint)(Math.Clamp(color.Y,0,1)*255),b=(uint)(Math.Clamp(color.Z,0,1)*255),a=(uint)(Math.Clamp(color.W*alpha,0,1)*255);
  return a<<24|b<<16|g<<8|r;
 }
 void DrawEspPreview()
 {
  ImGui.Separator();ImGui.Text("Live ESP preview");var start=ImGui.GetCursorScreenPos();var size=new Num.Vector2(Math.Max(180,ImGui.GetContentRegionAvail().X),180);ImGui.Dummy(size);var draw=ImGui.GetWindowDrawList();
  draw.AddRectFilled(start,start+size,0xff111017);for(int x=0;x<size.X;x+=24)draw.AddLine(start+new Num.Vector2(x,0),start+new Num.Vector2(x,size.Y),0x182a2632);for(int y=0;y<size.Y;y+=24)draw.AddLine(start+new Num.Vector2(0,y),start+new Num.Vector2(size.X,y),0x182a2632);
  var a=start+new Num.Vector2(size.X*.43f,size.Y*.2f);var b=start+new Num.Vector2(size.X*.59f,size.Y*.82f);uint col=PackColor(settings.EspColor,settings.Alpha);
  if(settings.Snaplines)draw.AddLine(start+new Num.Vector2(size.X/2,size.Y),new((a.X+b.X)/2,b.Y),col,settings.Thickness);
  if(settings.Boxes||settings.CornerBoxes){if(settings.Outline)draw.AddRect(a,b,0xff050507,0,ImDrawFlags.None,settings.Thickness+2);if(settings.Boxes)draw.AddRect(a,b,col,0,ImDrawFlags.None,settings.Thickness);if(settings.CornerBoxes){float cx=(b.X-a.X)*.25f,cy=(b.Y-a.Y)*.2f;foreach(var p in new[]{(a,new Num.Vector2(a.X+cx,a.Y)),(a,new Num.Vector2(a.X,a.Y+cy)),(new Num.Vector2(b.X-cx,a.Y),new Num.Vector2(b.X,a.Y)),(new Num.Vector2(b.X,a.Y),new Num.Vector2(b.X,a.Y+cy)),(new Num.Vector2(a.X,b.Y-cy),new Num.Vector2(a.X,b.Y)),(new Num.Vector2(a.X,b.Y),new Num.Vector2(a.X+cx,b.Y)),(new Num.Vector2(b.X-cx,b.Y),b),(new Num.Vector2(b.X,b.Y-cy),b)})draw.AddLine(p.Item1,p.Item2,col,settings.Thickness);}}
  if(settings.Boxes3D){foreach(var edge in new[]{(new Num.Vector2(a.X+10,a.Y+8),new Num.Vector2(a.X+25,a.Y-2)),(new Num.Vector2(b.X+10,b.Y-8),new Num.Vector2(b.X+25,b.Y-18)),(new Num.Vector2(a.X+25,a.Y-2),new Num.Vector2(b.X+25,b.Y-18))})draw.AddLine(edge.Item1,edge.Item2,col,settings.Thickness);}
  if(settings.Skeleton){var head=new Num.Vector2((a.X+b.X)/2,a.Y+14);var torso=new Num.Vector2(head.X,(a.Y+b.Y)/2);draw.AddCircle(head,5,col,12,settings.Thickness);draw.AddLine(head,torso,col,settings.Thickness);draw.AddLine(torso,new(torso.X-18,torso.Y+12),col,settings.Thickness);draw.AddLine(torso,new(torso.X+18,torso.Y+12),col,settings.Thickness);draw.AddLine(torso,new(torso.X-12,b.Y-8),col,settings.Thickness);draw.AddLine(torso,new(torso.X+12,b.Y-8),col,settings.Thickness);}
  if(settings.HeadDots)draw.AddCircleFilled(new((a.X+b.X)/2,a.Y+9),3,col,12);
  if(settings.HealthBars){var lo=new Num.Vector2(a.X-6,a.Y);var hi=new Num.Vector2(a.X-3,b.Y);float health=.64f;draw.AddRectFilled(lo,hi,0xd0101018);draw.AddRectFilled(new(lo.X,hi.Y-(hi.Y-lo.Y)*health),hi,0xff45d483);draw.AddText(new(a.X,b.Y+32),PackColor(settings.EspColor),"64 / 100 health");}
  if(settings.Names)draw.AddText(new(a.X,a.Y-18),PackColor(settings.EspColor),"SamplePlayer");if(settings.ToolNames)draw.AddText(new(a.X,b.Y+16),PackColor(settings.EspColor),"SampleTool");if(settings.Distances)draw.AddText(new(a.X,b.Y+2),PackColor(settings.EspColor),"42 studs");
  if(settings.TargetHighlight)draw.AddCircle(new((a.X+b.X)/2,(a.Y+b.Y)/2),12,0xffffcc44,20,settings.Thickness);
  if(settings.Prediction)draw.AddCircle(new((a.X+b.X)/2,a.Y+2),6,0xff40c0ff,16,settings.Thickness);
 }
 void DrawRadar(ImDrawListPtr draw,IReadOnlyList<Entity> entities,Num.Vector3 local)
 {
  const float size=126,scale=.45f;var min=new Num.Vector2(ClientSize.X-size-14,48);var max=min+new Num.Vector2(size,size);draw.AddRectFilled(min,max,0xb8101018,4);draw.AddRect(min,max,0x909090a0,4);draw.AddLine(new(min.X+size/2,min.Y+8),new(min.X+size/2,max.Y-8),0x40808090);draw.AddLine(new(min.X+8,min.Y+size/2),new(max.X-8,min.Y+size/2),0x40808090);draw.AddText(min+new Num.Vector2(7,5),0xffbcb4c4,"RADAR · XZ");
  foreach(var e in entities)
  {
   if(e.IsNpc&&!settings.EspNpcs)continue;
   if(e.Distance>settings.MaxDistance)continue;float dx=(e.Position.X-local.X)*scale,dz=(e.Position.Z-local.Z)*scale;var p=new Num.Vector2(min.X+size/2+dx,min.Y+size/2+dz);if(p.X<min.X+5||p.X>max.X-5||p.Y<min.Y+5||p.Y>max.Y-5)continue;var c=settings.Gradient?Num.Vector4.Lerp(settings.EspColor,settings.EspGradientColor,Math.Clamp(e.Distance/Math.Max(1,settings.MaxDistance),0,1)):settings.EspColor;draw.AddCircleFilled(p,3,PackColor(c,settings.Alpha),8);
  }
 }
 static void DisabledGroup(string title,string reason)
 {
  ImGui.BeginChild(title,new(0,74),ImGuiChildFlags.Borders);
  ImGui.TextDisabled(title);ImGui.TextWrapped(reason);ImGui.EndChild();
 }
 static bool BeginTab(string label,bool selected)
 {
  var bytes=System.Text.Encoding.UTF8.GetBytes(label+"\0");
  fixed(byte* text=bytes)return ImGuiNative.igBeginTabItem(text,null,selected?ImGuiTabItemFlags.SetSelected:ImGuiTabItemFlags.None)!=0;
 }
 void DrawMenu(Snapshot state)
 {
  ImGui.SetNextWindowPos(new(35,55),ImGuiCond.FirstUseEver);ImGui.SetNextWindowSize(new(760,Math.Clamp(ClientSize.Y-90,360,700)),ImGuiCond.FirstUseEver);
  bool open=true,explorerActive=false;
  ImGui.Begin("Settings",ref open,ImGuiWindowFlags.NoCollapse);
  ImGui.Text("DRUNK");ImGui.SameLine();ImGui.TextDisabled("   ·   "+state.Status);ImGui.Separator();
  if(ImGui.BeginTabBar("Settings tabs"))
  {
   var testTab=renderTest?(frames-1)/2:-1;
   if(BeginTab("Aimbot",testTab==0))
   {
    if(renderTest)renderedTabs.Add(0);
    ImGui.BeginChild("aimbot-content",new(0,Math.Max(160,ImGui.GetContentRegionAvail().Y-48)));
    DrawAimbot();ImGui.EndChild();ImGui.EndTabItem();
   }
   if(BeginTab("ESP",testTab==1))
   {
    if(renderTest)renderedTabs.Add(1);
    ImGui.BeginChild("esp-content",new(0,Math.Max(160,ImGui.GetContentRegionAvail().Y-48)));
    DrawEsp();ImGui.EndChild();ImGui.EndTabItem();
   }
   if(BeginTab("Triggerbot",testTab==2)){if(renderTest)renderedTabs.Add(2);ImGui.BeginChild("trigger-content",new(0,Math.Max(160,ImGui.GetContentRegionAvail().Y-48)));ImGui.Text("Triggerbot");DisabledGroup("Activation / visibility / hit zones","Unavailable: no verified input, hit-zone, visibility, or team fields are present in the active tracker.");ImGui.EndChild();ImGui.EndTabItem();}
   if(BeginTab("World",testTab==3)){if(renderTest)renderedTabs.Add(3);ImGui.BeginChild("world-content",new(0,Math.Max(160,ImGui.GetContentRegionAvail().Y-48)));ImGui.Text("World visuals");ImGui.Checkbox("Center crosshair",ref settings.Crosshair);ImGui.Checkbox("Show aim FOV circle",ref settings.ShowFov);ImGui.TextDisabled("Freecam, lighting, fog, and game camera FOV are unavailable; only validated camera rotation is supported by the bridge.");ImGui.EndChild();ImGui.EndTabItem();}
   if(BeginTab("Players",testTab==4)){if(renderTest)renderedTabs.Add(4);ImGui.BeginChild("players-content",new(0,Math.Max(160,ImGui.GetContentRegionAvail().Y-48)));ImGui.Text("Tracked players and NPCs");ImGui.InputText("Search",ref playerSearch,64);ImGui.Checkbox("Target HUD",ref settings.TargetHud);ImGui.Checkbox("Allow NPC aim targets",ref settings.AimNpcs);var found=(state.Entities??[]).Where(e=>e.Name.Contains(playerSearch,StringComparison.OrdinalIgnoreCase)).ToArray();ImGui.TextDisabled($"{found.Length} shown / {state.Tracked} tracked");foreach(var entity in found.Take(100)){bool isWhitelisted=settings.Whitelist.Contains(entity.Name,StringComparer.OrdinalIgnoreCase);if(ImGui.Checkbox($"Ignore {entity.Name}##whitelist-{entity.Id}",ref isWhitelisted)){if(isWhitelisted)settings.Whitelist.Add(entity.Name);else settings.Whitelist.RemoveAll(n=>string.Equals(n,entity.Name,StringComparison.OrdinalIgnoreCase));}ImGui.SameLine();ImGui.TextDisabled(entity.IsNpc?"NPC":"Player");}ImGui.Separator();ImGui.TextDisabled("NPCs are Workspace humanoid models not linked through Players.Character. Object browser: read-only Game Explorer. Spectating remains unavailable.");ImGui.EndChild();ImGui.EndTabItem();}
   if(BeginTab("Experimental",testTab==5)){if(renderTest)renderedTabs.Add(5);ImGui.BeginChild("exp-content",new(0,Math.Max(160,ImGui.GetContentRegionAvail().Y-48)));ImGui.Text("Experimental movement");DisabledGroup("Speed · Infinite Jump · Fly · Noclip","Unavailable: movement fields have not been verified for this game build.");ImGui.EndChild();ImGui.EndTabItem();}
   if(BeginTab("Console",testTab==6)){if(renderTest)renderedTabs.Add(6);ImGui.BeginChild("console-content",new(0,Math.Max(160,ImGui.GetContentRegionAvail().Y-48)));ImGui.Text("Session metrics");ImGui.TextWrapped($"{state.Status}\nTracked: {state.Tracked}\nSample: {state.SampleMs:F1} ms\nViewport: {state.Width} × {state.Height}");if(aimError.Length>0)ImGui.TextWrapped(aimError);ImGui.EndChild();ImGui.EndTabItem();}
   if(BeginTab("Game Explorer",testTab==7))
   {
    if(renderTest)renderedTabs.Add(7);
    explorerActive=true;
    ImGui.BeginChild("explorer-content",new(0,Math.Max(160,ImGui.GetContentRegionAvail().Y-48)));
    explorer.Draw(state);ImGui.EndChild();ImGui.EndTabItem();
   }
   ImGui.EndTabBar();
  }
  if(!explorerActive)explorer.Pause();
  ImGui.Separator();if(ImGui.Button("Save settings"))Save();ImGui.SameLine();if(ImGui.Button("Close (Insert)"))open=false;
  if(saveError.Length>0)ImGui.TextWrapped("Save failed: "+saveError);
  ImGui.End();if(!open)SetMenu(false);
 }
 protected override void OnRenderFrame(FrameEventArgs e)
 {
  base.OnRenderFrame(e);frames++;var state=tracker.Latest;bool focused=false;
  if(!renderTest)
  {
   if(System.Diagnostics.Stopwatch.GetElapsedTime(lastLookup).TotalMilliseconds>250){game=x11!.FindSober();lastLookup=System.Diagnostics.Stopwatch.GetTimestamp();}
   focused=game!=0&&x11!.Focused(game);
   bool overlayFocused=menu&&x11!.Focused(own);
   bool insert=x11!.InsertDown();
   if(insert&&!insertWasDown&&(focused||overlayFocused))SetMenu(!menu);
   insertWasDown=insert;
   // Opening the menu transfers focus from Sober to this overlay. That is not an Alt-Tab.
   focused=game!=0&&x11.Focused(game);
   overlayFocused=menu&&x11.Focused(own);
   if(!focused&&!overlayFocused&&menu)SetMenu(false);
   bool visible=game!=0&&(focused||overlayFocused);
   if(IsVisible!=visible){IsVisible=visible;if(visible){x11.Interactive(own,menu);x11.Raise(own);}}
   if(game!=0)try{var g=x11.Geometry(game);if(Location.X!=g.X||Location.Y!=g.Y)Location=new(g.X,g.Y);if(ClientSize.X!=g.W||ClientSize.Y!=g.H)ClientSize=new(Math.Max(1,g.W),Math.Max(1,g.H));}catch(IOException){game=0;focused=false;IsVisible=false;}
   string status=game==0?"Waiting for Sober's X11 window (use start-sober.sh)":state.Status;
   if(status!=lastStatus){Console.WriteLine(status);lastStatus=status;}
  }
  var io=ImGui.GetIO();io.DisplaySize=new(ClientSize.X,ClientSize.Y);io.DisplayFramebufferScale=new((float)FramebufferSize.X/Math.Max(1,ClientSize.X),(float)FramebufferSize.Y/Math.Max(1,ClientSize.Y));io.DeltaTime=(float)Math.Clamp(e.Time,.001,.1);
  var pointer=x11!.Pointer(own);io.AddMousePosEvent(pointer.X,pointer.Y);bool left=menu&&pointer.Left;if(left!=mouseWasDown){io.AddMouseButtonEvent(0,left);mouseWasDown=left;}
  ImguiImplOpenGL3.NewFrame();ImGui.NewFrame();var list=ImGui.GetBackgroundDrawList();
  if(renderTest)
  {
   list.AddRect(new(100,100),new(300,350),0xff40ff60,0,ImDrawFlags.None,2);list.AddText(new(100,80),0xffffffff,"Renderer smoke test");
   DrawMenu(state);
  }
  else
  {
   var scale=new Num.Vector2((float)ClientSize.X/Math.Max(1,state.Width),(float)ClientSize.Y/Math.Max(1,state.Height));var center=new Num.Vector2(ClientSize.X/2f,ClientSize.Y/2f);
   if(aimPid!=state.Pid&&state.Pid!=0){selection.Reset();aimPid=state.Pid;}
   bool aiming=settings.AimEnabled&&pointer.Right&&!menu&&focused;
   var target=selection.Update(state.Status=="Live"?state.Entities??[]:[],scale,center,settings,aiming);
   var visibleIds=(state.Entities??[]).Select(e=>e.Id).ToHashSet();
   foreach(var id in trails.Keys.Where(id=>!visibleIds.Contains(id)).ToArray())trails.Remove(id);
   foreach(var entity in state.Entities??[])
   {
    if(entity.IsNpc&&!settings.EspNpcs)continue;
    var r=entity.Rect;var a=new Num.Vector2(r.X,r.Y)*scale;var b=a+new Num.Vector2(r.Z,r.W)*scale;
    float fade=settings.DistanceFade?Math.Clamp((settings.MaxDistance-entity.Distance)/Math.Max(1,settings.MaxDistance-settings.FadeStart),0,1):1;
    if(entity.Distance>settings.MaxDistance)continue;
    var colorVector=settings.Gradient?Num.Vector4.Lerp(settings.EspColor,settings.EspGradientColor,Math.Clamp(entity.Distance/Math.Max(1,settings.MaxDistance),0,1)):settings.EspColor;
    uint color=PackColor(colorVector,settings.Alpha*fade);bool highlighted=settings.TargetHighlight&&target?.Id==entity.Id;
    float thickness=settings.Thickness+(highlighted?1.5f:0);
    if(settings.Trails){if(!trails.TryGetValue(entity.Id,out var trail))trails[entity.Id]=trail=new();var point=entity.AimPoint*scale;if(trail.Count==0||Num.Vector2.DistanceSquared(trail.Last(),point)>4){trail.Enqueue(point);while(trail.Count>20)trail.Dequeue();}var points=trail.ToArray();for(int i=1;i<points.Length;i++)list.AddLine(points[i-1],points[i],PackColor(colorVector,settings.Alpha*fade*i/points.Length),thickness);}
  if(entity.BoxVisible)
    {
     if(settings.Boxes){if(settings.Outline)list.AddRect(a,b,PackColor(new(0,0,0,1),fade),0,ImDrawFlags.None,thickness+2);list.AddRect(a,b,color,0,ImDrawFlags.None,thickness);}
     if(settings.CornerBoxes){float w=b.X-a.X,h=b.Y-a.Y,cx=Math.Min(w*.3f,12),cy=Math.Min(h*.3f,14);foreach(var p in new[]{(a,new Num.Vector2(a.X+cx,a.Y)),(a,new Num.Vector2(a.X,a.Y+cy)),(new Num.Vector2(b.X-cx,a.Y),new Num.Vector2(b.X,a.Y)),(new Num.Vector2(b.X,a.Y),new Num.Vector2(b.X,a.Y+cy)),(new Num.Vector2(a.X,b.Y-cy),new Num.Vector2(a.X,b.Y)),(new Num.Vector2(a.X,b.Y),new Num.Vector2(a.X+cx,b.Y)),(new Num.Vector2(b.X-cx,b.Y),b),(new Num.Vector2(b.X,b.Y-cy),b)})list.AddLine(p.Item1,p.Item2,color,thickness);}
     if(settings.Boxes3D&&entity.Box3DEdges is{} edges)foreach(var edge in edges)list.AddLine(new Num.Vector2(edge.X,edge.Y)*scale,new Num.Vector2(edge.Z,edge.W)*scale,color,thickness);
     if(settings.Skeleton&&entity.Skeleton is{} bones)foreach(var bone in bones)list.AddLine(new Num.Vector2(bone.X,bone.Y)*scale,new Num.Vector2(bone.Z,bone.W)*scale,color,thickness);
     if(settings.Snaplines)list.AddLine(new(ClientSize.X/2f,ClientSize.Y),new((a.X+b.X)/2,b.Y),color,thickness);
     if(settings.HeadDots)list.AddCircleFilled(new((a.X+b.X)/2,a.Y+3),3,color,12);
     if(settings.HealthBars&&entity.HealthFraction is{} health)
     {
      var barMin=new Num.Vector2(a.X-6,a.Y);var barMax=new Num.Vector2(a.X-3,b.Y);list.AddRectFilled(barMin,barMax,0xd0101018);
      float fill=(barMax.Y-barMin.Y)*Math.Clamp(health,0,1);uint healthColor=health>.6f?0xff45d483:health>.3f?0xffe6b34a:0xffed5c67;
      if(fill>0)list.AddRectFilled(new(barMin.X,barMax.Y-fill),barMax,healthColor);
     }
     if(settings.TargetHighlight&&target?.Id==entity.Id)list.AddRect(a-new Num.Vector2(3,3),b+new Num.Vector2(3,3),0xffffcc44,0,ImDrawFlags.None,thickness+1);
     string text=(settings.Names?entity.Name:"")+(entity.IsNpc&&settings.Names?" [NPC]":"")+(settings.Distances?$" {entity.Distance:F0} studs":"");
     if(settings.ToolNames&&!string.IsNullOrWhiteSpace(entity.ToolName))text+=$" · {entity.ToolName}";
     if(text.Length>0)list.AddText(a+new Num.Vector2(0,-16),color,text);
    }
    if(settings.OffscreenArrows)
    {
     var point=entity.AimPoint*scale;
     if(point.X<0||point.Y<0||point.X>ClientSize.X||point.Y>ClientSize.Y){var dir=point-center;if(dir.LengthSquared()>.01f){dir=Num.Vector2.Normalize(dir);float t=Math.Min((ClientSize.X/2-18)/Math.Max(.001f,Math.Abs(dir.X)),(ClientSize.Y/2-18)/Math.Max(.001f,Math.Abs(dir.Y)));var tip=center+dir*t;var side=new Num.Vector2(-dir.Y,dir.X)*7;list.AddTriangleFilled(tip,tip-dir*14+side,tip-dir*14-side,color);}}
    }
    if(settings.Prediction&&entity.PredictedPoint is{} predicted)list.AddCircle(predicted*scale,5,0xff40c0ff,16,thickness);
   }
   if(settings.Crosshair){list.AddLine(center-new Num.Vector2(8,0),center+new Num.Vector2(8,0),0xb0d0c0d8,1);list.AddLine(center-new Num.Vector2(0,8),center+new Num.Vector2(0,8),0xb0d0c0d8,1);}
   if(settings.ShowFov){float radius=settings.DynamicFov&&target is not null?settings.FovRadius*Math.Clamp(1-target.Distance/Math.Max(1,settings.MaxDistance),.2f,1):settings.FovRadius;list.AddCircle(center,radius,settings.AimEnabled?0xb060ff40u:0x80808080u,96,1.5f);}
   if(settings.Radar&&state.LocalPosition is{} local)DrawRadar(list,state.Entities??[],local);
   if(target is not null)
   {
    var point=AimMath.Point(target,scale,settings);
    if((settings.AimEnabled&&settings.TargetMarker)||settings.TargetHighlight)list.AddCircle(point,settings.TargetHighlight?8:5,0xff00c0ff,20,2);
    if(aiming)
    {
     try
     {
      if(settings.AimMethod==AimMethod.Camera){remainder=default;if(Num.Vector2.Distance(point,center)>settings.Deadzone)tracker.AimCamera(state,target,settings,e.Time);}
      else
      {
       var error=point-center;var move=AimMath.Motion(error,settings,e.Time,target.Distance)+(error.Length()>settings.Deadzone?remainder:default);int dx=(int)MathF.Truncate(move.X),dy=(int)MathF.Truncate(move.Y);remainder=move-new Num.Vector2(dx,dy);
       x11.MovePointer(dx,dy);
      }
      aimError="";
     }
     catch(TargetUnavailableException error){remainder=default;aimError="Waiting: "+error.Message;}
     catch(Exception error)when(error is IOException or System.Net.Sockets.SocketException or DllNotFoundException)
     {
      settings.AimEnabled=false;selection.Reset();remainder=default;aimError="Stopped: "+error.Message;Console.Error.WriteLine("Aim: "+aimError);
     }
    }
    else remainder=default;
   }
   else remainder=default;
   if(settings.TargetHud&&target is not null)list.AddText(center+new Num.Vector2(-100,24),0xffeeeeee,$"{target.Name} · {target.Distance:F0} studs"+(target.HealthFraction is{} health?$" · {health*100:F0}/100 HP":""));
   if(settings.Status){list.AddRectFilled(new(8,8),new(700,33),0xb0000000);list.AddText(new(16,15),0xffb0ffc0,$"{state.Status} · {state.Entities?.Count(e=>e.BoxVisible)??0}/{state.Tracked} visible · {state.SampleMs:F1} ms · Insert: settings");}
   if(aimError.Length>0)list.AddText(new(16,40),0xff8080ff,"Aim: "+aimError);
   if(menu)DrawMenu(state);
  }
  ImGui.Render();GL.Viewport(0,0,FramebufferSize.X,FramebufferSize.Y);GL.ClearColor(0,0,0,0);GL.Clear(ClearBufferMask.ColorBufferBit);ImguiImplOpenGL3.RenderDrawData(ImGui.GetDrawData());
  if(renderTest&&frames%2==0){var pixels=new byte[FramebufferSize.X*FramebufferSize.Y*4];GL.ReadPixels(0,0,FramebufferSize.X,FramebufferSize.Y,PixelFormat.Rgba,PixelType.UnsignedByte,pixels);int painted=Enumerable.Range(0,pixels.Length/4).Count(i=>pixels[4*i+3]>0);if(painted<40000||GL.GetError()!=OpenTK.Graphics.OpenGL4.ErrorCode.NoError)throw new IOException("ImGui render validation failed");Console.WriteLine($"Renderer/tab {(frames/2)} smoke test passed: {painted} painted pixels, transparent framebuffer, no GL errors.");if(frames==16){if(renderedTabs.Count!=8)throw new IOException($"Only {renderedTabs.Count}/8 menu tabs rendered");Close();}}
  SwapBuffers();
 }
 protected override void OnUnload(){explorer.Dispose();if(!renderTest)Save();ImguiImplOpenGL3.Shutdown();ImGui.DestroyContext();x11?.Dispose();base.OnUnload();}
}
