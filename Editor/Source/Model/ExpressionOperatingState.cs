namespace Gp200.Replica;
public sealed record ExpressionOperatingState(bool InternalEnabled,bool BankB,bool ExternalEnabled)
{
    public string InternalLabel=>InternalEnabled?(BankB?"B":"A"):"OFF";
    public string ExternalLabel=>ExternalEnabled?"ON":"OFF";
    public static ExpressionOperatingState From(PresetDocument preset,DeviceSettings settings)=>new(settings.Get(0x1007)[44]==0,preset.ExpressionBankB,settings.Global(12)==0);
}
