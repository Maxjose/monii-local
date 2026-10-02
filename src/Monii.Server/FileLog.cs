namespace Monii.Server;
public sealed class FileLog(string directory) : ILoggerProvider
{
    private readonly object gate=new();
    private readonly string logDirectory=directory;
    public ILogger CreateLogger(string categoryName)=>new Writer(this,categoryName);
    public void Dispose() { }
    private sealed class Writer(FileLog owner,string category) : ILogger
    {
        public IDisposable? BeginScope<TState>(TState state) where TState:notnull=>null;
        public bool IsEnabled(LogLevel level)=>level>=LogLevel.Warning||category=="Microsoft.Hosting.Lifetime";
        public void Log<TState>(LogLevel level,EventId id,TState state,Exception? error,Func<TState,Exception?,string> formatter)
        {
            if(!IsEnabled(level))return;
            try { lock(owner.gate) { Directory.CreateDirectory(owner.logDirectory);File.AppendAllText(Path.Combine(owner.logDirectory,"server-"+DateTime.UtcNow.ToString("yyyyMMdd")+".log"),$"{DateTimeOffset.UtcNow:O} {level} {category}: {formatter(state,error)} {error}\n"); } }
            catch(IOException) { } catch(UnauthorizedAccessException) { }
        }
    }
}
