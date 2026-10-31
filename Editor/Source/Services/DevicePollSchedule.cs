namespace Gp200.Replica;
// Keep UI animation ticks independent from native USB polling. A recent
// action/change briefly accelerates reads; steady idle performs at most 2/s.
internal sealed class DevicePollSchedule
{
    DateTime next,fastUntil;
    internal bool Due(DateTime now)=>now>=next;
    internal void Activity(DateTime now){fastUntil=now.AddSeconds(2);next=DateTime.MinValue;}
    internal void Complete(DateTime now,bool changed)
    {
        if(changed)fastUntil=now.AddSeconds(2);
        next=now.AddMilliseconds(now<fastUntil?150:500);
    }
}
