using System.Buffers.Binary;
using System.Numerics;
using System.Text;
namespace SoberEsp;

public static class CharacterTrackingTests
{
 public static void Run()
 {
  var pages=new Dictionary<ulong,byte[]>();ulong next=0x100000;
  ulong Alloc(){ulong a=next;next+=4096;pages[a]=new byte[4096];return a;}
  void Bytes(ulong a,byte[] b)=>Buffer.BlockCopy(b,0,pages[a&~4095UL],(int)(a&4095),b.Length);
  void U64(ulong a,ulong v){var b=new byte[8];BinaryPrimitives.WriteUInt64LittleEndian(b,v);Bytes(a,b);}
  void Float(ulong address,float value)=>Bytes(address,BitConverter.GetBytes(value));
  void Type(ulong instance,string raw){ulong layout=Alloc(),vtable=layout+8,name=Alloc();U64(instance,vtable);U64(vtable-8,layout);U64(layout+8,name);Bytes(name,[..Encoding.ASCII.GetBytes(raw),0]);}
  void Position(ulong primitive,Vector3 v){var bytes=new byte[12];Buffer.BlockCopy(new[]{v.X,v.Y,v.Z},0,bytes,0,12);Bytes(primitive+Offsets.Part.Position,bytes);}
  ulong Object(string name,ulong parent)
  {
   ulong a=Alloc(),text=Alloc();U64(a+Offsets.Instance.Parent,parent);U64(a+Offsets.Instance.Name,text);
   Bytes(text+Offsets.Instance.NameText,[(byte)(name.Length*2),..Encoding.UTF8.GetBytes(name)]);return a;
  }
  void Children(ulong a,params ulong[] children)
  {
   ulong v=Alloc(),data=Alloc();U64(a+Offsets.Instance.Children,v);U64(v,data);U64(v+8,data+(ulong)children.Length*16);
   for(int i=0;i<children.Length;i++)U64(data+(ulong)i*16,children[i]);
  }
  SceneMemory Memory()=>new((a,n)=>pages.TryGetValue(a,out var page)?page.ToArray():throw new IOException("Fixture page unavailable"));
  void Check(bool value,string error){if(!value)throw new Exception("Character tracking: "+error);}
  ulong workspace=Object("Workspace",0),folder=Object("Characters",workspace),model=Object("Remote",folder),player=Object("Player",0);
  ulong root=Object("HumanoidRootPart",model),head=Object("Head",model),rootPrimitive=Alloc(),headPrimitive=Alloc();
  U64(player+Offsets.Player.Character,model);U64(root+Offsets.Part.Primitive,rootPrimitive);U64(head+Offsets.Part.Primitive,headPrimitive);Children(model,root,head);
  var target=new Target(model,"Remote",root,head,player);
  ulong tool=Object("TestRifle",model),humanoid=Object("Humanoid",model);Type(tool,"N3RBX4ToolE");Type(humanoid,"N3RBX8HumanoidE");Float(humanoid+Offsets.Humanoid.Health,75);Float(humanoid+Offsets.Humanoid.MaxHealth,100);Float(humanoid+Offsets.Humanoid.WalkSpeed,16);Children(model,root,head,tool,humanoid);
  Check(CharacterReader.HasHumanoid(Memory(),model),"verified Humanoid RTTI was not recognized");
  float[] frame=[1,0,0,0,1,0,0,0,1,0,0,0];
  var settings=new Settings{MaxDistance=10000};
  foreach(var position in new[]{new Vector3(0,0,-20),new Vector3(6,2,-20),new Vector3(-6,-2,-20),new Vector3(1000,0,-2000),new Vector3(-1000,0,-2000)})
  {
   Position(rootPrimitive,position);Position(headPrimitive,position+new Vector3(0,2,0));
   var sample=CharacterReader.Read(Memory(),target,workspace);
   Check(sample!=null,"moving/nested character disappeared");
   var entity=EntityProjection.Create(sample!.Target,sample.Position,sample.Head,frame,MathF.PI/2,1000,1000,null);
   Check(entity is {BoxVisible:true},"fast/distant movement dropped box");
  }
  var equipped=CharacterReader.Read(Memory(),target,workspace)!;
  Check(equipped.ToolName=="TestRifle","direct equipped Tool child was not sampled");
  Check(equipped.HealthFraction==.75f&&equipped.WalkSpeed==16,"verified Humanoid health or WalkSpeed getter fields were not sampled");
  Float(humanoid+Offsets.Humanoid.Health,0);Check(CharacterReader.Read(Memory(),target,workspace)?.HealthFraction==0,"zero Health was not exposed as dead");
  Float(humanoid+Offsets.Humanoid.Health,125);Check(CharacterReader.Read(Memory(),target,workspace)?.HealthFraction==1,"health fraction did not clamp to the fixed 100-point display scale");
  Float(humanoid+Offsets.Humanoid.Health,float.NaN);Check(CharacterReader.Read(Memory(),target,workspace)?.HealthFraction==null,"non-finite Health was trusted");Float(humanoid+Offsets.Humanoid.Health,75);
  var npcTarget=target with{Player=0,Name="TrainingNpc"};var npcEntity=EntityProjection.Create(npcTarget,equipped.Position,equipped.Head,frame,MathF.PI/2,1000,1000,null,equipped.Parts,Vector3.Zero,equipped.ToolName,equipped.HealthFraction,equipped.WalkSpeed)!;
  Check(npcEntity.IsNpc&&npcEntity.ToolName=="TestRifle","NPC class and Tool name were not propagated to overlay data");
  Position(rootPrimitive,new(0,0,-.1f));Position(headPrimitive,new(0,2,.5f));
  var close=CharacterReader.Read(Memory(),target,workspace)!;
  var closeEntity=EntityProjection.Create(close.Target,close.Position,close.Head,frame,MathF.PI/2,1000,1000,null)!;
  Check(closeEntity.BoxVisible&&!closeEntity.HeadAvailable&&AimMath.Eligible(closeEntity,Vector2.One,new(500,500),settings),"near-plane head dropped valid body/aim");
  Check(closeEntity.Rect.X>=0&&closeEntity.Rect.Y>=0&&closeEntity.Rect.X+closeEntity.Rect.Z<=1000&&closeEntity.Rect.Y+closeEntity.Rect.W<=1000,"close box was not clipped");
  Position(rootPrimitive,new(0,0,-20));Position(headPrimitive,new(3,-3.8f,-20));
  var ragdoll=CharacterReader.Read(Memory(),target,workspace)!;
  Check(EntityProjection.Create(ragdoll.Target,ragdoll.Position,ragdoll.Head,frame,MathF.PI/2,1000,1000,null) is {BoxVisible:true},"coincident head/feet Y removed ragdoll");
  U64(head+Offsets.Part.Primitive,0xdead0000);
  var partial=CharacterReader.Read(Memory(),target,workspace);
  Check(partial is {Head:null},"unreadable head removed body");
  var partialEntity=EntityProjection.Create(partial!.Target,partial.Position,partial.Head,frame,MathF.PI/2,1000,1000,null)!;
  Check(!partialEntity.HeadAvailable&&AimMath.Eligible(partialEntity,Vector2.One,new(500,500),settings),"head fallback not aimable");
  U64(head+Offsets.Part.Primitive,headPrimitive);U64(head+Offsets.Instance.Parent,0);
  Check(CharacterReader.Read(Memory(),target,workspace) is {Head:null},"reparented stale head was trusted");
  ulong replacement=Object("HumanoidRootPart",model),replacementPrimitive=Alloc();U64(replacement+Offsets.Part.Primitive,replacementPrimitive);Position(replacementPrimitive,new(2,0,-20));
  U64(root+Offsets.Instance.Parent,0);Children(model,replacement);
  var replaced=CharacterReader.Read(Memory(),target,workspace);
  Check(replaced?.Target.Root==replacement&&replaced.Position.X==2,"replaced root was not resolved immediately");
  ulong newPrimitive=Alloc();Position(newPrimitive,new(-3,0,-20));U64(replacement+Offsets.Part.Primitive,newPrimitive);
  Check(CharacterReader.Read(Memory(),replaced!.Target,workspace)?.Position.X==-3,"swapped physics primitive retained old position");
  ulong torso=Object("UpperTorso",model),torsoPrimitive=Alloc();U64(torso+Offsets.Part.Primitive,torsoPrimitive);Position(torsoPrimitive,new(0,0,-20));
  U64(replacement+Offsets.Part.Primitive,0);Children(model,replacement,torso);
  Check(CharacterReader.Read(Memory(),replaced.Target,workspace)?.Target.Root==torso,"unreadable root did not fall back to a live torso");
  U64(replacement+Offsets.Part.Primitive,replacementPrimitive);Position(replacementPrimitive,new(0,-1,-20));
  var articulated=CharacterReader.Read(Memory(),replaced.Target,workspace)!;
  var articulatedEntity=EntityProjection.Create(articulated.Target,articulated.Position,articulated.Head,frame,MathF.PI/2,1000,1000,null,articulated.Parts,new(1,2,0))!;
  Check(articulatedEntity.Box3DEdges is {Count:12},"3D bounds did not produce all twelve projected edges");
  Check(articulatedEntity.Skeleton is {Count:>0},"sampled rig parts did not produce skeleton bones");
  Check(articulatedEntity.PredictedPoint is{} predicted&&Vector2.Distance(predicted,articulatedEntity.AimPoint)>.01f,"sampled velocity did not move prediction point");
  U64(player+Offsets.Player.Character,0);Check(CharacterReader.Read(Memory(),replaced.Target,workspace)==null,"removed character retained ghost target");
  U64(player+Offsets.Player.Character,model);U64(model+Offsets.Instance.Parent,0);Check(CharacterReader.Read(Memory(),replaced.Target,workspace)==null,"detached model retained ghost target");
  U64(model+Offsets.Instance.Parent,folder);U64(folder+Offsets.Instance.Parent,model);Check(!CharacterReader.IsInWorkspace(Memory(),model,workspace),"cyclic ancestry was accepted");
 }
}
