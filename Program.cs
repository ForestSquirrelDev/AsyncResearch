using AsyncResearch.AsyncExperiments.Chapter_4_AsyncVoid;

namespace AsyncResearch
{
    public class Program
    {
        public static void Main(string[] args)
        {
            AppDomain.CurrentDomain.UnhandledException += (sender, e) => 
            {
                Console.WriteLine($"\nCLR Caught unhandled exception!");
                Console.WriteLine($"Is process terminating? {e.IsTerminating}");
                Console.WriteLine($"Error: '{((Exception)e.ExceptionObject).Message}'");
            };
            ThreadPoolException.Test();
            while (true)
            {
                Console.WriteLine("Hello World!");
                Thread.Sleep(100);
            }
        }
    }
}