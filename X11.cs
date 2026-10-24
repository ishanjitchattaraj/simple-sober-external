using System.Runtime.InteropServices;
using System.Text;
namespace SoberEsp;
public sealed class X11 : IDisposable
{
 const string Lib="libX11.so.6";
 [DllImport(Lib)]static extern nint XOpenDisplay(nint name);
 [DllImport(Lib)]static extern int XCloseDisplay(nint d);
 [DllImport(Lib)]static extern nuint XDefaultRootWindow(nint d);
 [DllImport(Lib)]static extern nuint XInternAtom(nint d,string name,bool onlyIfExists);
 [DllImport(Lib)]static extern int XGetWindowProperty(nint d,nuint w,nuint property,nint offset,nint length,bool delete,nuint requested,out nuint type,out int format,out nuint count,out nuint remaining,out nint data);
 [DllImport(Lib)]static extern int XFree(nint p);
 [DllImport(Lib)]static extern int XQueryTree(nint d,nuint w,out nuint root,out nuint parent,out nint children,out uint count);
 [DllImport(Lib)]static extern int XFetchName(nint d,nuint w,out nint name);
 [DllImport(Lib)]static extern int XGetGeometry(nint d,nuint w,out nuint root,out int x,out int y,out uint width,out uint height,out uint border,out uint depth);
 [DllImport(Lib)]static extern int XTranslateCoordinates(nint d,nuint src,nuint dest,int x,int y,out int dx,out int dy,out nuint child);
 [DllImport(Lib)]static extern int XFlush(nint d);
 [DllImport(Lib)]static extern int XChangeWindowAttributes(nint d,nuint w,nuint mask,ref Attributes attributes);
 [StructLayout(LayoutKind.Sequential)]struct Attributes{public nuint BackgroundPixmap,BackgroundPixel,BorderPixmap,BorderPixel;public int BitGravity,WinGravity,BackingStore;public nuint BackingPlanes,BackingPixel;public int SaveUnder;public nint EventMask,DoNotPropagateMask;public int OverrideRedirect;public nuint Colormap,Cursor;}
 public void Unmanaged(nuint window){var attributes=new Attributes{OverrideRedirect=1};XChangeWindowAttributes(display,window,1u<<9,ref attributes);XFlush(display);}

 [DllImport(Lib)]static extern int XGetInputFocus(nint d,out nuint focus,out int revert);
 [DllImport(Lib)]static extern nint XGetWMHints(nint d,nuint w);
 [DllImport(Lib)]static extern int XSetWMHints(nint d,nuint w,ref Hints hints);
 [DllImport("libXfixes.so.3")]static extern nuint XFixesCreateRegion(nint d,nint rectangles,int count);
 [DllImport("libXfixes.so.3")]static extern void XFixesSetWindowShapeRegion(nint d,nuint w,int kind,int x,int y,nuint region);
 [DllImport("libXfixes.so.3")]static extern void XFixesDestroyRegion(nint d,nuint r);
 [StructLayout(LayoutKind.Sequential)]struct Hints{public nint Flags;public int Input,InitialState;public nuint IconPixmap,IconWindow;public int IconX,IconY;public nuint IconMask,WindowGroup;}
 [UnmanagedFunctionPointer(CallingConvention.Cdecl)]delegate int ErrorHandler(nint d,nint error);
 [DllImport(Lib)]static extern nint XSetErrorHandler(ErrorHandler handler);
 static readonly ErrorHandler IgnoreClosedWindow=(d,error)=>0;
 readonly nint display;readonly nuint root;
 public X11(){XSetErrorHandler(IgnoreClosedWindow);display=XOpenDisplay(0);if(display==0)throw new IOException("Xwayland display unavailable");root=XDefaultRootWindow(display);}
 byte[] Property(nuint w,string property,out int format)
 {
  XGetWindowProperty(display,w,XInternAtom(display,property,false),0,1024,false,0,out _,out format,out var count,out _,out var data);if(data==0)return [];try{int bytes=checked((int)count*(format==32?IntPtr.Size:format/8));var result=new byte[bytes];Marshal.Copy(data,result,0,bytes);return result;}finally{XFree(data);}
 }
 public List<nuint> Children(nuint w){if(XQueryTree(display,w,out _,out _,out var data,out var count)==0)return [];try{var children=new List<nuint>();for(int i=0;i<count;i++)children.Add((nuint)Marshal.ReadIntPtr(data,i*IntPtr.Size));return children;}finally{if(data!=0)XFree(data);}}
 public nuint FindTitle(string title){foreach(var w in Children(root)){XFetchName(display,w,out var p);if(p!=0){string name=Marshal.PtrToStringUTF8(p)??"";XFree(p);if(name==title)return w;}}return 0;}
 public nuint FindSober()
 {
  nuint best=0;long area=0;
  var list=Property(root,"_NET_CLIENT_LIST",out _);
  for(int i=0;i+IntPtr.Size<=list.Length;i+=IntPtr.Size)
  {
   nuint w=(nuint)BitConverter.ToUInt64(list,i);
   var cls=Encoding.UTF8.GetString(Property(w,"WM_CLASS",out _)).ToLowerInvariant();
   bool matches=cls.Split('\0').Any(s=>s=="sober"||s=="org.vinegarhq.sober");
   if(!matches)
   {
    var pid=Property(w,"_NET_WM_PID",out _);
    if(pid.Length>=8)try{matches=File.ReadAllLines($"/proc/{BitConverter.ToUInt64(pid)}/root/.flatpak-info").Contains("name=org.vinegarhq.Sober");}catch(IOException){}catch(UnauthorizedAccessException){}
   }
   if(matches)try{var g=Geometry(w);long candidate=(long)g.W*g.H;if(candidate>area){best=w;area=candidate;}}catch(IOException){}
  }
  return best;
 }
 // Read actual input focus: _NET_ACTIVE_WINDOW can still name Sober while the
 // unmanaged settings window has keyboard focus.
 public bool Focused(nuint window)
 {
  if(window==0)return false;
  XGetInputFocus(display,out var focus,out _);
  for(int i=0;i<8&&focus>1&&focus!=root;i++)
  {
   if(focus==window)return true;
   if(XQueryTree(display,focus,out _,out var parent,out var children,out _)!=0)
   {if(children!=0)XFree(children);focus=parent;}else break;
  }
  return false;
 }
 [DllImport(Lib)]static extern int XRaiseWindow(nint d,nuint window);
 [DllImport(Lib)]static extern int XSetInputFocus(nint d,nuint window,int revert,nuint time);
 public void Raise(nuint window){XRaiseWindow(display,window);XFlush(display);}
 public void Focus(nuint window){XRaiseWindow(display,window);XSetInputFocus(display,window,2,0);XFlush(display);}
 public (int X,int Y,int W,int H) Geometry(nuint window){if(XGetGeometry(display,window,out _,out _,out _,out var w,out var h,out _,out _)==0)throw new IOException("Game window closed");XTranslateCoordinates(display,window,root,0,0,out var x,out var y,out _);return(x,y,(int)w,(int)h);}
 [DllImport(Lib)]static extern int XQueryKeymap(nint d,byte[] keys);
 [DllImport(Lib)]static extern uint XKeysymToKeycode(nint d,nuint keysym);
 [DllImport(Lib)]static extern int XQueryPointer(nint d,nuint w,out nuint root,out nuint child,out int rx,out int ry,out int x,out int y,out uint mask);
 [DllImport("libXtst.so.6")]static extern int XTestFakeRelativeMotionEvent(nint d,int x,int y,nuint delay);
 readonly byte[] keys=new byte[32];
 public bool InsertDown(){XQueryKeymap(display,keys);int key=(int)XKeysymToKeycode(display,0xff63);return key!=0&&(keys[key/8]&(1<<(key%8)))!=0;}
 public (int X,int Y,bool Left,bool Right) Pointer(nuint window)
 {
  if(XQueryPointer(display,window,out _,out _,out _,out _,out int x,out int y,out uint mask)==0)return(0,0,false,false);
  return(x,y,(mask&256)!=0,(mask&1024)!=0);
 }
 public void MovePointer(int x,int y){XTestFakeRelativeMotionEvent(display,x,y,0);XFlush(display);}
 public void Interactive(nuint window,bool interactive)
 {
  if(!interactive){ClickThrough(window);return;}
  XFixesSetWindowShapeRegion(display,window,2,0,0,0);
  var p=XGetWMHints(display,window);var hints=p==0?new Hints():Marshal.PtrToStructure<Hints>(p);if(p!=0)XFree(p);
  hints.Flags|=1;hints.Input=1;XSetWMHints(display,window,ref hints);XFlush(display);
 }
 public void ClickThrough(nuint window){if(window==0)return;var region=XFixesCreateRegion(display,0,0);XFixesSetWindowShapeRegion(display,window,2,0,0,region);XFixesDestroyRegion(display,region);var p=XGetWMHints(display,window);var hints=p==0?new Hints():Marshal.PtrToStructure<Hints>(p);if(p!=0)XFree(p);hints.Flags|=1;hints.Input=0;XSetWMHints(display,window,ref hints);XFlush(display);}
 public void Dispose()=>XCloseDisplay(display);
}
