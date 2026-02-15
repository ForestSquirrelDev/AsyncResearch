using AsyncResearch.AsyncExperiments.Chapter_6_AsyncGcSurvivor.Source;

namespace AsyncResearch
{
    public class Program
    {
        public static async Task Main(string[] args)
        { 
            await FixedWhenAnyLeakExample.RunFixedWhenAnyLeakExample();
        }
    }
}