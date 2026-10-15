using System.Collections;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Windows;
using System.Windows.Threading;
using RtlTerminal;

internal static partial class Program
{
    private static void CheckLivePaneSessions()
    {
        const BindingFlags flags = BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.DeclaredOnly;
        var window = new MainWindow();
        var tabs = (IList)typeof(MainWindow).GetField("_tabs", flags)!.GetValue(window)!;
        object? Call(string name, params object?[] args) => typeof(MainWindow).GetMethods(flags)
            .Single(m => m.Name == name && m.GetParameters().Length == args.Length).Invoke(window,args);
        object? Get(object o,string p) => o.GetType().GetProperty(p)!.GetValue(o);
        void Set(object o,string p,object? v) => o.GetType().GetProperty(p)!.SetValue(o,v);
        var tabType = typeof(MainWindow).GetNestedType("TerminalTab",BindingFlags.NonPublic)!;
        var profileType = typeof(MainWindow).GetNestedType("TerminalProfile",BindingFlags.NonPublic)!;
        var sessions = new List<ConPtySession>();
        var readers = new List<Task>();
        var root = (FrameworkElement)window.Content;
        void Pump()
        {
            root.Measure(new Size(980,620)); root.Arrange(new Rect(0,0,980,620)); root.UpdateLayout();
            root.Dispatcher.Invoke(() => {},DispatcherPriority.ContextIdle);
        }
        try
        {
            var panes = new List<object>();
            for(var i=0;i<2;i++)
            {
                var pane=Call("CreatePane",Enum.ToObject(profileType,0))!;
                var tab=Activator.CreateInstance(tabType,pane)!; tabs.Add(tab); panes.Add(pane);
                Call("ShowTab",tab); Pump();
                var session=new ConPtySession(80,20); sessions.Add(session);
                Set(pane,"Session",session); Set(pane,"Buffer",new TerminalBuffer(80,20));
                Set(pane,"CancellationTokenSource",new CancellationTokenSource());
                session.Start("dotnet \"" + typeof(ConPtyPersianProbe).Assembly.Location + "\" --conpty-child --shell-launch",Environment.CurrentDirectory);
                readers.Add(Task.Run(()=>Call("ReadOutputLoop",pane)));
            }
            string Text(object pane) => string.Concat(((TerminalBuffer)Get(pane,"Buffer")!).CaptureSnapshot().Lines.SelectMany(l=>l.Runs).Select(r=>r.Text));
            void WaitFor(Func<bool> condition)
            {
                var watch=Stopwatch.StartNew();
                while(!condition())
                {
                    Pump(); Thread.Sleep(15);
                    if(watch.Elapsed>TimeSpan.FromSeconds(10))throw new Exception("Live pane output timed out");
                }
                Pump();
            }
            sessions[0].Write("echo ALPHA_BEFORE_JOIN\r"); sessions[1].Write("echo BETA_BEFORE_JOIN\r");
            WaitFor(()=>Text(panes[0]).Contains("ALPHA_BEFORE_JOIN")&&Text(panes[1]).Contains("BETA_BEFORE_JOIN"));
            if(!(bool)Call("TryJoinTab",tabs[0],false)!)throw new Exception("Live panes could not be joined");
            sessions[0].Write("echo ALPHA_AFTER_JOIN\r"); sessions[1].Write("echo BETA_AFTER_JOIN\r");
            WaitFor(()=>Text(panes[0]).Contains("ALPHA_AFTER_JOIN")&&Text(panes[1]).Contains("BETA_AFTER_JOIN"));
            if(Text(panes[0]).Contains("BETA_AFTER_JOIN")||Text(panes[1]).Contains("ALPHA_AFTER_JOIN") ||
               !ReferenceEquals(Get(panes[0],"Session"),sessions[0]) || !ReferenceEquals(Get(panes[1],"Session"),sessions[1]))
                throw new Exception("Joining crossed shell input/output or replaced a running session");
            Console.WriteLine("PASS two real ConPTY shells retain independent input/output before and after joining tabs");
        }
        finally
        {
            // Keep readers draining while each native host shuts down.
            foreach(var pane in tabs.Cast<object>().SelectMany(t=>((IList)Get(t,"Panes")!).Cast<object>())) Set(pane,"Session",null);
            Task.WaitAll(sessions.Select(s=>Task.Run(s.Dispose)).ToArray(),TimeSpan.FromSeconds(10));
            window.Close();
            Task.WaitAll(readers.ToArray(),TimeSpan.FromSeconds(10));
        }
    }
}
