namespace SoberEsp;

public static class Offsets
{
 public const string BuildSha256="852151db150ec26604a4e79a0c15d30c9724af302e635c182d35d82a0f67a4df";
 public const ulong WorkspaceVtable=0x6fed9e8, MinimumLibraryMappingSize=0x6000000;
 public static readonly IReadOnlyDictionary<ulong,byte[]> Fingerprints=new Dictionary<ulong,byte[]>
 {
  [0x498cd14]=Convert.FromHexString("554889e541574156534883ec384989f6"),
  [0x6635602]=Convert.FromHexString("554889e55350488b9f68010000")
 };
 public static class Instance
 {
  public const ulong Self=8, Parent=0x68, Name=0x70, Children=0x78;
  public const ulong NameText=8, ChildrenBegin=0, ChildrenEnd=8;
  public const int ChildStride=16;
 }
 public static class Rtti {public const ulong VtableTypeInfoBack=8, TypeName=8;}
 public static class Text {public const int Size=24, ShortData=1, ShortCapacity=22, Length=8, Data=16;}
 public static class Players {public const ulong LocalPlayer=0x110;}
 public static class Player {public const ulong Character=0x240;}
 public static class Part {public const ulong Primitive=0x168, Position=0xd4;}
 // Property getters run through the generic callback at ELF VA 0x2f4f538,
 // which adjusts the Humanoid Instance by +0xa0 before invoking the getter.
 public static class Humanoid {public const ulong Health=0x170, MaxHealth=0x188, WalkSpeed=0x1b0;}
 public static class Camera
 {
  public const ulong Frame=0xc0, Viewport=0x274;
  public const int RotationBytes=36;
  public const int SampleSize=0x200, FrameBytes=48, FrameFloats=12;
  public const int FovInSample=0x68, WidthInSample=0x1b4, HeightInSample=0x1b6;
 }
}
