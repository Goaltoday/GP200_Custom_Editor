namespace Gp200.Replica;

[Flags]
internal enum Mx42Capability : uint
{
    CoherentActive = 1, UniversalSlotSix = 2, SlotElevenParameter12 = 4, Chain = 8,
    SceneSnapshot = 16, SceneRestore = 32, SceneBase = 64, NamNames = 128,
    PresetAdmission = 256, NamUpload = 512, NamRecovery = 1024, CabMap = 2048
}
internal static class SceneLayout
{
    internal const uint Magic=0x3153424d;
    internal const int Version=1,Size=4144,Revision=36,Header=64,Identity=66,IdentityBytes=94,Ownership=138;
    internal const int MagicAt=0,VersionAt=4,SizeAt=8,Generation=12,Slot=16,Mode=20,Presence=24,Active=28,HasBase=32;
    internal const int Reserved=40,ReservedBytes=24,Order=77,Keys=94;
    internal const int FirstScene=160,SceneBytes=664,BaseWords=2816,LiveWords=3480;
    internal const int WordsPerBlock=15,BlockCount=11,BlockWordBytes=60,BypassInState=660,LiveBypass=4140,SceneCount=4;
    internal const int AllScenesBytes=SceneCount*SceneBytes,Sc10Capacity=380,ChainMask=0x6000;
    internal static int SceneAt(int scene)=>scene is >=0 and <SceneCount?FirstScene+scene*SceneBytes:throw new ArgumentOutOfRangeException(nameof(scene));
    internal static readonly int[] StoredStates=[SceneAt(0),SceneAt(1),SceneAt(2),SceneAt(3),BaseWords];
    internal static readonly int[] AllStates=[..StoredStates,LiveWords];
}
internal static class MxeLayout
{
    internal const ushort RecordType=0x1009,Marker=0x584d,Version=1;
    internal const uint Magic=0x3145584d;
    internal const int Header=32,Service=2,Operation=3,MagicAt=4,Nonce=8,VersionAt=12,Status=14,Revision=16,Arg0=20,Arg1=24,Length=28;
}
internal static class MxeCapabilitiesLayout
{
    internal const int Size=32,Version=0,Flags=4,NamSlots=8,Footswitches=12,SceneBytes=16,Chunk=20,Families=24,LayoutId=28;
    internal const uint ExpectedLayoutId=0x3149584d;
}
internal static class NamRecordLayout
{
    internal const uint Magic=0x314d414e,Architecture=0x41324c31;
    internal const int Size=7552,Header=64,Slot=4,WeightsCrc=8,ArchitectureAt=12,Name=16,NameBytes=32,MaxName=16,Reserved=48;
    internal const int WeightCount=1871,WeightBytes=7484,Padding=7548,HeadScale=7544;
}
