using AsyncResearch.AsyncExperiments.Chapter_6_AsyncAndGarbageCollector.Source;

namespace AsyncResearch
{
    public class Program
    {
        public static async Task Main(string[] args)
        { 
            await AsyncSurvivorExample.RunAsyncSurvivorExample();
        }
    }
}