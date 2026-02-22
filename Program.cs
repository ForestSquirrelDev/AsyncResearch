using AsyncResearch.Chapter_6_AsyncGcSurvivor.Source;

namespace AsyncResearch
{
    public class Program
    {
        public static async Task Main(string[] args)
        { 
            await FixedSystemTimerExample.RunFixedSystemTimerExample();
        }
    }
}