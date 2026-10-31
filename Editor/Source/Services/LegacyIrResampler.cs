using System.Runtime.InteropServices;

namespace Gp200.Replica;

/// <summary>Historic r8brain implementation matching native template24,673 and
/// ConvCountMax8. Exact comparison with official converted fixtures is pending.</summary>
public static class LegacyIrResampler
{
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    delegate int Resample([In]float[] source,int count,int rate,[Out]float[] output,int outputCount);
    static readonly Lazy<Resample> Function=new(()=>
    {
        string library=Path.Combine(AppContext.BaseDirectory,"Data","r8brain-gp200.dll");
        if(!File.Exists(library))throw new NotSupportedException(UiText.T("Falta el componente de remuestreo r8brain-gp200.dll. Usa el paquete completo del editor."));
        var handle=NativeLibrary.Load(library);return Marshal.GetDelegateForFunctionPointer<Resample>(NativeLibrary.GetExport(handle,"gp200_resample24"));
    });
    public static float[] Convert(float[] source,int rate,int count)
    {
        if(source.Length==0||count==0)return [];
        if(count is <0 or >1024)throw new ArgumentOutOfRangeException(nameof(count));
        float[] result=new float[count];int written=Function.Value(source,source.Length,rate,result,count);
        if(written!=count)throw new InvalidDataException(UiText.T($"El conversor IR no pudo completar el remuestreo ({written})."));return result;
    }
}
