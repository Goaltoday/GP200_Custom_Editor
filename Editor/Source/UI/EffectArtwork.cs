using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace Gp200.Replica;
public static class EffectArtwork
{
    static readonly Dictionary<string,BitmapSource> cache=[];
    public static string Root=>Path.Combine(AppContext.BaseDirectory,"Data","OfficialImages");
    public static string EffectPath(AlgorithmInfo algorithm)=>Path.Combine(Root,"effects_bg",algorithm.Module=="NAM"?"AMP":algorithm.Module,algorithm.ResourceName+".png");
    public static string ChainPath(string module,bool enabled,bool selected)=>Path.Combine(Root,"Background",$"chain_icon_{module}_{(enabled?"on":"off")}{(selected?"_selected":"")}.png");
    public static Image? Load(string path,double height)
    {
        if(!File.Exists(path))return null;
        if(!cache.TryGetValue(path,out var source))
        {
            if(cache.Count>=48)cache.Clear();
            var bitmap=new BitmapImage();bitmap.BeginInit();bitmap.CacheOption=BitmapCacheOption.OnLoad;bitmap.DecodePixelHeight=(int)(height*2);bitmap.UriSource=new Uri(path);bitmap.EndInit();bitmap.Freeze();source=bitmap;
            if(Path.GetFileName(path).StartsWith("chain_icon_"))
            {
                // Official chain assets include a generous transparent canvas.
                // Fit the artwork itself to the card, preserving aspect ratio.
                var pixels=new FormatConvertedBitmap(bitmap,PixelFormats.Bgra32,null,0);int w=pixels.PixelWidth,h=pixels.PixelHeight,stride=w*4;
                byte[] bytes=new byte[stride*h];pixels.CopyPixels(bytes,stride,0);int left=w,top=h,right=-1,bottom=-1;
                for(int y=0;y<h;y++)for(int x=0;x<w;x++)if(bytes[y*stride+x*4+3]>16){left=Math.Min(left,x);top=Math.Min(top,y);right=Math.Max(right,x);bottom=Math.Max(bottom,y);}
                if(right>=left){source=new CroppedBitmap(bitmap,new Int32Rect(left,top,right-left+1,bottom-top+1));source.Freeze();}
            }
            cache[path]=source;
        }
        return new Image{Source=source,Height=height,Stretch=Stretch.Uniform,HorizontalAlignment=HorizontalAlignment.Left,Margin=new Thickness(0,0,0,8)};
    }
}
