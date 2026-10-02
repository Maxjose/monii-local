using System.Windows;
using System.Windows.Media;
using Monii.Domain;

namespace Monii.Desktop;
internal static class Theme
{
    private static readonly Dictionary<string,SolidColorBrush> Palette=[];
    public static bool IsDark { get; private set; }
    public static string Accent { get; private set; }="#0F766E";
    public static SolidColorBrush Resource(string key)
    {
        if(!Palette.TryGetValue(key,out var brush)) { brush=new SolidColorBrush(Colors.Transparent); Palette.Add(key,brush); }
        return brush;
    }
    public static Brush Resolve(string color)=>color.ToUpperInvariant() switch
    {
        "#172B3A"=>Resource("ThemeText"),"#64748B"=>Resource("ThemeMuted"),"#0F766E"=>Resource("ThemeAccentInk"),"#F3F6FA"=>Resource("ThemeCanvas"),"#FFFFFF"=>Resource("ThemeSurface"),
        "#B91C1C"=>Resource("ThemeError"),"#92400E"=>Resource("ThemeWarning"),_=>new SolidColorBrush((Color)ColorConverter.ConvertFromString(color))
    };
    public static Color Contrast(Color color)
    {
        return Luminance(color)>.179?Colors.Black:Colors.White;
    }
    private static double Luminance(Color color)
    {
        static double Linear(byte component) { var value=component/255.0; return value<=.04045?value/12.92:Math.Pow((value+.055)/1.055,2.4); }
        return .2126*Linear(color.R)+.7152*Linear(color.G)+.0722*Linear(color.B);
    }
    private static Color ReadableAccent(Color color,Color background)
    {
        var light=Luminance(background)<.5;
        for(var step=0;step<30;step++)
        {
            var first=Luminance(color); var second=Luminance(background); if((Math.Max(first,second)+.05)/(Math.Min(first,second)+.05)>=4.5) break;
            byte Adjust(byte value)=>(byte)(light?Math.Min(255,value+(255-value)/5+1):value*4/5);
            color=Color.FromRgb(Adjust(color.R),Adjust(color.G),Adjust(color.B));
        }
        return color;
    }
    private static Color TintSidebar(Color c)=>Color.FromRgb((byte)(16+c.R*.16),(byte)(16+c.G*.16),(byte)(16+c.B*.16));
    public static void Apply(BusinessSettings settings)
    {
        IsDark=settings.DarkMode; Accent=settings.AccentColor;
        Color accent; try { accent=(Color)ColorConverter.ConvertFromString(Accent); } catch(FormatException) { accent=(Color)ColorConverter.ConvertFromString("#0F766E"); }
        void Set(string key,string color)
        {
            var value=(Color)ColorConverter.ConvertFromString(color);
            if(Palette.TryGetValue(key,out var existing)&&existing.Color==value&&System.Windows.Application.Current.Resources.Contains(key)) return;
            var brush=new SolidColorBrush(value); Palette[key]=brush; System.Windows.Application.Current.Resources[key]=brush;
        }
        Set("ThemeSidebar",TintSidebar(accent).ToString()); Set("Accent",accent.ToString()); Set("AccentText",Contrast(accent).ToString());
        Color Tint(string basis,double amount) { var neutral=(Color)ColorConverter.ConvertFromString(basis); byte Mix(byte n,byte a)=>(byte)Math.Round(n*(1-amount)+a*amount); return Color.FromRgb(Mix(neutral.R,accent.R),Mix(neutral.G,accent.G),Mix(neutral.B,accent.B)); }
        Set("ThemeCanvas",Tint(IsDark?"#111111":"#F6F6F6",IsDark?.12:.05).ToString()); Set("ThemeSurface",Tint(IsDark?"#222222":"#FFFFFF",IsDark?.10:.025).ToString());
        Set("ThemeAccentInk",ReadableAccent(accent,Resource("ThemeCanvas").Color).ToString());
        Set("ThemeText",IsDark?"#F1F5F9":"#172B3A"); Set("ThemeMuted",IsDark?"#BAC6D5":"#64748B");
        Set("ThemeBorder",Tint(IsDark?"#555555":"#D5D5D5",.18).ToString()); Set("ThemeAlternate",Tint(IsDark?"#303030":"#FAFAFA",.08).ToString());
        Set("ThemeHeader",Tint(IsDark?"#383838":"#EEEEEE",.16).ToString()); Set("ThemeSelected",Tint(IsDark?"#444444":"#FFFFFF",.25).ToString());
        Set("ThemeError",IsDark?"#FCA5A5":"#B91C1C"); Set("ThemeWarning",IsDark?"#FCD34D":"#92400E");
    }
}
