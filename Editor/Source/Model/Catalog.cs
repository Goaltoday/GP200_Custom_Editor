using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace Gp200.Replica;

public sealed record ParameterMenu(string Name,float Value);
public sealed record ParameterInfo(int Slot,string Name,string Kind,string Unit,string Range,string Default)
{
    public IReadOnlyDictionary<string,string> Attributes {get;init;}=new Dictionary<string,string>();
    public IReadOnlyList<ParameterMenu> Menus {get;init;}=[];
    public float Number(string key,float fallback)=>Attributes.TryGetValue(key,out var value)?Catalog.DefaultValue(value):fallback;
    public int Bind=>(int)Number("bind",-1);
    public bool Power=>Attributes.TryGetValue("SubType",out var subtype)&&subtype=="Power";
    public (float Minimum,float Maximum) Bounds(Effect effect)
    {
        if(!Power&&Bind>=0&&Bind<15&&effect.Parameters[Bind]>0)return(0,10);
        float low=Number("Dmin",0),high=Number("Dmax",100);return(Number("Min",low),Number("Max",high));
    }
    public string Display(float raw,Effect effect)
    {
        if(!float.IsFinite(raw))return raw.ToString("R",CultureInfo.InvariantCulture);
        var menu=Menus.FirstOrDefault(m=>m.Value==raw);if(Kind=="Combox"&&menu!=null)return menu.Name;
        if(!Power&&Bind>=0&&Bind<15&&effect.Parameters[Bind]>0)
        {
            string[] divisions=["1/1","1/2","1/2D","1/2T","1/4","1/4D","1/4T","1/8","1/8D","1/8T","1/16"];
            return divisions[Math.Clamp((int)Math.Ceiling(raw-.5f),0,10)];
        }
        var (min,max)=Bounds(effect);float value=raw;
        if(max!=min)value=(raw-min)/(max-min)*(Number("Dmax",max)-Number("Dmin",min))+Number("Dmin",min);
        if(raw==min&&Number("MinISOff",0)!=0)return "OFF";
        if(raw==min&&Number("MinISMute",0)!=0)return "Mute";
        int decimals=Number("valueType",0)==3?1:2;double scale=decimals==1?10:100;
        return (Math.Truncate(value*scale)/scale).ToString(decimals==1?"0.#":"0.##",CultureInfo.InvariantCulture)+(Unit.Length==0?"":UiText.T(" ")+Unit);
    }
}
public sealed record AlgorithmInfo(string Module,uint Code,string Name,IReadOnlyList<ParameterInfo> Parameters,uint? CabinetCode)
{
    public string ResourceName {get;init;}=Name;
    public int PhysicalSlot {get;init;}=-1;
    public int FamilyId {get;init;}=-1;
    public int Ordinal {get;init;}=-1;
    public bool IsNone {get;init;}
    public bool ParametersKnown {get;init;}=true;
}

public sealed partial class Catalog
{
    private readonly Dictionary<(string,uint),AlgorithmInfo> algorithms=[];
    public int Count=>algorithms.Count;
    public void UpdateSnapToneNames(IEnumerable<SnapToneSlot> slots)
    {
        var bank=slots.ToArray();if(bank.Length!=10||bank.Select(s=>s.Index).Distinct().Count()!=10||bank.Any(s=>s.Index is <0 or >9))throw new InvalidDataException("SnapTone catalog requires all ten slots.");
        foreach(var slot in bank)
        {
            var key=(slot.Index<5?"AMP":"DST",0x0f000000u+(uint)slot.Index);
            if(IsMx42)UpdateMxName(slot.Index<5?3:2,key.Item2,$"SnapTone {slot.Index%5+1} · "+(slot.State==1?UiText.T("vacío"):slot.Name.TrimEnd()));
            if(algorithms.TryGetValue(key,out var algorithm))algorithms[key]=algorithm with{Name=$"SnapTone {slot.Index%5+1} · "+(slot.State==1?UiText.T("vacío"):slot.Name.TrimEnd())};
        }
    }
    public void UpdateUserIrNames(IEnumerable<IrSlot> slots)
    {
        var bank=slots.ToArray();
        if(bank.Length!=20||bank.Select(s=>s.Index).Distinct().Count()!=20||bank.Any(s=>s.Index<0||s.Index>=20))
            throw new InvalidDataException(UiText.T("El catálogo IR no contiene los veinte slots."));
        foreach(var slot in bank)
        {
            var key=("CAB",0x0a100000u+(uint)slot.Index);
            if(IsMx42)UpdateMxName(5,key.Item2,$"USER IR {slot.Index+1:00} · "+(slot.State==1?UiText.T("vacío"):IrService.CanonicalName(slot.Name)));
            if(algorithms.TryGetValue(key,out var algorithm))
                algorithms[key]=algorithm with{Name=$"USER IR {slot.Index+1:00} · "+(slot.State==1?UiText.T("vacío"):slot.State==0?IrService.CanonicalName(slot.Name):UiText.T("estado desconocido"))};
        }
    }
    public Catalog(string path)
    {
        using var document=JsonDocument.Parse(File.ReadAllText(path));
        foreach(var item in document.RootElement.EnumerateArray())
        {
            if(item.GetProperty("file").GetString()!="algorithm.xml")continue;
            string module=item.GetProperty("module").GetString()!;
            if(!uint.TryParse(item.GetProperty("code").GetString(),out uint code))continue;
            var parameters=new List<ParameterInfo>();
            foreach(var parameter in item.GetProperty("parameters").EnumerateArray())
            {
                var attrs=parameter.GetProperty("attributes");
                string Get(string name)=>attrs.TryGetProperty(name,out var v)?v.GetString()??"":"";
                // Native default/value vectors are indexed by ID. idx is layout order.
                var id=Regex.Match(Get("ID"),@"^\s*([+-]?\d+)");
                if(!id.Success||!int.TryParse(id.Groups[1].Value,out int slot)||slot<0||slot>=15)continue;
                var menus=new List<ParameterMenu>();
                if(parameter.TryGetProperty("menus",out var menuItems))foreach(var menu in menuItems.EnumerateArray())
                {
                    var menuAttrs=menu.TryGetProperty("attributes",out var nested)?nested:menu;
                    if(menuAttrs.TryGetProperty("Name",out var label)&&(menuAttrs.TryGetProperty("TrueID",out var idValue)||menuAttrs.TryGetProperty("ID",out idValue)))menus.Add(new(label.GetString()??"",DefaultValue(idValue.GetString()??"0")));
                }
                parameters.Add(new(slot,Get("Name"),parameter.GetProperty("kind").GetString()!,Get("Suffix"),
                    $"{Get("Dmin")} … {Get("Dmax")}",Get("default")){Attributes=attrs.EnumerateObject().ToDictionary(a=>a.Name,a=>a.Value.GetString()??""),Menus=menus});
            }
            uint? cabinet=null;
            if(item.TryGetProperty("attributes",out var algorithmAttrs)&&algorithmAttrs.TryGetProperty("CABCode",out var cab)&&uint.TryParse(cab.GetString(),out uint cabCode))cabinet=cabCode;
            algorithms.TryAdd((module,code),new(module,code,item.GetProperty("name").GetString()!,parameters,cabinet));
        }
    }
    public AlgorithmInfo? Find(int module,uint code)=>IsMx42&&module is >=0 and <11?MxFind(module,code):module>=0&&module<PresetDocument.Modules.Length&&algorithms.TryGetValue((PresetDocument.Modules[module],code),out var value)?value:null;
    public AlgorithmInfo[] ForModule(int module)=>IsMx42?ForFamily(module,module):algorithms.Values.Where(a=>a.Module==PresetDocument.Modules[module]).ToArray();
    public static float DefaultValue(string text)
    {
        // JUCE accepts a leading numeric token, including "-25 Cents" in stock XML.
        var prefix=Regex.Match(text,@"^\s*[+-]?(?:\d+(?:\.\d*)?|\.\d+)(?:[eE][+-]?\d+)?");
        float value=prefix.Success?float.Parse(prefix.Value,CultureInfo.InvariantCulture):0;
        if(!float.IsFinite(value))throw new InvalidDataException(UiText.T("Valor predeterminado no finito."));return value;
    }
}
