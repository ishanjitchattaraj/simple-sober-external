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
 X11? x11;nuint own,game;int frames;long lastLookup;bool menu,insertWasDown,mouseWasDown;string lastStatus="",saveError="",aimError="";
 readonly Settings settings=Settings.Load(Path.Combine(home,"settings.json"));
 readonly AimSelection selection=new();int aimPid;
 readonly HashSet<int> renderedTabs=[];
 readonly GameExplorer explorer=renderTest?GameExplorer.CreatePreview():new GameExplorer(tracker.ReadExplorer);
 Num.Vector2 remainder;
 protected override void OnLoad()
 {
  base.OnLoad();x11=new();own=(nuint)GLFW.GetX11Window(WindowPtr);if(own==0)throw new IOException("Overlay X11 window not found");x11.Unmanaged(own);
  GLFW.SetWindowAttrib(WindowPtr,WindowAttribute.FocusOnShow,false);GLFW.SetWindowAttrib(WindowPtr,WindowAttribute.Floating,true);MousePassthrough=true;x11.ClickThrough(own);
  if(!HasTransparentFramebuffer)throw new IOException("A transparent OpenGL framebuffer could not be created");
  ImGui.CreateContext();var io=ImGui.GetIO();io.NativePtr->IniFilename=null;io.DisplaySize=new(800,600);io.DeltaTime=1/60f;ImguiImplOpenGL3.Init(false);VSync=VSyncMode.Off;
  Console.WriteLine("Overlay ready. Insert: menu. Aim: hold right mouse (disabled by default).");
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
  ImGui.SliderFloat("Maximum distance",ref settings.MaxDistance,10,10000,"%.0f studs");
  int priority=(int)settings.TargetPriority;
  if(ImGui.Combo("Target priority",ref priority,"Closest to mouse\0Closest to player\0")){settings.TargetPriority=(TargetPriority)priority;selection.Reset();}
  if(ImGui.Checkbox("Aim lock (keep target while held)",ref settings.AimLock))selection.Reset();
  ImGui.Checkbox("Aim at head (otherwise torso)",ref settings.AimAtHead);
  ImGui.TextWrapped("Higher smoothness moves more slowly. Closest to mouse uses the centered crosshair, not the captured desktop cursor. Both priorities use the FOV circle. Closest to player uses your character position. Lock pauses if its target becomes unavailable; release right mouse to choose another. No team or wall/occlusion filtering.");
  if(aimError.Length>0)ImGui.TextWrapped("Aim: "+aimError);
 }
 void DrawEsp()
 {
  ImGui.Checkbox("Player boxes",ref settings.Boxes);
  ImGui.Checkbox("Player names",ref settings.Names);
  ImGui.Checkbox("Distances (studs)",ref settings.Distances);
  ImGui.Checkbox("Status bar",ref settings.Status);
  ImGui.Separator();ImGui.TextWrapped("Boxes follow other players. Your own character is excluded. FOV and target markers are configured in the Aimbot tab.");
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
  ImGui.TextWrapped("test.");ImGui.TextWrapped(state.Status);ImGui.Separator();
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
   if(BeginTab("Game Explorer",testTab==2))
   {
    if(renderTest)renderedTabs.Add(2);
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
   foreach(var entity in state.Entities??[])
   {
    if(!entity.BoxVisible)continue;
    var r=entity.Rect;var a=new Num.Vector2(r.X,r.Y)*scale;var b=a+new Num.Vector2(r.Z,r.W)*scale;
    if(settings.Boxes){list.AddRect(a,b,0xc0000000,0,ImDrawFlags.None,4);list.AddRect(a,b,0xff60ff40,0,ImDrawFlags.None,1.5f);}
    string text=(settings.Names?entity.Name:"")+(settings.Distances?$" {entity.Distance:F0} studs":"");
    if(text.Length>0){list.AddText(a+new Num.Vector2(1,-15),0xff000000,text);list.AddText(a+new Num.Vector2(0,-16),0xffd0ffe0,text);}
   }
   if(settings.ShowFov)list.AddCircle(center,settings.FovRadius,settings.AimEnabled?0xb060ff40u:0x80808080u,96,1.5f);
   if(aimPid!=state.Pid&&state.Pid!=0){selection.Reset();aimPid=state.Pid;}
   bool aiming=settings.AimEnabled&&pointer.Right&&!menu&&focused;
   var target=selection.Update(state.Status=="Live"?state.Entities??[]:[],scale,center,settings,aiming);
   if(target is not null)
   {
    var point=AimMath.Point(target,scale,settings);
    if(settings.AimEnabled&&settings.TargetMarker)list.AddCircle(point,5,0xff00c0ff,20,2);
    if(aiming)
    {
     try
     {
      if(settings.AimMethod==AimMethod.Camera){remainder=default;tracker.AimCamera(state,target,settings,e.Time);}
      else
      {
       var move=AimMath.Motion(point-center,settings.Sensitivity,settings.Smoothness,e.Time)+remainder;int dx=(int)MathF.Truncate(move.X),dy=(int)MathF.Truncate(move.Y);remainder=move-new Num.Vector2(dx,dy);
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
   if(settings.Status){list.AddRectFilled(new(8,8),new(700,33),0xb0000000);list.AddText(new(16,15),0xffb0ffc0,$"{state.Status} · {state.Entities?.Count(e=>e.BoxVisible)??0}/{state.Tracked} visible · {state.SampleMs:F1} ms · Insert: settings");}
   if(aimError.Length>0)list.AddText(new(16,40),0xff8080ff,"Aim: "+aimError);
   if(menu)DrawMenu(state);
  }
  ImGui.Render();GL.Viewport(0,0,FramebufferSize.X,FramebufferSize.Y);GL.ClearColor(0,0,0,0);GL.Clear(ClearBufferMask.ColorBufferBit);ImguiImplOpenGL3.RenderDrawData(ImGui.GetDrawData());
  if(renderTest&&frames%2==0){var pixels=new byte[FramebufferSize.X*FramebufferSize.Y*4];GL.ReadPixels(0,0,FramebufferSize.X,FramebufferSize.Y,PixelFormat.Rgba,PixelType.UnsignedByte,pixels);int painted=Enumerable.Range(0,pixels.Length/4).Count(i=>pixels[4*i+3]>0);if(painted<40000||GL.GetError()!=OpenTK.Graphics.OpenGL4.ErrorCode.NoError)throw new IOException("ImGui render validation failed");Console.WriteLine($"Renderer/tab {(frames/2)} smoke test passed: {painted} painted pixels, transparent framebuffer, no GL errors.");if(frames==6){if(renderedTabs.Count!=3)throw new IOException("Not all three menu tabs rendered");Close();}}
  SwapBuffers();
 }
 protected override void OnUnload(){explorer.Dispose();if(!renderTest)Save();ImguiImplOpenGL3.Shutdown();ImGui.DestroyContext();x11?.Dispose();base.OnUnload();}
}
