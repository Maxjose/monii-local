using Monii.Server;
try { await ServerHost.Run(args); }
catch(Exception error)
{
    Console.Error.WriteLine(error);Environment.ExitCode=1;
    try
    {
        var index=Array.IndexOf(args,"--data-dir");var data=index>=0&&index+1<args.Length?Path.GetFullPath(args[index+1]):Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),"MoniiServer","data");
        var logs=Path.Combine(Path.GetDirectoryName(data)!,"logs");Directory.CreateDirectory(logs);File.AppendAllText(Path.Combine(logs,"startup.log"),$"{DateTimeOffset.UtcNow:O} {error}\n");
    }
    catch(IOException) { }catch(UnauthorizedAccessException) { }
}
