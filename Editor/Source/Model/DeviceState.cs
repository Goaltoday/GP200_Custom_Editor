using System.Buffers.Binary;

namespace Gp200.Replica;

/// <summary>Known GP-200 fields only; no inference of firmware version.
/// Global TLV starts at full PresetManager+0x58; setter this is full+4.
/// Thus value offsets are setterOffset-0x58.</summary>
public sealed record GlobalSnapshot(ushort CurrentPreset,short MidiInputSource,short DinInputChannel,
    short UsbInputChannel,short DinOutputChannel,short UsbOutputChannel,short ClockSource,
    short DinClockOut,short UsbClockOut,short AutoCabMatch)
{
    public static GlobalSnapshot Parse(byte[] value)
    {
        if(value.Length!=72)throw new InvalidDataException(UiText.T("Snapshot global de longitud no reconocida."));
        short At(int offset)=>BinaryPrimitives.ReadInt16LittleEndian(value.AsSpan(offset,2));
        return new((ushort)At(4),At(28),At(30),At(32),At(34),At(36),At(38),At(40),At(42),At(70));
    }
    public static string Source(short value)=>value switch{0=>"DIN Only",1=>"USB Only",2=>"Mixed",_=>UiText.T($"Valor {value}")};
    public static string Clock(short value)=>value switch{0=>"Internal",1=>"DIN Only",2=>"USB Only",3=>"External",4=>"Mixed",_=>UiText.T($"Valor {value}")};
    public static string Channel(short value)=>value switch{-1=>"OFF",0=>"Omni",>=1 and <=16=>value.ToString(),_=>UiText.T($"Valor {value}")};
    public static string Toggle(short value)=>value switch{0=>"OFF",1=>"ON",_=>UiText.T($"Valor {value}")};
    public string Summary()=>UiText.T($"Preset seleccionado: {CurrentPreset/4+1:00}-{(char)('A'+CurrentPreset%4)}\n")+
        $"MIDI In Source: {Source(MidiInputSource)}\n"+
        $"Input CH DIN / USB: {Channel(DinInputChannel)} / {Channel(UsbInputChannel)}\n"+
        $"Output CH DIN / USB: {Channel(DinOutputChannel)} / {Channel(UsbOutputChannel)}\n"+
        $"Clock Source: {Clock(ClockSource)}\n"+
        $"Clock Out DIN / USB: {Toggle(DinClockOut)} / {Toggle(UsbClockOut)}\n"+
        $"Auto CAB Match: {Toggle(AutoCabMatch)}\n";
}
