using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Threading;

namespace Monii.Desktop;
public partial class MainWindow
{
    private void ScanProductCode(TextBox target)
    {
        var panel=new StackPanel { Margin=new Thickness(24) }; panel.Children.Add(Text("Escanear código",24));
        panel.Children.Add(Text("Apunta el lector al código y pulsa su gatillo. Compatible con lectores USB/Bluetooth en modo teclado, con terminación Enter o Tab.",14,"#64748B"));
        var input=new TextBox { Name="ScanInput",MaxLength=256 }; panel.Children.Add(input); var status=Text("Esperando el código…",13,"#64748B"); panel.Children.Add(status);
        var window=new Window { Title="Monii · Escanear código",Width=500,Height=340,ResizeMode=ResizeMode.NoResize,Owner=Window.GetWindow(target),WindowStartupLocation=WindowStartupLocation.CenterOwner,Content=panel };
        window.SetResourceReference(Window.BackgroundProperty,"ThemeCanvas"); var seconds=30;
        var timer=new DispatcherTimer { Interval=TimeSpan.FromSeconds(1) };
        timer.Tick+=(_,_)=> { seconds--; status.Text=$"Esperando el código… {seconds} s"; if(seconds<=0) { window.Close(); Status.Text="No se recibió un código. Pulsa Escanear para volver a intentarlo."; } };
        input.PreviewKeyDown+=(_,eventArgs)=>
        {
            if(eventArgs.Key is Key.Enter or Key.Tab)
            {
                eventArgs.Handled=true; var code=input.Text.Trim(); if(code.Length==0) { status.Text="El lector no envió un código. Inténtalo otra vez."; return; }
                target.Text=code; window.Close(); target.Focus(); target.CaretIndex=target.Text.Length; Status.Text="Código capturado. Completa los datos del producto antes de guardarlo.";
            }
        };
        panel.Children.Add(Button("Cancelar escaneo",()=>window.Close())); window.PreviewKeyDown+=(_,e)=> { if(e.Key==Key.Escape) window.Close(); };
        window.Loaded+=(_,_)=> { input.Focus(); Keyboard.Focus(input); timer.Start(); }; window.Closed+=(_,_)=>timer.Stop(); window.ShowDialog();
    }
}
