namespace Gp200.Replica;

internal static class CtrlAssignmentView
{
    internal static int OwnerAt(PresetDocument document,int visualPosition)
    {
        if((uint)visualPosition>=document.Chain.Length)throw new ArgumentOutOfRangeException(nameof(visualPosition));
        return document.Chain[visualPosition];
    }

    internal static string FamilyLabel(Catalog catalog,PresetDocument document,int owner)
    {
        var effect=document.GetEffect(owner);
        var algorithm=catalog.Find(owner,effect.Code);
        if(catalog.IsMx42&&algorithm?.IsNone==true)return "NONE";
        return catalog.IsMx42?catalog.FamilyLabel(owner,effect.Code):PresetDocument.Modules[owner];
    }

    internal static string CardLabel(Catalog catalog,PresetDocument document,int visualPosition,int owner)
    {
        var state=document.GetEffect(owner).Enabled?"ON":"OFF";
        return $"{visualPosition+1:00} · {FamilyLabel(catalog,document,owner)}\n{state}";
    }

    internal static bool IsAssigned(uint mask,int owner)=>(mask&(1u<<owner))!=0;

    internal static void SetAssigned(PresetDocument document,byte ctrlSlot,int owner,bool assigned)
    {
        uint mask=document.Controls.Single(c=>c.Slot==ctrlSlot).Mask&0x7ffu;
        uint bit=1u<<owner;
        document.SetCtrl(ctrlSlot,assigned?mask|bit:mask&~bit);
    }
}
